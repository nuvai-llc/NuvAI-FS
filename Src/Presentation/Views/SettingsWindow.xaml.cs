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

        // Instancias únicas de las páginas (para conservar su estado y suscripción a eventos)
        private readonly GeneralSettingsPage _generalPage = new();
        private readonly EnterpriseSettingsPage _enterprisePage = new();

        public SettingsWindow()
        {
            InitializeComponent();

            this.ContentRendered += (_, __) =>
            {
                _navReady = true;
                // Suscribir a cambios
                HookDirtyEvents();
                // Navegación inicial
                Navigate("General");
            };
        }

        private void HookDirtyEvents()
        {
            foreach (var p in GetAllPages())
                p.DirtyChanged += (_, __) => UpdateSaveButton();
            UpdateSaveButton();
        }

        private ISettingsPage[] GetAllPages()
            => new ISettingsPage[] { _generalPage, _enterprisePage };

        private void UpdateSaveButton()
        {
            try
            {
                BtnSave.IsEnabled = GetAllPages().Any(p => p.IsDirty);
            }
            catch { /* noop */ }
        }

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

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = true; } catch { }
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Aplica cambios en todas las páginas sucias
                foreach (var p in GetAllPages().Where(p => p.IsDirty))
                    p.ApplyChanges();

                // Si llegó aquí, todo OK → limpiamos estado y deshabilitamos Guardar
                foreach (var p in GetAllPages())
                    p.ResetDirty();

                UpdateSaveButton();
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
