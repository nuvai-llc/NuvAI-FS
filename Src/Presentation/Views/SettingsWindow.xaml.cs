// Src/Presentation/Views/SettingsWindow.xaml.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NuvAI_FS.Src.Presentation.Views.Settings;

namespace NuvAI_FS.Src.Presentation.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _navReady = false;

        // Instancias únicas de las páginas (conservan estado)
        private readonly GeneralSettingsPage _generalPage = new();
        private readonly EnterpriseSettingsPage _enterprisePage = new();

        public SettingsWindow()
        {
            InitializeComponent();

            this.ContentRendered += (_, __) =>
            {
                _navReady = true;
                HookDirtyEvents();
                Navigate("General");
            };
        }

        // ===== Suscripción a cambios de todas las páginas =====
        private void HookDirtyEvents()
        {
            foreach (var p in GetAllPages())
                p.DirtyChanged += (_, __) => UpdateActionsUI();
            UpdateActionsUI();
        }

        private ISettingsPage[] GetAllPages()
            => new ISettingsPage[] { _generalPage, _enterprisePage };

        // ===== UI acción inferior (Guardar / Cancelar / Salir) =====
        private void UpdateActionsUI()
        {
            bool anyDirty = GetAllPages().Any(p => p.IsDirty);

            try
            {
                // Guardar
                if (BtnSave != null)
                    BtnSave.IsEnabled = anyDirty;

                // Alternar Salir <-> Cancelar
                if (BtnCancel != null)
                    BtnCancel.Visibility = anyDirty ? Visibility.Visible : Visibility.Collapsed;

                if (BtnExit != null)
                    BtnExit.Visibility = anyDirty ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { /* noop visual */ }
        }

        // ===== Navegación =====
        private void NavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!_navReady) return;
            if (e.NewValue is TreeViewItem item && item.Tag is string key)
                Navigate(key);
        }

        private void Navigate(string key)
        {
            Page page = key switch
            {
                "General" => _generalPage,
                "Enterprise" => _enterprisePage,
                _ => new Page { Content = new TextBlock { Text = "Not implemented." } }
            };

            SettingsFrame.Navigate(page);
        }

        // ===== Botones =====
        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = true; } catch { /* noop */ }
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Descartar cambios en todas las páginas y restaurar su UI
            foreach (var p in GetAllPages())
                p.ResetDirty();

            // Actualizar barra de acciones
            UpdateActionsUI();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Aplica cambios en todas las páginas sucias
                foreach (var p in GetAllPages().Where(p => p.IsDirty))
                    p.ApplyChanges();

                // Limpiar estado de todas
                foreach (var p in GetAllPages())
                    p.ResetDirty();

                UpdateActionsUI();

                MessageBox.Show(this, "Configuración guardada.", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error al guardar",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
