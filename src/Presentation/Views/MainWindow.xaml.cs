// MainWindow.xaml.cs
#nullable enable

using Microsoft.Win32;
using NuvAI_FS.Src.Services;
using NuvAI_FS.Src.Common;
using NuvAI_FS.Src.Presentation.Setup;
using NuvAI_FS.Src.Presentation.Views;
using NuvAI_FS.Src.Presentation.Views.Shared;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NuvAI_FS
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        // ===== Autoupdate =====
        private const string LatestUrl = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/fs/latest.json";
        private readonly UpdateService _updateService = new(LatestUrl);
        private bool _isUpdating;

        // ===== UI =====
        private static readonly SolidColorBrush LedGreen = Make("#FF2ECC71");
        private static readonly SolidColorBrush LedRed = Make("#FFE74C3C");
        private static readonly SolidColorBrush LedYellow = Make("#FFF1C40F");

        // ===== Servicios =====
        private ApiService? _apiService;
        private FrpManager? _frp;

        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"{AppInfo.ProductName} v{AppInfo.InformationalVersion}";
        }

        private static SolidColorBrush Make(string hex)
        {
            try
            {
                var obj = ColorConverter.ConvertFromString(hex);
                if (obj is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    if (brush.CanFreeze) brush.Freeze();
                    return brush;
                }
            }
            catch { /* noop */ }

            var fallback = new SolidColorBrush(Colors.Gray);
            if (fallback.CanFreeze) fallback.Freeze();
            return fallback;
        }

        // ===== Estados de servicio =====
        public enum ServiceState { Starting, Running, Error }

        public void SetServiceState(ServiceState state, string? message = null)
        {
            switch (state)
            {
                case ServiceState.Running:
                    Led.Fill = LedGreen;
                    InfoText.Text = string.IsNullOrWhiteSpace(message) ? "Corriendo" : message!;
                    break;
                case ServiceState.Error:
                    Led.Fill = LedRed;
                    InfoText.Text = string.IsNullOrWhiteSpace(message) ? "Error" : message!;
                    break;
                default:
                    Led.Fill = LedYellow;
                    InfoText.Text = string.IsNullOrWhiteSpace(message) ? "Iniciando…" : message!;
                    break;
            }
        }

        // ===== URL a partir del clientId (versión simple) =====
        public void SetClientUrl(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                TxtUrl.Text = "https://*.clients.nuvai.es";
                return;
            }

            var safe = new string(clientId.Trim().ToLowerInvariant()
                                  .Where(c => char.IsLetterOrDigit(c) || c == '-')
                                  .ToArray());
            if (string.IsNullOrWhiteSpace(safe))
                safe = "cliente";

            TxtUrl.Text = $"https://{safe}.clients.nuvai.es";
        }

        // ===== Copiar URL con un click =====
        private void TxtUrl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TxtUrl.IsKeyboardFocused)
            {
                e.Handled = true;
                TxtUrl.Focus();
                TxtUrl.SelectAll();
            }
        }

        private void TxtUrl_GotFocus(object sender, RoutedEventArgs e) => TxtUrl.SelectAll();

        private async void TxtUrl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(TxtUrl.Text))
                {
                    // Copiar URL
                    Clipboard.SetText(TxtUrl.Text);

                    // Quitar la selección del TextBox (deja el cursor al final)
                    TxtUrl.SelectionLength = 0;
                    TxtUrl.SelectionStart = TxtUrl.Text.Length;

                    // Guardar estado previo del InfoText
                    var prevText = InfoText.Text;
                    var prevBrush = InfoText.Foreground;

                    // Mostrar mensaje en verde
                    InfoText.Text = "URL copiada al portapapeles";
                    InfoText.Foreground = LedGreen;

                    await Task.Delay(2000);

                    // Restaurar solo si nadie más cambió el texto/estilo mientras tanto
                    if (InfoText.Text == "URL copiada al portapapeles")
                    {
                        InfoText.Text = prevText;
                        InfoText.Foreground = prevBrush;
                    }
                }
            }
            catch
            {
                InfoText.Text = "No se pudo copiar la URL";
                InfoText.Foreground = LedRed;
            }
        }


        // ===== Menú / Comandos =====
        private void Always_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void OpenSettings_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                var dlg = new SettingsWindow { Owner = this };
                _ = dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error abriendo configuración", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InvokeUpdate_Executed(object sender, ExecutedRoutedEventArgs e) => await RunUpdateFlowAsync();

        private void OpenHelp_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            MessageBox.Show(this,
                $"{AppInfo.ProductName}\nVersión: {AppInfo.InformationalVersion}\n\nSoporte: soporte@nuvai.es",
                "Ayuda", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // (legacy) por si lo llamas desde algún lado
        private async void UpdateBtn_Click(object sender, RoutedEventArgs e) => await RunUpdateFlowAsync();

        // ===== Orquestación de actualización =====
        private async Task RunUpdateFlowAsync()
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                UpdateService.CheckResult? check = null;

                await Dialogs.RunWithLoadingAsync(this, "Buscando actualizaciones...", async () =>
                {
                    check = await _updateService.CheckAsync(AppInfo.InformationalVersion ?? string.Empty);
                });

                if (check is null) return;

                switch (check.Outcome)
                {
                    case UpdateService.CheckOutcome.UpToDate:
                        MessageBox.Show(this, check.Message ?? "Ya estás en la última versión.",
                                        "Actualización", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;

                    case UpdateService.CheckOutcome.NoLatestInfo:
                        MessageBox.Show(this, check.Message ?? "No se pudo leer latest.json.",
                                        "Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;

                    case UpdateService.CheckOutcome.Error:
                        MessageBox.Show(this, check.Message ?? "Error al buscar actualización.",
                                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;

                    case UpdateService.CheckOutcome.NewVersionAvailable:
                        if (check.Latest is null)
                        {
                            MessageBox.Show(this, "Se detectó una versión nueva, pero falta información.",
                                            "Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        var latest = check.Latest;
                        var fecha = latest.publishedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "desconocida";
                        var notas = string.IsNullOrWhiteSpace(latest.notes) ? "—" : latest.notes;

                        var msg =
$@"Se ha encontrado una nueva versión:

Versión:   {latest.version}
Publicada: {fecha}
Notas:     {notas}

¿Deseas descargar e instalar ahora?
(la aplicación se reiniciará)";

                        var yes = MessageBox.Show(this, msg, "Nueva versión disponible",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

                        if (!yes) return;

                        UpdateService.ApplyResult? apply = null;
                        await Dialogs.RunWithLoadingAsync(this, "Descargando e instalando actualización...", async () =>
                        {
                            apply = await _updateService.DownloadAndApplyAsync(latest.baseUrl);
                        });

                        if (apply is null) return;

                        if (apply.Outcome == UpdateService.ApplyOutcome.StartedRestart)
                            return;

                        if (apply.Outcome == UpdateService.ApplyOutcome.NoUpdatesFound)
                        {
                            MessageBox.Show(this, apply.Message ?? "No hay actualizaciones disponibles.",
                                            "Actualización", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        MessageBox.Show(this, apply.Message ?? "Error al aplicar la actualización.",
                                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Error en actualización", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        // ===== Carga inicial =====
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetServiceState(ServiceState.Starting, "Iniciando…");

            // 1) Resolver URL (BaseUrl ó compuesta por clientId) y reflejar
            try
            {
                string? baseUrl = null;
                try { baseUrl = RegistryService.GetAppKeyString("BaseUrl"); } catch { /* noop */ }

                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    SetClientUrl(clientId: null, baseUrl: baseUrl);
                }
                else
                {
                    var clientId = ResolveClientId() ?? "cliente";
                    var composedUrl = BuildUrlFromClientId(clientId);
                    try { RegistryService.SetAppKey("BaseUrl", composedUrl); } catch { /* noop */ }
                    SetClientUrl(clientId: clientId, baseUrl: null);
                }
            }
            catch
            {
                SetClientUrl(clientId: null, baseUrl: null);
            }

            // 2) Iniciar API local y sondear /health
            try
            {
                SetServiceState(ServiceState.Starting, "Inicializando API…");

                _apiService = new ApiService(port: 5137);

                // (test visual) pequeña pausa
                await Task.Delay(3000);

                await _apiService.StartAsync();

                var healthy = await _apiService.WaitHealthyAsync(TimeSpan.FromSeconds(5));
                if (!healthy)
                {
                    SetServiceState(ServiceState.Error, "API iniciada pero sin respuesta /health");
                    return;
                }

                // 3) Levantar túnel FRP usando defaults del FrpManager
                SetServiceState(ServiceState.Starting, "API OK. Levantando túnel…");

                try
                {
                    var clientId = ResolveClientId() ?? "cliente";
                    var localPort = _apiService.Port;

                    _frp = new FrpManager(
                        clientId: clientId,
                        localPort: localPort
                    );

                    await _frp.StartAsync();

                    // Prioriza la URL pública del túnel en la UI
                    TxtUrl.Text = _frp.PublicUrl;

                    SetServiceState(ServiceState.Running, "Servicio activo (túnel OK)");
                }
                catch (Exception exTunnel)
                {
                    SetServiceState(ServiceState.Error, "Túnel frp no pudo iniciarse");
                    try { InfoText.Text = $"Error túnel: {exTunnel.Message}"; } catch { /* noop */ }
                }
            }
            catch (Exception ex)
            {
                SetServiceState(ServiceState.Error, "API no pudo iniciarse");
                try { InfoText.Text = $"Error API: {ex.Message}"; } catch { /* noop */ }
            }
        }

        // ===== Resolución de ClientId desde registro =====
        private static string? ResolveClientId()
        {
            try
            {
                var fromReg = RegistryService.GetAppKeyString("ClientId");
                if (!string.IsNullOrWhiteSpace(fromReg))
                    return fromReg.Trim();
            }
            catch { /* noop */ }
            return null;
        }

        // ===== Helpers =====
        private static string SanitizeClientId(string clientId)
        {
            var safe = new string((clientId ?? "")
                .Trim()
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(safe) ? "cliente" : safe;
        }

        private static string BuildUrlFromClientId(string clientId)
            => $"https://{SanitizeClientId(clientId)}.clients.nuvai.es";

        // Sobrecarga que acepta clientId o baseUrl explícita
        public void SetClientUrl(string? clientId, string? baseUrl)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var url = baseUrl.Trim();
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                TxtUrl.Text = url;
                return;
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                TxtUrl.Text = BuildUrlFromClientId(clientId);
                return;
            }

            TxtUrl.Text = "https://*.clients.nuvai.es";
        }

        // ===== Cierre limpio =====
        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try { if (_frp is not null) await _frp.StopAsync(); } catch { }
            try { if (_apiService is not null) await _apiService.StopAsync(); } catch { }
            base.OnClosing(e);
        }
    }
}
