// Src/Infrastructure/Tray/TrayService.cs
#nullable enable
using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace NuvAI_FS.Src.Infrastructure.Tray
{
    /// <summary>Gestiona el icono de bandeja y su menú.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _ctx;
        private bool _disposed;
        private readonly Window _mainWindow;
        private readonly Func<Window>? _openSettingsWindow;

        /// <summary>Exposición del icono para notificaciones (NotificationService.BindToTray).</summary>
        public NotifyIcon IconHandle => _icon;

        public TrayService(Window mainWindow, Func<Window>? openSettingsWindow = null, Icon? icon = null, string? tooltip = null)
        {
            _mainWindow = mainWindow;
            _openSettingsWindow = openSettingsWindow;

            _ctx = new ContextMenuStrip();
            _ctx.Items.Add("Abrir", null, (_, __) => ShowMainWindow());
            _ctx.Items.Add("Ajustes", null, (_, __) => ShowSettings());
            _ctx.Items.Add(new ToolStripSeparator());
            _ctx.Items.Add("Salir", null, (_, __) => ExitApp());

            // Configuramos primero; Visible al final para evitar parpadeos/“fantasmas”
            _icon = new NotifyIcon
            {
                Text = string.IsNullOrWhiteSpace(tooltip) ? "NuvAI FS" : tooltip,
                Icon = icon ?? SystemIcons.Application,
                ContextMenuStrip = _ctx
            };

            _icon.DoubleClick += (_, __) => ShowMainWindow();

            // Hacer visible al final
            _icon.Visible = true;
        }

        public void ShowMainWindow()
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.Topmost = true; _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }

        public void ShowSettings()
        {
            try
            {
                if (_openSettingsWindow is null) { ShowMainWindow(); return; }
                var w = _openSettingsWindow.Invoke();
                if (w.Owner is null) w.Owner = _mainWindow;
                w.ShowDialog();
            }
            catch { /* noop */ }
        }

        /// <summary>Salida real de la app.</summary>
        public void ExitApp()
        {
            try
            {
                if (_mainWindow is ITrayAware twa) twa.RequestRealExit();
            }
            catch { /* noop */ }

            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _icon.Visible = false; } catch { /* noop */ }
            try { _icon.Dispose(); } catch { /* noop */ }
            try { _ctx.Dispose(); } catch { /* noop */ }
        }
    }

    /// <summary>Contrato para ventanas que deben saber cuándo salir de verdad.</summary>
    public interface ITrayAware
    {
        void RequestRealExit();
    }
}
