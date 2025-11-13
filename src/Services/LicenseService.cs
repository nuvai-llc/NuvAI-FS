// Src\Services\LicenseService.cs
using Microsoft.Win32;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace NuvAI_FS.Src.Services
{
    public sealed class LicenseService
    {
        private static bool IsWindows() => OperatingSystem.IsWindows();

        private static readonly string Company =
       Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
       ?? "NuvAI LLC";

        private static readonly string Product =
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? "NuvAI FS";

        // HKCU\Software\<Company>\<Product>
        private static readonly string RegBasePath = $@"Software\{Company}\{Product}";

        private const string RegNameClientId = "ClientId";
        private const string RegNameLicense = "LicenseKey";
        public sealed class LicenseValidationResult
        {
            public string? client_id { get; set; }
            public string? license_key { get; set; }
            public string? status { get; set; }   // active | inactive | revoked | expired | null (no encontrada)
            public int? usage { get; set; }   // 0 cuando libre, >0 en uso
            public int? code { get; set; }   // 402 no coincide; 200 éxito; -1 error cliente
        }

        private static readonly JsonSerializerOptions s_jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Reutiliza HttpClient (evita sockets en TIME_WAIT y mejora rendimiento)
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
        private readonly string _baseUrl;

        public sealed class LicenseCheckResult
        {
            public string? status { get; set; } // "active" | "inactive" | "revoked" | "expired" | null
            public int? code { get; set; }      // 200 OK, 402 no encontrada, etc. (opcional)
        }

        public LicenseService(string? backendBaseUrl = null)
        {
            _baseUrl = (backendBaseUrl ?? Environment.GetEnvironmentVariable("BACKEND_BASE_URL") ?? "https://license.nuvai.es/").TrimEnd('/');
        }

        // ========== PÚBLICO (se mantienen las firmas) ==========

        // 1) Validar licencia contra el backend
        public async Task<LicenseValidationResult> ValidateLicenseAsync(string clientId, string licenseKey)
            => await ValidateCoreAsync(NormalizeClientId(clientId), NormalizeLicenseKey(licenseKey)).ConfigureAwait(false);

        // 2) Activar licencia y persistir en registro
        public async Task<bool> ActivateLicenseAsync(string clientId, string licenseKey)
        {
            var url = $"{_baseUrl}/activate";
            try
            {
                var payload = new { clientId = NormalizeClientId(clientId), licenseKey = NormalizeLicenseKey(licenseKey) };
                using var resp = await PostJsonWithRetryAsync(url, payload).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return false;

                if (OperatingSystem.IsWindows())
                {
                    SaveLicense(RegNameClientId, clientId);
                    SaveLicense(RegNameLicense, licenseKey);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] ActivateLicenseAsync error: {ex.Message}");
                return false;
            }
        }


        // 3) Cargar par(es) del registro
        public (string? ClientId, string? LicenseKey) LoadLicense()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return (null, null);

                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                if (reg == null) return (null, null);

                var clientId = reg.GetValue(RegNameClientId) as string;
                var licenseKey = reg.GetValue(RegNameLicense) as string;

                clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
                licenseKey = string.IsNullOrWhiteSpace(licenseKey) ? null : licenseKey.Trim();

                return (clientId, licenseKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] LoadLicense error: {ex.Message}");
                return (null, null);
            }
        }

        // 4) Check en arranque (solo status). No mira 'usage'.
        public async Task<LicenseCheckResult> CheckLicenseAsync(string clientId, string licenseKey)
        {
            var url = $"{_baseUrl}/check";
            try
            {
                var payload = new { clientId = NormalizeClientId(clientId), licenseKey = NormalizeLicenseKey(licenseKey) };
                using var resp = await PostJsonWithRetryAsync(url, payload).ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.NoContent)
                    return new LicenseCheckResult { code = 402, status = null };

                if (!resp.IsSuccessStatusCode)
                    return new LicenseCheckResult { code = (int)resp.StatusCode, status = null };

                var parsed = await SafeReadJsonAsync<LicenseCheckResult>(resp).ConfigureAwait(false);
                if (parsed == null)
                    return new LicenseCheckResult { code = -1, status = null };

                parsed.code ??= 200;
                return parsed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] CheckLicenseAsync error: {ex.Message}");
                return new LicenseCheckResult { code = -1, status = null };
            }
        }

        // 5) Guardar par(es) en registro
        public bool SaveLicense(string key, string value)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return false; // o true si quieres no fallar en otras plataformas

                using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
                reg?.SetValue(key, value ?? string.Empty, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] SaveLicense error: {ex.Message}");
                return false;
            }
        }

        // ========== PRIVADO (optimización interna) ==========

        private static string NormalizeClientId(string s) => (s ?? string.Empty).Trim();
        private static string NormalizeLicenseKey(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();

        // Core común para Validate/Check
        private async Task<LicenseValidationResult> ValidateCoreAsync(string clientId, string licenseKey)
        {
            var url = $"{_baseUrl}/validate";
            try
            {
                var payload = new { clientId, licenseKey };
                using var resp = await PostJsonWithRetryAsync(url, payload).ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.NoContent)
                    return new LicenseValidationResult { code = 402, status = null };

                if (!resp.IsSuccessStatusCode)
                    return new LicenseValidationResult { code = (int)resp.StatusCode, status = "error" };

                var parsed = await SafeReadJsonAsync<LicenseValidationResult>(resp).ConfigureAwait(false);
                if (parsed == null)
                    return new LicenseValidationResult { code = -1, status = "error" };

                parsed.code ??= 200; // si el backend no manda "code", asumimos 200 en éxito
                return parsed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] ValidateCoreAsync error: {ex.Message}");
                return new LicenseValidationResult { code = -1, status = "error" };
            }
        }

        // POST JSON con pequeño retry para errores transitorios (429/5xx)
        private async Task<HttpResponseMessage> PostJsonWithRetryAsync(string url, object payload, int maxRetries = 2)
        {
            for (int attempt = 0; ; attempt++)
            {
                // Crear el contenido en cada intento (importante)
                using var content = new StringContent(JsonSerializer.Serialize(payload, s_jsonOpts), Encoding.UTF8, "application/json");

                try
                {
                    var resp = await _http.PostAsync(url, content).ConfigureAwait(false);

                    if (IsTransient(resp.StatusCode) && attempt < maxRetries)
                    {
                        resp.Dispose();
                        await Task.Delay(300 * (attempt + 1)).ConfigureAwait(false); // backoff simple
                        continue;
                    }

                    return resp;
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    await Task.Delay(300 * (attempt + 1)).ConfigureAwait(false);
                    continue;
                }
            }
        }


        private static bool IsTransient(HttpStatusCode code)
            => code == HttpStatusCode.TooManyRequests || (int)code >= 500;

        private static async Task<T?> SafeReadJsonAsync<T>(HttpResponseMessage resp)
        {
            try
            {
                return await resp.Content.ReadFromJsonAsync<T>(s_jsonOpts).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] JSON parse error: {ex.Message}");
                return default;
            }
        }


        // Borra los valores guardados de licencia en HKCU\Software\<Company>\<Product>
        public bool Clear()
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return false;

                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: true);
                if (reg == null) return true; // nada que limpiar

                // Elimina solo las claves conocidas (no borra toda la subclave por si guardas más cosas)
                reg.DeleteValue(RegNameClientId, throwOnMissingValue: false);
                reg.DeleteValue(RegNameLicense, throwOnMissingValue: false);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LicenseService] Clear error: {ex.Message}");
                return false;
            }
        }

       
    }
}
