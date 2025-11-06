// Src/Infrastructure/Notifications/NotificationService.cs
#nullable enable
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace NuvAI_FS.Src.Infrastructure.Notifications
{
    /// <summary>
    /// Notificaciones (balloon): reutiliza el NotifyIcon del tray si está disponible;
    /// si no, crea uno temporal por notificación y lo elimina tras el timeout.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class NotificationService
    {
        private static NotifyIcon? _hostIcon;

        public static void BindToTray(NotifyIcon hostIcon)
        {
            _hostIcon = hostIcon;
        }

        // No-op para compatibilidad: ya no crea icono persistente
        public static void Initialize(string? trayText = null, Icon? trayIcon = null) { }

        // Compat: no hay recursos persistentes que limpiar aquí
        public static void Dispose() { }

        public static void Show(string title, string message, int timeoutMs = 4000)
            => ShowBalloon(title, message, ToolTipIcon.Info, timeoutMs);

        public static void ShowInfo(string title, string message, int timeoutMs = 4000)
            => ShowBalloon(title, message, ToolTipIcon.Info, timeoutMs);

        public static void ShowWarning(string title, string message, int timeoutMs = 4000)
            => ShowBalloon(title, message, ToolTipIcon.Warning, timeoutMs);

        public static void ShowError(string title, string message, int timeoutMs = 4000)
            => ShowBalloon(title, message, ToolTipIcon.Error, timeoutMs);

        private static void ShowBalloon(string title, string message, ToolTipIcon icon, int timeoutMs)
        {
            var ms = Math.Clamp(timeoutMs, 1000, 10000);

            if (_hostIcon is not null)
            {
                try
                {
                    _hostIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "NuvAI FS" : title;
                    _hostIcon.BalloonTipText = message ?? string.Empty;
                    _hostIcon.BalloonTipIcon = icon;
                    _hostIcon.ShowBalloonTip(ms);
                    return;
                }
                catch { /* noop */ }
            }

            // Fallback temporal
            var temp = new NotifyIcon
            {
                Visible = true,
                Icon = SystemIcons.Application,
                Text = string.IsNullOrWhiteSpace(title) ? "NuvAI FS" : title
            };

            temp.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "NuvAI FS" : title;
            temp.BalloonTipText = message ?? string.Empty;
            temp.BalloonTipIcon = icon;
            temp.ShowBalloonTip(ms);

            var timer = new FormsTimer { Interval = ms + 500 };
            timer.Tick += (_, __) =>
            {
                try { temp.Visible = false; temp.Dispose(); } catch { }
                try { timer.Stop(); timer.Dispose(); } catch { }
            };
            timer.Start();
        }
    }
}
