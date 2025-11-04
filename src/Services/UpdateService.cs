using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Velopack;

namespace NuvAI_FS.src.Services
{
    /// <summary>
    /// Servicio reutilizable para gestionar actualizaciones con Velopack.
    /// - Lee latest.json (version, baseUrl, publishedAt, notes)
    /// - Compara con la versión actual (SemVer simple)
    /// - Permite orquestar descarga e instalación por separado
    /// </summary>
    public sealed class UpdateService
    {
        private readonly string _latestUrl;

        public UpdateService(string latestUrl)
        {
            _latestUrl = latestUrl ?? throw new ArgumentNullException(nameof(latestUrl));
        }

        public enum CheckOutcome
        {
            UpToDate,
            NewVersionAvailable,
            NoLatestInfo,
            Error
        }

        public enum ApplyOutcome
        {
            StartedRestart,     // normalmente no vuelve (ApplyAndRestart)
            NoUpdatesFound,     // el feed no reporta updates a pesar de latest
            Error
        }

        public sealed record LatestInfo(
            string version,
            string baseUrl,
            DateTime? publishedAt,
            string? notes
        );

        public sealed record CheckResult(
            CheckOutcome Outcome,
            LatestInfo? Latest,
            string? Message
        );

        public sealed record ApplyResult(
            ApplyOutcome Outcome,
            string? Message
        );

        /// <summary>
        /// Lee latest.json y compara con la versión actual. No descarga nada.
        /// </summary>
        public async Task<CheckResult> CheckAsync(string currentVersion)
        {
            try
            {
                var latest = await FetchLatestAsync();
                if (latest is null ||
                    string.IsNullOrWhiteSpace(latest.version) ||
                    string.IsNullOrWhiteSpace(latest.baseUrl))
                {
                    return new CheckResult(CheckOutcome.NoLatestInfo, null, "No se pudo leer latest.json.");
                }

                if (!IsNewer(latest.version, currentVersion))
                {
                    return new CheckResult(CheckOutcome.UpToDate,
                        new LatestInfo(latest.version, latest.baseUrl, latest.publishedAt, latest.notes),
                        "Ya estás en la última versión.");
                }

                return new CheckResult(CheckOutcome.NewVersionAvailable,
                    new LatestInfo(latest.version, latest.baseUrl, latest.publishedAt, latest.notes),
                    "Nueva versión disponible.");
            }
            catch (Exception ex)
            {
                return new CheckResult(CheckOutcome.Error, null, ex.ToString());
            }
        }

        /// <summary>
        /// Descarga y aplica la actualización desde un baseUrl concreto.
        /// </summary>
        public async Task<ApplyResult> DownloadAndApplyAsync(string baseUrl)
        {
            try
            {
                var mgr = new UpdateManager(baseUrl);

                var info = await mgr.CheckForUpdatesAsync();
                if (info == null)
                {
                    return new ApplyResult(ApplyOutcome.NoUpdatesFound, "No hay actualizaciones disponibles.");
                }

                await mgr.DownloadUpdatesAsync(info);

                // Normalmente no retorna: cierra y reinicia
                mgr.ApplyUpdatesAndRestart(info);

                return new ApplyResult(ApplyOutcome.StartedRestart, "Aplicando actualización y reiniciando...");
            }
            catch (Exception ex)
            {
                return new ApplyResult(ApplyOutcome.Error, ex.ToString());
            }
        }

        // ===== Helpers =====

        private async Task<LatestModel?> FetchLatestAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            var json = await http.GetStringAsync(_latestUrl);
            return JsonSerializer.Deserialize<LatestModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private static bool IsNewer(string latest, string current)
        {
            static string Normalize(string v)
            {
                var p = (v ?? string.Empty).Split('-', '+')[0]; // quita pre-release/metadata
                return p;
            }

            var lv = Normalize(latest);
            var cv = Normalize(current);

            if (Version.TryParse(lv, out var l) && Version.TryParse(cv, out var c))
                return l > c;

            // Fallback lexicográfico si no es un Version estricto
            return string.CompareOrdinal(latest ?? string.Empty, current ?? string.Empty) > 0;
        }

        // Debe mapear exactamente tu latest.json
        private sealed record LatestModel(
            string version,
            string baseUrl,
            DateTime? publishedAt,
            string? notes
        );

        public async Task<bool> CheckAndPromptAsync(Window? owner, string currentVersion)
        {
            var check = await CheckAsync(currentVersion);
            if (check.Outcome != CheckOutcome.NewVersionAvailable || check.Latest is null)
                return false;

            var latest = check.Latest;
            var fecha = latest.publishedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "desconocida";
            var notas = string.IsNullOrWhiteSpace(latest.notes) ? "—" : latest.notes;

            var msg =
                        $@"Hay una nueva versión disponible.

                Actual:   {currentVersion}
                Nueva:    {latest.version}
                Fecha:    {fecha}
                Notas:    {notas}

                ¿Quieres actualizar ahora? (la aplicación se reiniciará)";

            var result = owner is null
                ? MessageBox.Show(msg, "Actualización disponible", MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show(owner, msg, "Actualización disponible", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return false;

            var apply = await DownloadAndApplyAsync(latest.baseUrl);

            // Normalmente ApplyAndRestart no vuelve; si vuelve con error, informa.
            if (apply.Outcome == ApplyOutcome.Error)
            {
                if (owner is null)
                    MessageBox.Show(apply.Message ?? "Error al aplicar la actualización.",
                                    "Actualización", MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    MessageBox.Show(owner, apply.Message ?? "Error al aplicar la actualización.",
                                    "Actualización", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return true;
        }
    }
}
