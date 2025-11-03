using NuvAI_FS.src.Common;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    public partial class MainWindow : Window
    {
        private const string LatestUrl = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/fs/latest.json";

        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"{AppInfo.ProductName} v{AppInfo.InformationalVersion}";
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateBtn.IsEnabled = false;
            try
            {
                var latest = await FetchLatestAsync();
                if (latest == null || string.IsNullOrWhiteSpace(latest.version) || string.IsNullOrWhiteSpace(latest.baseUrl))
                {
                    MessageBox.Show("No se pudo leer latest.json.");
                    return;
                }

                // Compara InformationalVersion (SemVer típico X.Y.Z[-...]) con latest.version
                if (!IsNewer(latest.version, AppInfo.InformationalVersion))
                {
                    MessageBox.Show("Ya estás en la última versión.");
                    return;
                }

                // Apunta el UpdateManager a la carpeta de esa versión (que contiene releases.win.json + .nupkg)
                var mgr = new UpdateManager(latest.baseUrl);

                var info = await mgr.CheckForUpdatesAsync();
                if (info == null)
                {
                    MessageBox.Show("No hay actualizaciones disponibles.");
                    return;
                }

                await mgr.DownloadUpdatesAsync(info);
                mgr.ApplyUpdatesAndRestart(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error al actualizar");
            }
            finally
            {
                UpdateBtn.IsEnabled = true;
            }
        }

        private static bool IsNewer(string latest, string current)
        {
            // Comparación sencilla de SemVer (X.Y.Z[-...]); ignora metadatos
            string Normalize(string v)
            {
                var p = v.Split('-', '+')[0]; // quita pre-release/metadata
                return p;
            }

            var lv = Normalize(latest);
            var cv = Normalize(current);

            if (Version.TryParse(lv, out var l) && Version.TryParse(cv, out var c))
                return l > c;

            // Fallback: compara texto si no es estricto SemVer numérico
            return string.CompareOrdinal(latest, current) > 0;
        }

        private static async Task<LatestModel?> FetchLatestAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            var json = await http.GetStringAsync(LatestUrl);
            return JsonSerializer.Deserialize<LatestModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private record LatestModel(string version, string baseUrl, DateTime? publishedAt, string? notes);

        private void SubmitBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Hola, {MainTxt.Text}");
        }
    }
}
