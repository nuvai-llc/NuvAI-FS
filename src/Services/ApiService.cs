// Src/Services/ApiService.cs
#nullable enable
// Router + contratos + endpoints
using NuvAI_FS.Api.Endpoints;
using NuvAI_FS.Src.Api.Abstractions;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using CoreServices = NuvAI_FS.Src.Services;

namespace NuvAI_FS.Src.Services
{
    /// <summary>
    /// API HTTP local muy ligera basada en HttpListener.
    /// Host/Demux: levanta listener, hace CORS y delega en EndpointRouter.
    /// Endpoints GET simples (health, etc.) se responden inline aquí.
    /// El resto se registran como IApiEndpoint (ver carpeta Api/Endpoints).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class ApiService : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptLoop;
        private int _port;

        // Router de endpoints
        private EndpointRouter? _router;

        public int Port => _port;
        public string BaseUrl => $"http://127.0.0.1:{_port}/";

        public ApiService(int port = 5137)
        {
            if (port <= 0 || port > 65535) port = 5137;
            _port = port;
        }

        public async Task StartAsync()
        {
            if (_listener.IsListening) return;

            // ===== Construcción de servicios compartidos para endpoints =====
            // ACCDB desde el registro (tu Setup lo guarda en HKCU\Software\<Company>\<Product>\DatabasePath)
            var accdbPath = CoreServices.RegistryService.GetAppKeyString("DatabasePath");
            if (string.IsNullOrWhiteSpace(accdbPath) || !System.IO.File.Exists(accdbPath))
                throw new InvalidOperationException("ACCDB no configurada. Ejecuta el Setup para fijar 'DatabasePath'.");

            var db = new CoreServices.OleDbAccdbService(accdbPath);
            var ui = new CoreServices.WpfUiNotifier();

            // Registrar endpoints (añade aquí los nuevos cuando los crees)
            _router = new EndpointRouter(new IApiEndpoint[]
            {
                new CargaTablaEndpoint(db, ui),
                // new LanzarConsultaEndpoint(db, ui),
                // new EscribirRegistroEndpoint(db, ui),
                // new ActualizarRegistroEndpoint(db, ui),
                // new ArticulosImagenEndpoint(db, ui),
            });

            // ===== Arranque del listener con fallback de puertos =====
            var prefix = $"http://127.0.0.1:{_port}/";
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(prefix);

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Si el puerto está ocupado o requiere permisos, probamos puertos sucesivos (hasta 20)
                if (ex.ErrorCode == 32 /* sharing violation */ || ex.ErrorCode == 5 /* access denied */)
                {
                    var tried = 0;
                    while (tried++ < 20)
                    {
                        _port++;
                        prefix = $"http://127.0.0.1:{_port}/";
                        _listener.Prefixes.Clear();
                        _listener.Prefixes.Add(prefix);
                        try { _listener.Start(); break; }
                        catch { /* sigue probando */ }
                    }
                    if (!_listener.IsListening) throw;
                }
                else throw;
            }

            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            try { _cts.Cancel(); } catch { }
            try { if (_listener.IsListening) _listener.Stop(); } catch { }
            if (_acceptLoop is not null)
            {
                try { await _acceptLoop.ConfigureAwait(false); } catch { }
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext? raw = null;
                try
                {
                    raw = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { if (!_listener.IsListening) break; continue; }
                catch { continue; }

                _ = Task.Run(() => HandleRequestAsync(raw!, ct), ct);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext raw, CancellationToken ct)
        {
            var ctx = new ApiContext(raw);

            try
            {
                // Preflight CORS
                if (ctx.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    ApiContext.WriteCors(ctx.Response);
                    ctx.Response.StatusCode = 204;
                    ctx.Response.OutputStream.Close();
                    return;
                }

                var method = ctx.Request.HttpMethod.ToUpperInvariant();
                var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
                if (string.IsNullOrEmpty(path)) path = "/";

                // ===== Endpoints GET sencillos inline =====
                if (method == "GET" && path == "/health")
                {
                    await ctx.WriteJsonAsync(new { ok = true, service = "NuvAI_FS", ts = DateTimeOffset.UtcNow });
                    return;
                }
                if (method == "GET" && path == "/leerregistro")
                {
                    await ctx.WriteJsonAsync(new { ok = true, endpoint = "LeerRegistro" });
                    return;
                }
                if (method == "GET" && path == "/leerconfiguracion")
                {
                    await ctx.WriteJsonAsync(new { ok = true, endpoint = "LeerConfiguracion" });
                    return;
                }
                if (method == "GET" && path == "/borrarregistros")
                {
                    await ctx.WriteJsonAsync(new { ok = true, endpoint = "BorrarRegistros" });
                    return;
                }

                // ===== Delegación a router para el resto =====
                var ep = _router?.Resolve(method, path);
                if (ep is null)
                {
                    await ctx.WriteJsonAsync(new { ok = false, error = "Not Found" }, 404);
                    return;
                }

                await ep.HandleAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                try { await ctx.WriteJsonAsync(new { ok = false, error = ex.Message }, 500); }
                catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Close(); } catch { }
            _cts.Dispose();
        }

        // --- Health helpers ---
        public bool IsListening => _listener.IsListening;

        private static readonly HttpClient s_http = new()
        {
            Timeout = TimeSpan.FromMilliseconds(1500)
        };

        /// <summary>
        /// Espera hasta que /health responda OK o agota el timeout.
        /// </summary>
        public async Task<bool> WaitHealthyAsync(TimeSpan timeout, TimeSpan? pollInterval = null, CancellationToken ct = default)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            var interval = pollInterval ?? TimeSpan.FromMilliseconds(300);
            var url = $"{BaseUrl}health";

            while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                try
                {
                    using var resp = await s_http.GetAsync(url, ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode) return true;
                }
                catch
                {
                    // ignoramos y reintentamos
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;

                await Task.Delay(remaining > interval ? interval : remaining, ct).ConfigureAwait(false);
            }

            return false;
        }
    }
}
