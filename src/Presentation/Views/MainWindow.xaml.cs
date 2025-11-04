using NuvAI_FS.src.Common;
using NuvAI_FS.src.Presentation.Views;
using NuvAI_FS.src.Presentation.Setup;
using NuvAI_FS.src.Presentation.Views.Shared; // Dialogs.RunWithLoadingAsync
using NuvAI_FS.src.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NuvAI_FS
{
    public partial class MainWindow : Window
    {
        // OJO: este es tu latest.json real
        private const string LatestUrl = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/fs/latest.json";

        private readonly UpdateService _updateService = new(LatestUrl);
        private bool _isUpdating;

        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"{AppInfo.ProductName} v{AppInfo.InformationalVersion}";
        }

        // ===== Menú / Comandos =====
        private void Always_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void OpenSettings_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                var dlg = new SetupWizardWindow { Owner = this };
                _ = dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error abriendo configuración", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InvokeUpdate_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            await RunUpdateFlowAsync();
        }

        private void OpenHelp_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            MessageBox.Show(this,
                $"{AppInfo.ProductName}\nVersión: {AppInfo.InformationalVersion}\n\nSoporte: soporte@nuvai.es",
                "Ayuda", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ===== Método legacy preservado (si en algún sitio aún lo llamas) =====
        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateFlowAsync();
        }

        // ===== Orquestación con dos diálogos de carga y confirmación intermedia =====
        private async Task RunUpdateFlowAsync()
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                UpdateService.CheckResult? check = null;

                // 1) Buscar actualización con LoadingDialog
                await Dialogs.RunWithLoadingAsync(this, "Buscando actualizaciones...", async () =>
                {
                    check = await _updateService.CheckAsync(AppInfo.InformationalVersion ?? string.Empty);
                });

                if (check is null) return;

                switch (check.Outcome)
                {
                    case UpdateService.CheckOutcome.UpToDate:
                        MessageBox.Show(this,
                            check.Message ?? "Ya estás en la última versión.",
                            "Actualización",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;

                    case UpdateService.CheckOutcome.NoLatestInfo:
                        MessageBox.Show(this,
                            check.Message ?? "No se pudo leer latest.json.",
                            "Actualización",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;

                    case UpdateService.CheckOutcome.Error:
                        MessageBox.Show(this,
                            check.Message ?? "Error al buscar actualización.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;

                    case UpdateService.CheckOutcome.NewVersionAvailable:
                        // 2) Confirmación del usuario mostrando versión/fecha/notas
                        if (check.Latest is null)
                        {
                            MessageBox.Show(this, "Se detectó una versión nueva, pero falta información.", "Actualización",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
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

                        // 3) Descarga + aplica con otro LoadingDialog
                        UpdateService.ApplyResult? apply = null;
                        await Dialogs.RunWithLoadingAsync(this, "Descargando e instalando actualización...", async () =>
                        {
                            apply = await _updateService.DownloadAndApplyAsync(latest.baseUrl);
                        });

                        // Normalmente no llegamos aquí si ApplyAndRestart se ejecutó
                        if (apply is null) return;

                        if (apply.Outcome == UpdateService.ApplyOutcome.StartedRestart)
                        {
                            // nada que hacer; la app debería reiniciarse
                            return;
                        }

                        if (apply.Outcome == UpdateService.ApplyOutcome.NoUpdatesFound)
                        {
                            MessageBox.Show(this,
                                apply.Message ?? "No hay actualizaciones disponibles.",
                                "Actualización",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return;
                        }

                        // Error
                        MessageBox.Show(this,
                            apply.Message ?? "Error al aplicar la actualización.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
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
    }
}
