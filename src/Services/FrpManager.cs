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
    /// - Puede descargar desde manifest.json (elige por arquitectura) o desde URL directa.
    /// - Guarda binarios en: %LOCALAPPDATA%\NuvAI FS\frpc\win-amd64 | win-arm64
    /// - Publica un proxy HTTP con subdominio único por instancia: <clientId>-<instanceId>.clients.nuvai.es
    /// - Usa nombre de proxy único en frps para evitar colisiones: [api-http-<sub>]
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class FrpManager : IDisposable
    {
        // ===== Defaults (Railway TCP Proxy) =====
        private const string DefaultFrpsHost = "yamanote.proxy.rlwy.net"; // host del TCP Proxy
        private const int DefaultFrpsPort = 39235;                     // puerto del TCP Proxy (control/bind)
        private const string DefaultFrpsToken = "p644xi8dyqtlmm5qnv7fanmsaj9pvo5kn8p"; // mismo token que en frps

        // Recomendado: usar manifest.json en R2 para no hardcodear SHA aquí
        private const string DefaultFrpcSource = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/frpc/manifest.json";

        // TLS del canal frpc<->frps (activado)
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
        private readonly int _frpsPort;
        private readonly string _frpsToken;
        private readonly int _localPort;

        private readonly string _frpcSourceUrl;  // manifest.json ó exe directo
        private readonly string _forcedShaHex;   // opcional; si se pasa, fuerza verificación
        private readonly bool _useTls;

        private readonly string _archTag;        // "win-amd64" | "win-arm64"
        private readonly string _workDir;
        private readonly string _exePath;
        private readonly string _iniPath;

        private readonly string _instanceId;     // sufijo estable por máquina/usuario

        private Process? _proc;

        private static readonly HttpClient s_http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // ===== Ctor "corto": defaults + instanceId autogenerado =====
        public FrpManager(string clientId, int localPort)
            : this(
                clientId,
                DefaultFrpsHost,
                DefaultFrpsPort,
                DefaultFrpsToken,
                localPort,
                DefaultFrpcSource,
                forcedSha256Hex: "",
                useTls: DefaultUseTls,
                instanceId: null // se generará estable
            )
        { }

        // ===== Ctor completo (permite overrides) =====
        public FrpManager(
            string clientId,
            string frpsHost,
            int frpsPort,
            string frpsToken,
            int localPort,
            string frpcSourceUrl,        // manifest.json o exe directo
            string forcedSha256Hex = "", // si se pasa, fuerza este SHA
            bool useTls = DefaultUseTls,
            string? instanceId = null    // si null, se genera y persiste
        )
        {
            _clientId = clientId;
            _frpsHost = frpsHost;
            _frpsPort = frpsPort;
            _frpsToken = frpsToken;
            _localPort = localPort;
            _frpcSourceUrl = frpcSourceUrl;
            _forcedShaHex = forcedSha256Hex?.Trim() ?? "";
            _useTls = useTls;
            _instanceId = SanitizeSuffix(instanceId) ?? GetOrCreateInstanceId();

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

        public string PublicUrl
            => $"https://{MakeSafeSubdomain(_clientId)}.clients.nuvai.es";

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

        public void Dispose()
        {
            try { _ = StopAsync(); } catch { }
        }

        // ===== Descarga + verificación =====
        private async Task EnsureBinaryAsync(CancellationToken ct)
        {
            Directory.CreateDirectory(_workDir);

            // Si ya existe y el SHA coincide (forced o manifest), no descargamos
            if (File.Exists(_exePath))
            {
                if (!string.IsNullOrWhiteSpace(_forcedShaHex))
                {
                    if (await VerifyShaAsync(_exePath, _forcedShaHex).ConfigureAwait(false)) return;
                }
                else if (IsManifestUrl(_frpcSourceUrl))
                {
                    var (_, manifestSha) = await ResolveFrpcFromManifestAsync(_frpcSourceUrl, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(manifestSha) &&
                        await VerifyShaAsync(_exePath, manifestSha!).ConfigureAwait(false))
                        return;
                }
                // Si no hay SHA, podríamos aceptar el existente. Para seguridad, lo forzamos a renovar.
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

            // Claves esperadas en manifest: "windows-amd64" | "windows-arm64"
            var key = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "windows-arm64"
                : "windows-amd64";

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
            // subdominio público = SOLO clientId
            var sub = MakeSafeSubdomain(_clientId);

            // nombre de proxy ÚNICO en frps (añadimos instanceId SOLO aquí)
            var proxyName = $"api-http-{sub}-{_instanceId}";  // p.ej. "api-http-29748-3ph7v"
            if (proxyName.Length > 200) proxyName = proxyName[..200];

            var logPath = Path.Combine(_workDir, "frpc.log").Replace("\\", "/");

            var sb = new StringBuilder();
            sb.AppendLine("[common]");
            sb.AppendLine($"server_addr = {_frpsHost}");
            sb.AppendLine($"server_port = {_frpsPort}");
            if (!string.IsNullOrWhiteSpace(_frpsToken))
                sb.AppendLine($"token = {_frpsToken}");
            if (_useTls)
                sb.AppendLine("tls_enable = true");

            // logging (opcional)
            sb.AppendLine($"log_file = {logPath}");
            sb.AppendLine("log_level = debug");
            sb.AppendLine("log_max_days = 3");

            sb.AppendLine();
            sb.AppendLine($"[{proxyName}]");   // nombre único del proxy
            sb.AppendLine("type = http");
            sb.AppendLine($"subdomain = {sub}");   // <<< URL pública sin sufijo
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

        // ==== InstanceId helpers ====
        private static string GetOrCreateInstanceId()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NuvAI FS"
                );
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "instance.id");

                if (File.Exists(path))
                {
                    var val = File.ReadAllText(path).Trim();
                    var cleaned = SanitizeSuffix(val);
                    if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned!;
                }

                var id = NewShortId(5); // 5-6 chars
                File.WriteAllText(path, id);
                return id;
            }
            catch
            {
                return NewShortId(5);
            }
        }

        private static string? SanitizeSuffix(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var sb = new StringBuilder(s.Trim().ToLowerInvariant().Length);
            foreach (var ch in s.Trim().ToLowerInvariant())
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-') sb.Append(ch);
            if (sb.Length == 0) return null;
            var str = sb.ToString();
            return str.Length > 12 ? str[..12] : str;
        }

        private static string NewShortId(int len)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz234567"; // base32 friendly
            Span<byte> rnd = stackalloc byte[8];
            RandomNumberGenerator.Fill(rnd);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(alphabet[rnd[i % rnd.Length] % alphabet.Length]);
            return sb.ToString();
        }
    }
}
