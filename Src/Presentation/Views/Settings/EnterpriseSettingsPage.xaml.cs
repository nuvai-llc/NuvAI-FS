// Src/Presentation/Views/Settings/EnterpriseSettingsPage.xaml.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NuvAI_FS.Src.Services;

namespace NuvAI_FS.Src.Presentation.Views.Settings
{
    [SupportedOSPlatform("windows")]
    public partial class EnterpriseSettingsPage : Page, ISettingsPage
    {
        private string? _currentExerciseYear;
        private string? _currentDatabasePath;
        private string? _dataPathFs;
        private string? _enterpriseId; // NUEVO: leído de registro

        // Para impedir que SelectedItem programático dispare "dirty"
        private bool _suspendSelectionChanged;

        // Cambios pendientes (no se persisten hasta ApplyChanges)
        public string? PendingExerciseYear { get; private set; }
        public string? PendingDatabasePath { get; private set; }

        public EnterpriseSettingsPage()
        {
            InitializeComponent();
            LoadFromRegistryAndPopulate();
        }

        private void LoadFromRegistryAndPopulate()
        {
            var companyName = RegistryService.GetAppKeyString("CompanyName");
            var installPath = RegistryService.GetAppKeyString("FactusolInstallPath");
            var dataPath = RegistryService.GetAppKeyString("FactusolDataPath");
            var databasePath = RegistryService.GetAppKeyString("DatabasePath");
            var exerciseYear = RegistryService.GetAppKeyString("ExerciseYear");
            _enterpriseId = RegistryService.GetAppKeyString("EnterpriseId"); // NUEVO

            LblCompanyName.Text = OrDash(companyName);
            LblInstallPath.Text = OrDash(installPath);
            LblDataPath.Text = OrDash(dataPath);
            LblDatabasePath.Text = OrDash(databasePath);
            LblExercise.Text = OrDash(exerciseYear);

            _currentExerciseYear = exerciseYear;
            _currentDatabasePath = databasePath;
            _dataPathFs = string.IsNullOrWhiteSpace(dataPath) ? null : Path.Combine(dataPath, "FS");

            // Estado UI lectura
            LblExercise.Visibility = Visibility.Visible;
            CmbExercise.Visibility = Visibility.Collapsed;
            BtnChangeExercise.Visibility = Visibility.Visible;

            // Reset “sucio”
            PendingExerciseYear = null;
            PendingDatabasePath = null;
            _isDirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string OrDash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

        private void BtnChangeExercise_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var result = MessageBox.Show(owner,
                "Si cambias el ejercicio, el anterior dejará de funcionar si no está configurado.\n\n¿Deseas continuar?",
                "Cambiar ejercicio",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK) return;

            // Solo cambiar a modo selector (no guardar nada)
            LblExercise.Visibility = Visibility.Collapsed;
            CmbExercise.Visibility = Visibility.Visible;
            BtnChangeExercise.Visibility = Visibility.Collapsed;

            PopulateExerciseComboFilteredByEnterprise();

            // Selección programática del año actual SIN disparar cambios
            _suspendSelectionChanged = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(_currentExerciseYear))
                    CmbExercise.SelectedItem = _currentExerciseYear;
            }
            finally
            {
                _suspendSelectionChanged = false;
            }
        }

        /// <summary>
        /// Llena el combo SOLO con años que existan como ACCDB con el mismo EnterpriseId:
        /// &lt;EnterpriseId&gt;&lt;Año&gt;.accdb
        /// Si no hay EnterpriseId, hace fallback a cualquier *YYYY.accdb (pero no escribe nada aún).
        /// </summary>
        private void PopulateExerciseComboFilteredByEnterprise()
        {
            CmbExercise.Items.Clear();

            if (string.IsNullOrWhiteSpace(_dataPathFs) || !Directory.Exists(_dataPathFs))
                return;

            // Si tenemos EnterpriseId, buscamos exactamente ^<EnterpriseId>(?<year>\d{4})\.accdb$
            Regex regex = !string.IsNullOrWhiteSpace(_enterpriseId)
                ? new Regex(@"^" + Regex.Escape(_enterpriseId) + @"(?<year>\d{4})\.accdb$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
                : new Regex(@"^(?<code>\d+)(?<year>\d{4})\.accdb$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var accdbs = SafeEnumerateFiles(_dataPathFs, "*.accdb");
            var years = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in accdbs)
            {
                var name = Path.GetFileName(file);
                var m = regex.Match(name);
                if (!m.Success) continue;
                years.Add(m.Groups["year"].Value);
            }

            foreach (var y in years.OrderBy(s => s))
                CmbExercise.Items.Add(y);
        }

        private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { return Enumerable.Empty<string>(); }
        }

        private void CmbExercise_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suspendSelectionChanged) return;
            if (CmbExercise.Visibility != Visibility.Visible) return;
            if (CmbExercise.SelectedItem is not string year || string.IsNullOrWhiteSpace(year)) return;

            // Resolver ACCDB exacto: <EnterpriseId><year>.accdb si hay EnterpriseId; si no, fallback *<year>.accdb
            var newDb = ResolveAccdbForYear(_dataPathFs, year, _enterpriseId);

            PendingExerciseYear = year;
            PendingDatabasePath = newDb;

            // Vista previa en labels (sin guardar)
            LblExercise.Text = year;
            LblDatabasePath.Text = OrDash(newDb);

            // Marcar sucio si realmente cambia algo
            IsDirty = (PendingExerciseYear != _currentExerciseYear) ||
                      (!string.Equals(PendingDatabasePath, _currentDatabasePath, StringComparison.OrdinalIgnoreCase));
        }

        private static string? ResolveAccdbForYear(string? fsDir, string year, string? enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(fsDir) || !Directory.Exists(fsDir)) return null;

            if (!string.IsNullOrWhiteSpace(enterpriseId))
            {
                var exact = Path.Combine(fsDir, $"{enterpriseId}{year}.accdb");
                if (File.Exists(exact)) return exact;
            }

            // Fallback prudente si no conocemos EnterpriseId
            return Directory.EnumerateFiles(fsDir, $"*{year}.accdb", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p)
                            .FirstOrDefault();
        }

        private void BtnChangeCompany_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var wizard = new SetupWizardWindow { Owner = owner };
            wizard.ShowDialog();

            // Tras wizard, recarga desde registro y vuelve a lectura
            LoadFromRegistryAndPopulate();
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

        public void ApplyChanges()
        {
            if (!IsDirty) return;

            // Debe existir ACCDB para el año elegido con el EnterpriseId actual
            if (PendingExerciseYear != null && string.IsNullOrWhiteSpace(PendingDatabasePath))
            {
                var owner = Window.GetWindow(this);
                MessageBox.Show(owner,
                    "No se encontró la base de datos para el ejercicio seleccionado.\n" +
                    "Asegúrate de que existe un archivo <EnterpriseId><Año>.accdb en la carpeta FS.",
                    "No se puede guardar",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                throw new InvalidOperationException("ACCDB inexistente para el ejercicio seleccionado.");
            }

            // Persistencia (solo lo que cambió)
            if (!string.IsNullOrWhiteSpace(PendingExerciseYear) &&
                !string.Equals(PendingExerciseYear, _currentExerciseYear, StringComparison.Ordinal))
            {
                RegistryService.SetAppKey("ExerciseYear", PendingExerciseYear);
                _currentExerciseYear = PendingExerciseYear;
            }

            if (!string.IsNullOrWhiteSpace(PendingDatabasePath) &&
                !string.Equals(PendingDatabasePath, _currentDatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                RegistryService.SetAppKey("DatabasePath", PendingDatabasePath);
                _currentDatabasePath = PendingDatabasePath;
            }

            // UI: volver a lectura
            CmbExercise.Visibility = Visibility.Collapsed;
            LblExercise.Visibility = Visibility.Visible;
            BtnChangeExercise.Visibility = Visibility.Visible;

            // Refrescar labels definitivos
            LblExercise.Text = OrDash(_currentExerciseYear);
            LblDatabasePath.Text = OrDash(_currentDatabasePath);

            // Reset “sucio”
            PendingExerciseYear = null;
            PendingDatabasePath = null;
            IsDirty = false;
        }

        public void ResetDirty()
        {
            // Descartar cambios y volver a lectura
            PendingExerciseYear = null;
            PendingDatabasePath = null;

            // Re-pintar desde estado actual
            LblExercise.Text = OrDash(_currentExerciseYear);
            LblDatabasePath.Text = OrDash(_currentDatabasePath);

            CmbExercise.Visibility = Visibility.Collapsed;
            LblExercise.Visibility = Visibility.Visible;
            BtnChangeExercise.Visibility = Visibility.Visible;

            IsDirty = false;
        }
    }
}
