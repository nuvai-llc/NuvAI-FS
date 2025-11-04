// src/Services/FrpManager.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NuvAI_FS.src.Services
{
    /// <summary>
    /// Gestiona frpc (descarga/verifica binario, genera INI y lanza el proceso).
    /// Descarga desde una URL directa o desde un manifest.json (elige por arquitectura).
    /// Guarda en: %LOCALAPPDATA%\NuvAI FS\frpc\win-amd64 | win-arm64
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class FrpManager : IDisposable
    {
        // ===== Defaults centralizados (Railway TCP Proxy) =====
        private const string DefaultFrpsHost = "yamanote.proxy.rlwy.net";    // host del TCP Proxy
        private const int DefaultFrpsPort = 39235;                         // puerto del TCP Proxy (control/bind)
        private const string DefaultFrpsToken = "p644xi8dyqtlmm5qnv7fanmsaj9pvo5kn8p"; // mismo token que en Railway

        // Puedes apuntar a un EXE directo o a un manifest.json
        // RECOMENDADO: manifest.json para no hardcodear SHA en el cliente
        private const string DefaultFrpcSource = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/frpc/manifest.json";

        // TLS del canal frpc<->frps (con Railway normalmente NO hace falta)
        private const bool DefaultUseTls = true;

        // ===== Modelos para leer el manifest =====
        private sealed class FrpcManifest
        {
            public string? Version { get; set; }
            public DateTimeOffset? PublishedAt { get; set; }
            public string? Notes { get; set; }
            public Dictionary<string, FrpcAsset>? Assets { get; set; }
        }

        private sealed class FrpcAsset
        {
            public string? Url { get; set; }
            public string? Sha256 { get; set; }
        }

        // ===== Campos =====
        private readonly string _clientId;
        private readonly string _frpsHost;
        private readonly int _frpsPort;        // Puerto de control/bind del FRPS (TCP Proxy)
        private readonly string _frpsToken;
        private readonly int _localPort;       // Puerto de tu API local (ej. 5137)

        // Fuente para obtener frpc:
        // - Si termina en .json => se tratará como manifest y se resolverá la URL/sha por arquitectura
        // - Si es .exe/.zip    => se descargará tal cual; si no hay SHA, no se verificará
        private readonly string _frpcSourceUrl;
        private readonly string _forcedShaHex;    // si no vacío, fuerza este SHA sobre el del manifest
        private readonly bool _useTls;

        private readonly string _archTag;         // "win-amd64" | "win-arm64"
        private readonly string _workDir;
        private readonly string _exePath;
        private readonly string _iniPath;

        private Process? _proc;

        private static readonly HttpClient s_http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // ===== Ctor "corto": usa defaults y sólo requiere clientId + localPort =====
        public FrpManager(string clientId, int localPort)
            : this(clientId, DefaultFrpsHost, DefaultFrpsPort, DefaultFrpsToken, localPort, DefaultFrpcSource, forcedSha256Hex: "", useTls: DefaultUseTls)
        {
        }

        // ===== Ctor completo (permite overrides) =====
        public FrpManager(
            string clientId,
            string frpsHost,
            int frpsPort,
            string frpsToken,
            int localPort,
            string frpcSourceUrl,          // puede ser EXE directo o manifest.json
            string forcedSha256Hex = "",   // si se pasa, fuerza verificación con este SHA
            bool useTls = DefaultUseTls)
        {
            _clientId = clientId;
            _frpsHost = frpsHost;
            _frpsPort = frpsPort;
            _frpsToken = frpsToken;
            _localPort = localPort;
            _frpcSourceUrl = frpcSourceUrl;
            _forcedShaHex = forcedSha256Hex?.Trim() ?? "";
            _useTls = useTls;

            _archTag = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "win-arm64",
                _ => "win-amd64"
            };

            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _workDir = Path.Combine(baseDir, "NuvAI FS", "frpc", _archTag);
            _exePath = Path.Combine(_workDir, "frpc.exe");
            _iniPath = Path.Combine(_workDir, "frpc.ini");
        }

        public string PublicUrl => $"https://{MakeSafeSubdomain(_clientId)}.clients.nuvai.es";

        // ===== Ciclo de vida =====
        public async Task StartAsync(CancellationToken ct = default)
        {
            await EnsureBinaryAsync(ct).ConfigureAwait(false);
            await WriteIniAsync().ConfigureAwait(false);

            if (_proc is { HasExited: false }) return;

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = $"-c {QuoteIfNeeded(_iniPath)}",
                WorkingDirectory = _workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine("[frpc] " + e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine("[frpc-err] " + e.Data); };
            _proc.Exited += (_, __) => Debug.WriteLine("[frpc] exited");

            if (!_proc.Start()) throw new InvalidOperationException("No se pudo iniciar frpc.");
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }

        public Task StopAsync()
        {
            try
            {
                if (_proc is { HasExited: false })
                {
                    _proc.Kill(entireProcessTree: true);
                    _proc.WaitForExit(2000);
                }
            }
            catch { }
            finally
            {
                try { _proc?.Dispose(); } catch { }
                _proc = null;
            }
            return Task.CompletedTask;
        }

        // ===== Descarga + verificación =====
        private async Task EnsureBinaryAsync(CancellationToken ct)
        {
            Directory.CreateDirectory(_workDir);

            // Si ya existe y el SHA coincide (forced o manifest), no descargamos
            if (File.Exists(_exePath))
            {
                // Si tenemos un SHA forzado, lo usamos
                if (!string.IsNullOrWhiteSpace(_forcedShaHex))
                {
                    if (await VerifyShaAsync(_exePath, _forcedShaHex).ConfigureAwait(false)) return;
                }
                else if (IsManifestUrl(_frpcSourceUrl))
                {
                    var (_, manifestSha) = await ResolveFrpcFromManifestAsync(_frpcSourceUrl, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(manifestSha) && await VerifyShaAsync(_exePath, manifestSha!).ConfigureAwait(false))
                        return;
                }
                else
                {
                    // No hay SHA para validar; si prefieres, puedes aceptar el binario existente
                    // return;
                }

                TryDelete(_exePath);
            }

            // Resolver URL y SHA
            string downloadUrl;
            string? shaHex = null;

            if (IsManifestUrl(_frpcSourceUrl))
            {
                (downloadUrl, shaHex) = await ResolveFrpcFromManifestAsync(_frpcSourceUrl, ct).ConfigureAwait(false);
            }
            else
            {
                downloadUrl = _frpcSourceUrl;
            }

            // Si hay forced SHA, prevalece
            if (!string.IsNullOrWhiteSpace(_forcedShaHex))
                shaHex = _forcedShaHex;

            // Descargar a tmp
            var tmp = Path.Combine(_workDir, "frpc.tmp");
            using (var resp = await s_http.GetAsync(downloadUrl, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            // Verificar SHA si está disponible
            if (!string.IsNullOrWhiteSpace(shaHex))
            {
                if (!await VerifyShaAsync(tmp, shaHex!).ConfigureAwait(false))
                {
                    TryDelete(tmp);
                    throw new InvalidOperationException("Checksum SHA-256 de frpc no coincide con el manifest.");
                }
            }

            File.Move(tmp, _exePath, overwrite: true);
        }

        private static bool IsManifestUrl(string url)
            => url.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        private async Task<(string downloadUrl, string? sha256)> ResolveFrpcFromManifestAsync(string manifestUrl, CancellationToken ct)
        {
            using var resp = await s_http.GetAsync(manifestUrl, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<FrpcManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest?.Assets is null)
                throw new InvalidOperationException("Manifest inválido: falta 'assets'.");

            var key = _archTag switch
            {
                "win-arm64" => "windows-arm64",
                _ => "windows-amd64"
            };

            if (!manifest.Assets.TryGetValue(key, out var asset) || string.IsNullOrWhiteSpace(asset?.Url))
                throw new InvalidOperationException($"Manifest no contiene asset '{key}' válido.");

            return (asset!.Url!, asset.Sha256);
        }

        private static async Task<bool> VerifyShaAsync(string path, string expectedHex)
        {
            if (string.IsNullOrWhiteSpace(expectedHex)) return true;
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs).ConfigureAwait(false);
            var hex = Convert.ToHexString(hash).ToUpperInvariant();
            return string.Equals(hex, expectedHex.Trim().ToUpperInvariant(), StringComparison.Ordinal);
        }

        // ===== INI de frpc =====
        private async Task WriteIniAsync()
        {
            var sub = MakeSafeSubdomain(_clientId);
            var sb = new StringBuilder();

            sb.AppendLine("[common]");
            sb.AppendLine($"server_addr = {_frpsHost}");
            sb.AppendLine($"server_port = {_frpsPort}");
            if (!string.IsNullOrWhiteSpace(_frpsToken))
                sb.AppendLine($"token = {_frpsToken}");
            if (_useTls)
                sb.AppendLine("tls_enable = true");

            sb.AppendLine();
            sb.AppendLine("[api-http]");
            sb.AppendLine("type = http");
            sb.AppendLine($"subdomain = {sub}");
            sb.AppendLine("local_ip = 127.0.0.1");
            sb.AppendLine($"local_port = {_localPort}");

            await File.WriteAllTextAsync(_iniPath, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
        }

        // ===== Utils =====
        private static string MakeSafeSubdomain(string s)
        {
            s ??= "cliente";
            s = s.Trim().ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch) || ch == '-') sb.Append(ch);
            return sb.Length == 0 ? "cliente" : sb.ToString();
        }

        private static string QuoteIfNeeded(string p) => p.Contains(' ') ? $"\"{p}\"" : p;

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public void Dispose()
        {
            try { _ = StopAsync(); } catch { }
        }
    }
}
