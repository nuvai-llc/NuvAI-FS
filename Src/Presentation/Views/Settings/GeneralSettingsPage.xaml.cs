// Src/Presentation/Views/Settings/GeneralSettingsPage.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;

namespace NuvAI_FS.Src.Presentation.Views.Settings
{
    public partial class GeneralSettingsPage : Page, ISettingsPage
    {
        public GeneralSettingsPage()
        {
            InitializeComponent();
            // Defaults temporales (puedes cambiarlos sin persistir)
            SafeSet(ChkRunOnStartup, true);
            SafeSet(ChkStartMinimized, false);
            SafeSet(ChkWindowsStartup, true);
            // Si añadiste notificaciones en tu XAML:
            SafeSet(ChkDialogNotifications, false);
            SafeSet(ChkDesktopNotifications, true);

            WireChanges();
            _isDirty = false; // recien cargado, no hay cambios
        }

        private static void SafeSet(CheckBox? cb, bool value)
        {
            if (cb != null) cb.IsChecked = value;
        }

        private void WireChanges()
        {
            // Suscribe solo si existen en el XAML actual
            Hook(ChkRunOnStartup);
            Hook(ChkStartMinimized);
            Hook(ChkWindowsStartup);
            Hook(ChkDialogNotifications);
            Hook(ChkDesktopNotifications);
        }

        private void Hook(CheckBox? cb)
        {
            if (cb == null) return;
            cb.Checked += AnyChanged;
            cb.Unchecked += AnyChanged;
        }

        private void AnyChanged(object? sender, RoutedEventArgs e)
        {
            IsDirty = true;
        }

        // ===== ISettingsPage =====
        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (_isDirty == value) return;
                _isDirty = value;
                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? DirtyChanged;

        // Por ahora no persistimos nada; evita errores mientras implementas la config
        public void ApplyChanges()
        {
            // no-op temporal
        }

        public void ResetDirty() => IsDirty = false;
    }
}
