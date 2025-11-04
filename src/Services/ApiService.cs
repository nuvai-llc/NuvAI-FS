// src/Services/ApiService.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // Para Application.Current y MessageBox (WPF)

namespace NuvAI_FS.src.Services
{
    /// <summary>
    /// API HTTP local muy ligera basada en HttpListener.
    /// Endpoints (mock): 
    ///  GET  /LeerRegistro
    ///  GET  /LeerConfiguracion
    ///  POST /CargaTabla
    ///  POST /LanzarConsulta
    ///  POST /EscribirRegistro
    ///  POST /ActualizarRegistro
    ///  GET  /BorrarRegistros
    ///  POST /ArticulosImagen
    ///  GET  /health
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class ApiService : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptLoop;
        private int _port;

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

            var prefix = $"http://127.0.0.1:{_port}/";
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(prefix);

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Si el puerto está ocupado, prueba puertos siguientes hasta 20 intentos
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
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { if (!_listener.IsListening) break; continue; }
                catch { continue; }

                _ = Task.Run(() => HandleRequestAsync(ctx!, ct), ct);
            }
        }

        private static void WriteCors(HttpListenerResponse resp)
        {
            resp.Headers["Access-Control-Allow-Origin"] = "*";
            resp.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            resp.Headers["Access-Control-Allow-Headers"] = "Content-Type,Authorization";
        }

        private static async Task WriteJsonAsync(HttpListenerResponse resp, object payload, int statusCode = 200)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.StatusCode = statusCode;
            WriteCors(resp);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            resp.OutputStream.Close();
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
        {
            using var sr = new System.IO.StreamReader(req.InputStream, req.ContentEncoding);
            return await sr.ReadToEndAsync().ConfigureAwait(false);
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            try
            {
                // Preflight CORS
                if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    WriteCors(resp);
                    resp.StatusCode = 204;
                    resp.OutputStream.Close();
                    return;
                }

                var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
                var method = req.HttpMethod.ToUpperInvariant();

                // Ruteo
                switch (path)
                {
                    case "/health" when method == "GET":
                        await WriteJsonAsync(resp, new { ok = true, service = "NuvAI_FS", ts = DateTimeOffset.UtcNow }).ConfigureAwait(false);
                        return;

                    case "/leerregistro" when method == "GET":
                        await WriteJsonAsync(resp, new { ok = true, endpoint = "LeerRegistro" }).ConfigureAwait(false);
                        return;

                    case "/leerconfiguracion" when method == "GET":
                        await WriteJsonAsync(resp, new { ok = true, endpoint = "LeerConfiguracion" }).ConfigureAwait(false);
                        return;

                    case "/borrarregistros" when method == "GET":
                        await WriteJsonAsync(resp, new { ok = true, endpoint = "BorrarRegistros" }).ConfigureAwait(false);
                        return;

                    case "/cargatabla" when method == "POST":
                        {
                            var body = await ReadBodyAsync(req).ConfigureAwait(false);
                            ShowPostBodyOnUiThread("CargaTabla", body);
                            await WriteJsonAsync(resp, new { ok = true, endpoint = "CargaTabla" }).ConfigureAwait(false);
                            return;
                        }

                    case "/lanzarconsulta" when method == "POST":
                        {
                            var body = await ReadBodyAsync(req).ConfigureAwait(false);
                            ShowPostBodyOnUiThread("LanzarConsulta", body);
                            await WriteJsonAsync(resp, new { ok = true, endpoint = "LanzarConsulta" }).ConfigureAwait(false);
                            return;
                        }

                    case "/escribirregistro" when method == "POST":
                        {
                            var body = await ReadBodyAsync(req).ConfigureAwait(false);
                            ShowPostBodyOnUiThread("EscribirRegistro", body);
                            await WriteJsonAsync(resp, new { ok = true, endpoint = "EscribirRegistro" }).ConfigureAwait(false);
                            return;
                        }

                    case "/actualizarregistro" when method == "POST":
                        {
                            var body = await ReadBodyAsync(req).ConfigureAwait(false);
                            ShowPostBodyOnUiThread("ActualizarRegistro", body);
                            await WriteJsonAsync(resp, new { ok = true, endpoint = "ActualizarRegistro" }).ConfigureAwait(false);
                            return;
                        }

                    case "/articulosimagen" when method == "POST":
                        {
                            var body = await ReadBodyAsync(req).ConfigureAwait(false);
                            ShowPostBodyOnUiThread("ArticulosImagen", body);
                            await WriteJsonAsync(resp, new { ok = true, endpoint = "ArticulosImagen" }).ConfigureAwait(false);
                            return;
                        }

                    default:
                        await WriteJsonAsync(resp, new { ok = false, error = "Not Found" }, 404).ConfigureAwait(false);
                        return;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await WriteJsonAsync(resp, new { ok = false, error = ex.Message }, 500).ConfigureAwait(false);
                }
                catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Close(); } catch { }
            _cts.Dispose();
        }

        // --- Helpers de estado/health ---
        public bool IsListening => _listener.IsListening;

        // HttpClient compartido para sondeo de salud
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
                    if (resp.IsSuccessStatusCode)
                        return true;
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

        // ===== UI helper para mostrar JSON entrante en POST =====
        private static void ShowPostBodyOnUiThread(string endpoint, string bodyRaw)
        {
            // Prettify JSON si es posible, con límite de longitud
            string display;
            try
            {
                using var doc = JsonDocument.Parse(bodyRaw);
                display = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                display = bodyRaw;
            }

            const int max = 4000; // límite para el diálogo
            if (display.Length > max)
            {
                display = display.Substring(0, max) + "\n\n[... truncado ...]";
            }

            var title = $"POST {endpoint}";
            var text = string.IsNullOrWhiteSpace(display) ? "(cuerpo vacío)" : display;

            // Si hay Dispatcher (WPF), muestra MessageBox en UI; si no, traza
            var app = Application.Current;
            if (app?.Dispatcher is not null)
            {
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[ApiService] Error mostrando MessageBox: " + ex.Message);
                    }
                }));
            }
            else
            {
                Debug.WriteLine($"[ApiService] {title}\n{text}");
            }
        }
    }
}
