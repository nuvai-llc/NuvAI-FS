// Src\Presentation\Views\MainWindow.xaml.cs
#nullable enable
using Microsoft.Win32;
using NuvAI_FS.Src.Common;
using NuvAI_FS.Src.Infrastructure.Notifications;
using NuvAI_FS.Src.Presentation.Views;
using NuvAI_FS.Src.Presentation.Views.Shared;
using NuvAI_FS.Src.Services;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NuvAI_FS
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window, NuvAI_FS.Src.Infrastructure.Tray.ITrayAware
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

        // ===== Retry loop =====
        private CancellationTokenSource? _startLoopCts;
        private Task? _startLoopTask;
        private int _attempt;

        // Cierre real (lo pedirá TrayService → ITrayAware)
        private bool _realExit = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"{AppInfo.ProductName} v{AppInfo.InformationalVersion}";

            // (Opcional) arranque con Windows
            EnsureStartupEntry();

            // Ocultar al minimizar
            this.StateChanged += (_, __) =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    try
                    {
                        ShowInTaskbar = false;
                        Hide();
                        NotificationService.ShowInfo("NuvAI FS", "Sigue ejecutándose en segundo plano.");
                    }
                    catch { /* noop */ }
                }
            };
        }

        // Implementación ITrayAware: el tray llama a esto para salir de verdad
        public void RequestRealExit()
        {
            _realExit = true;
            try { Close(); } catch { /* noop */ }
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
                    InfoText.ToolTip = string.IsNullOrWhiteSpace(message) ? "Corriendo" : message!;
                    break;
                case ServiceState.Error:
                    Led.Fill = LedRed;
                    InfoText.Text = string.IsNullOrWhiteSpace(message) ? "Error" : message!;
                    InfoText.ToolTip = string.IsNullOrWhiteSpace(message) ? "Error" : message!;
                    break;
                default:
                    Led.Fill = LedYellow;
                    InfoText.Text = string.IsNullOrWhiteSpace(message) ? "Iniciando…" : message!;
                    InfoText.ToolTip = string.IsNullOrWhiteSpace(message) ? "Iniciando…" : message!;
                    break;
            }
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
            catch { }
            var fallback = new SolidColorBrush(Colors.Gray);
            if (fallback.CanFreeze) fallback.Freeze();
            return fallback;
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

        // Copiar URL
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
                    Clipboard.SetText(TxtUrl.Text);
                    TxtUrl.SelectionLength = 0;
                    TxtUrl.SelectionStart = TxtUrl.Text.Length;

                    var prevText = InfoText.Text;
                    var prevBrush = InfoText.Foreground;

                    InfoText.Text = "URL copiada al portapapeles";
                    InfoText.Foreground = LedGreen;

                    await Task.Delay(2000);

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

        // Menú / comandos
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
                        if (apply.Outcome == UpdateService.ApplyOutcome.StartedRestart) return;

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
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Arrancamos un loop infinito de start/retry
            _startLoopCts?.Cancel();
            _startLoopCts = new CancellationTokenSource();
            _attempt = 0;

            _startLoopTask = StartOrRetryLoopAsync(_startLoopCts.Token);
        }

        private async Task StartOrRetryLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetServiceState(ServiceState.Starting, _attempt == 0 ? "Iniciando…" : $"Reintentando… (intento {_attempt + 1})");

                    // 1) Resolver URL (como ya hacías)
                    try
                    {
                        string? baseUrl = null;
                        try { baseUrl = RegistryService.GetAppKeyString("BaseUrl"); } catch { }

                        if (!string.IsNullOrWhiteSpace(baseUrl))
                        {
                            SetClientUrl(clientId: null, baseUrl: baseUrl);
                        }
                        else
                        {
                            var clientId = ResolveClientId() ?? "cliente";
                            var composedUrl = BuildUrlFromClientId(clientId);
                            try { RegistryService.SetAppKey("BaseUrl", composedUrl); } catch { }
                            SetClientUrl(clientId: clientId, baseUrl: null);
                        }
                    }
                    catch
                    {
                        SetClientUrl(clientId: null, baseUrl: null);
                    }

                    // 2) Intentar levantar servicios
                    await EnsureStoppedAsync(); // por si venimos de un intento previo

                    SetServiceState(ServiceState.Starting, "Inicializando API…");

                    _apiService = new ApiService(port: 5137);

                    // Si quieres mantener tu delay, ok, pero que sea cancelable
                    await Task.Delay(3000, ct);

                    await _apiService.StartAsync();
                    var healthy = await _apiService.WaitHealthyAsync(TimeSpan.FromSeconds(5));
                    if (!healthy)
                        throw new InvalidOperationException("API iniciada pero sin respuesta /health");

                    SetServiceState(ServiceState.Starting, "API OK. Levantando túnel…");

                    var clientId2 = ResolveClientId() ?? "cliente";
                    var localPort = _apiService.Port;

                    _frp = new FrpManager(clientId: clientId2, localPort: localPort);
                    await _frp.StartAsync();

                    TxtUrl.Text = _frp.PublicUrl;
                    TxtUrl.ToolTip = "Haz clic para copiar al portapapeles";

                    SetServiceState(ServiceState.Running, "Servicio activo (túnel OK)");
                    NotificationService.ShowInfo("Servicio activo", "El túnel está operativo.");

                    // Si llegó aquí, salimos del loop (todo OK)
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Mostramos error y reintentamos
                    SetServiceState(ServiceState.Error, $"Fallo al iniciar: {ex.Message}");

                    // Importante: limpiar lo que haya quedado a medias antes del siguiente intento
                    try { await EnsureStoppedAsync(); } catch { }

                    _attempt++;

                    var delay = ComputeBackoff(_attempt);
                    try
                    {
                        // Mensaje útil sin spam
                        InfoText.Text = $"Error: {ex.Message} | Reintento en {(int)delay.TotalSeconds}s";
                        InfoText.ToolTip = InfoText.Text;

                        await Task.Delay(delay, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private async Task EnsureStoppedAsync()
        {
            // Para orden: parar túnel primero, luego API
            try
            {
                if (_frp is not null)
                {
                    await _frp.StopAsync();
                    _frp = null;
                }
            }
            catch { /* noop */ }

            try
            {
                if (_apiService is not null)
                {
                    await _apiService.StopAsync();
                    _apiService = null;
                }
            }
            catch { /* noop */ }
        }

        private static TimeSpan ComputeBackoff(int attempt)
        {
            // No uses exponencial infinito: con túneles y DB suele ser “en algún momento aparece”
            if (attempt <= 0) return TimeSpan.FromSeconds(5);

            double seconds = attempt switch
            {
                1 => 5,
                2 => 10,
                3 => 20,
                4 => 30,
                5 => 45,
                _ => 60
            };

            return TimeSpan.FromSeconds(seconds);
        }

        // Resolución de ClientId
        private static string? ResolveClientId()
        {
            try
            {
                var fromReg = RegistryService.GetAppKeyString("ClientId");
                if (!string.IsNullOrWhiteSpace(fromReg))
                    return fromReg.Trim();
            }
            catch { }
            return null;
        }

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

        // Cierre: si no es “real”, oculta; si es real, para servicios y cierra
        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_realExit)
            {
                e.Cancel = true;
                try
                {
                    ShowInTaskbar = false;
                    Hide();
                }
                catch { }
                return;
            }

            // Cancelar loop de arranque/reintento
            try { _startLoopCts?.Cancel(); } catch { }

            try { await EnsureStoppedAsync(); } catch { }

            base.OnClosing(e);
        }

        // Inicio con Windows (opcional)
        private void EnsureStartupEntry()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exe)) return;

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key is null) return;

                var name = AppInfo.ProductName ?? "NuvAI FS";
                var existing = key.GetValue(name) as string;

                if (string.IsNullOrEmpty(existing) || !existing.Contains(exe, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(name, $"\"{exe}\"");
                }
            }
            catch { }
        }

        private void Restart_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout /t 1 /nobreak > NUL & start \"\" \"{exe}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch { /* noop */ }

            try { Close(); } catch { }
            try { Application.Current.Shutdown(); } catch { }
        }
    }
}
