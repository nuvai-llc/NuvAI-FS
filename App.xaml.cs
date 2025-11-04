// App.xaml.cs
using NuvAI_FS.Infrastructure.Services;
using NuvAI_FS.Presentation.Views;
using NuvAI_FS.src.Common;
using NuvAI_FS.src.Presentation.Views;
using NuvAI_FS.src.Services;
using System.Runtime.Versioning;
using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {

        private const string LatestUrl = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/fs/latest.json";

        private async void OnStartup(object sender, StartupEventArgs e)
        {
            VelopackApp.Build().Run();

            var svc = new LicenseService();
            var (cid, key) = svc.LoadLicense();

            bool canContinue = false;

            if (!string.IsNullOrWhiteSpace(cid) && !string.IsNullOrWhiteSpace(key))
            {
                var res = await svc.CheckLicenseAsync(cid!, key!);
                var status = res.status?.ToLowerInvariant();

                if (status == "active")
                {
                    canContinue = true;
                }
                else
                {
                    ShowLicenseInvalidDialog(status);
                    svc.Clear();
                    canContinue = ShowLicenseDialog();
                }
            }
            else
            {
                canContinue = ShowLicenseDialog();
            }

            if (!canContinue)
            {
                Shutdown();
                return;
            }

            // ===== NUEVO: Primera vez -> SetupWindow antes del MainWindow =====
            var settings = new SettingsService();
            if (settings.IsFirstRun())
            {
                var setupOk = ShowSetupDialog();
                if (!setupOk)
                {
                    // Si cancelan el setup, puedes decidir cerrar o permitir seguir. Aquí cerramos.
                    Shutdown();
                    return;
                }
            }


            try
            {
                var upd = new UpdateService(LatestUrl);
                await upd.CheckAndPromptAsync(owner: null, currentVersion: AppInfo.InformationalVersion ?? string.Empty);
            }
            catch
            {

            }

            // Ahora sí, abre el MainWindow
            var mw = new MainWindow();
            MainWindow = mw;
            mw.Show();
        }

        private bool ShowLicenseDialog()
        {
            var prev = Current.ShutdownMode;
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var win = new LicenseWindow();
                var r = win.ShowDialog();
                return r == true;
            }
            finally
            {
                Current.ShutdownMode = prev;
            }
        }

        // NUEVO: diálogo modal de Setup
        private bool ShowSetupDialog()
        {
            var prev = Current.ShutdownMode;
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var win = new SetupWizardWindow();
                var r = win.ShowDialog();
                return r == true;
            }
            finally
            {
                Current.ShutdownMode = prev;
            }
        }

        private void ShowLicenseInvalidDialog(string? status)
        {
            string msg = status switch
            {
                "inactive" => "Tu licencia existe pero no está activa. Vuelve a introducir una licencia válida.",
                "revoked" => "Tu licencia ha sido revocada. Contacta con soporte si crees que es un error.",
                "expired" => "Tu licencia ha expirado. Necesitas renovarla para continuar.",
                null => "No hemos encontrado tu licencia o no es válida.",
                _ => "Tu licencia no es válida en este momento."
            };

            MessageBox.Show(
                msg,
                "Licencia no válida",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
    }
}
