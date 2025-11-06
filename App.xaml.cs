// App.xaml.cs
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;
using System.Drawing;
using Velopack;

using NuvAI_FS.Src.Common;
using NuvAI_FS.Src.Infrastructure.Notifications;
using NuvAI_FS.Src.Infrastructure.Tray;
using NuvAI_FS.Src.Presentation.Views;
using NuvAI_FS.Src.Services;

namespace NuvAI_FS
{
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {
        private const string LatestUrl = "https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/fs/latest.json";

        private TrayService? _tray;
        private Mutex? _singleInstanceMutex;
        private bool _realExit;
        private static readonly string MutexName =
            @"Global\NuvAI_FS_{C7B3E8C8-1D0E-4C8A-9C79-0D5F8D0A1E12}";

        private Icon? TryLoadAppIcon()
        {
            // 1) Recurso pack (app.ico marcado como Resource)
            try
            {
                var uri = new Uri("pack://application:,,,/app.ico");
                var s = GetResourceStream(uri)?.Stream;
                if (s is not null) return new Icon(s);
            }
            catch { /* noop */ }

            // 2) Icono asociado al EXE (<ApplicationIcon> en .csproj)
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    return Icon.ExtractAssociatedIcon(exe);
            }
            catch { /* noop */ }

            // 3) Fallback
            return SystemIcons.Application;
        }

        private async void OnStartup(object sender, StartupEventArgs e)
        {
            // ---- Single instance guard ----
            bool createdNew;
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            // Updater runtime (Velopack)
            VelopackApp.Build().Run();

            // Notificaciones (no-op ahora, mantenido por compat)
            NotificationService.Initialize();

            // Licencia
            var licSvc = new LicenseService();
            var (cid, key) = licSvc.LoadLicense();
            bool canContinue;

            if (!string.IsNullOrWhiteSpace(cid) && !string.IsNullOrWhiteSpace(key))
            {
                var res = await licSvc.CheckLicenseAsync(cid!, key!);
                var status = res.status?.ToLowerInvariant();
                if (status == "active")
                {
                    canContinue = true;
                }
                else
                {
                    ShowLicenseInvalidDialog(status);
                    licSvc.Clear();
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

            // Setup inicial
            var settings = new SettingsService();
            if (settings.IsFirstRun())
            {
                var setupOk = ShowSetupDialog();
                if (!setupOk)
                {
                    Shutdown();
                    return;
                }
            }

            // Chequeo de actualización (no bloqueante)
            try
            {
                var upd = new UpdateService(LatestUrl);
                await upd.CheckAndPromptAsync(owner: null, currentVersion: AppInfo.InformationalVersion ?? string.Empty);
            }
            catch { /* noop */ }

            // MainWindow
            var mw = new MainWindow();
            MainWindow = mw;

            // Cierre/minimizado a bandeja (la salida real la ordena el tray)
            mw.StateChanged += (_, __) =>
            {
                if (mw.WindowState == WindowState.Minimized) mw.Hide();
            };
            mw.Closing += (s2, e2) =>
            {
                if (!_realExit)
                {
                    e2.Cancel = true;
                    mw.Hide();
                }
            };

            mw.Show();

            // Tray (icono explícito) + vincular notificaciones al mismo icono
            _tray = new TrayService(
                mainWindow: mw,
                openSettingsWindow: () => new SettingsWindow(),
                icon: TryLoadAppIcon(),
                tooltip: "NuvAI FS"
            );

            NotificationService.BindToTray(_tray.IconHandle);
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
            finally { Current.ShutdownMode = prev; }
        }

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
            finally { Current.ShutdownMode = prev; }
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

            MessageBox.Show(msg, "Licencia no válida", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            try { _tray?.Dispose(); } catch { /* noop */ }
            try { NotificationService.Dispose(); } catch { /* noop */ }
            try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { /* noop */ }
        }

        // Llamado por TrayService (o cuando quieras cierre real)
        public void RequestRealExit()
        {
            _realExit = true;
            Current.Shutdown();
        }
    }
}
