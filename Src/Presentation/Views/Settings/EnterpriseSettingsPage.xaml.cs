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

        private string? _companyId; // CompanyId efectivo (registro o derivado del DatabasePath)
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

            // CompanyId: primero del registro, si no existe, derivado
            _companyId = RegistryService.GetAppKeyString("CompanyId");
            if (string.IsNullOrWhiteSpace(_companyId))
                _companyId = DeriveCompanyIdFromDatabasePath(databasePath);

            LblCompanyName.Text = OrDash(companyName);
            LblInstallPath.Text = OrDash(installPath);
            LblDataPath.Text = OrDash(dataPath);
            LblDatabasePath.Text = OrDash(databasePath);
            LblExercise.Text = OrDash(exerciseYear);

            _currentExerciseYear = exerciseYear;
            _currentDatabasePath = databasePath;

            _dataPathFs = string.IsNullOrWhiteSpace(dataPath) ? null : Path.Combine(dataPath, "FS");

            // Modo lectura por defecto
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

        /// <summary>
        /// Deriva CompanyId a partir del DatabasePath actual (NombreArchivo = CompanyId + YYYY + ".accdb").
        /// Ej: "XD12025.accdb" -> "XD1"
        /// </summary>
        private static string? DeriveCompanyIdFromDatabasePath(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return null;
            var file = Path.GetFileNameWithoutExtension(dbPath);
            if (string.IsNullOrWhiteSpace(file) || file.Length <= 4) return null;

            var last4 = file.Substring(file.Length - 4);
            if (!int.TryParse(last4, out _)) return null; // últimos 4 deben ser numéricos (año)
            return file.Substring(0, file.Length - 4);
        }

        private void BtnChangeExercise_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var result = MessageBox.Show(owner,
                "Si cambias el ejercicio, el anterior dejará de funcionar si no está configurado.\n\n¿Deseas continuar?",
                "Cambiar ejercicio",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK) return;

            // Solo habilitar selector (no persistir)
            LblExercise.Visibility = Visibility.Collapsed;
            CmbExercise.Visibility = Visibility.Visible;
            BtnChangeExercise.Visibility = Visibility.Collapsed;

            PopulateExerciseComboForCompany();

            _suspendSelectionChanged = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(_currentExerciseYear))
                    CmbExercise.SelectedItem = _currentExerciseYear;
            }
            finally { _suspendSelectionChanged = false; }
        }

        /// <summary>
        /// Llena el combo con los ejercicios que EXISTEN en FS y pertenecen a ESTE CompanyId.
        /// Busca archivos: {CompanyId}{YYYY}.accdb
        /// </summary>
        private void PopulateExerciseComboForCompany()
        {
            CmbExercise.Items.Clear();

            if (string.IsNullOrWhiteSpace(_dataPathFs) || !Directory.Exists(_dataPathFs))
                return;

            var companyId = _companyId;
            if (string.IsNullOrWhiteSpace(companyId))
                return; // sin companyId no podemos filtrar correctamente

            var accdbs = SafeEnumerateFiles(_dataPathFs, $"{companyId}*.accdb");

            var years = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in accdbs)
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(file);
                if (nameNoExt.Length <= companyId.Length + 3) continue; // al menos companyId + 4 dígitos
                var maybeYear = nameNoExt.Substring(companyId.Length);

                if (maybeYear.Length == 4 && int.TryParse(maybeYear, out _)) // YYYY
                    years.Add(maybeYear);
            }

            foreach (var y in years.OrderBy(y => y))
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

            var companyId = _companyId;
            PendingExerciseYear = year;

            // Construimos la ruta candidata exacta: {FS}\{CompanyId}{YYYY}.accdb
            string? newDb = null;
            if (!string.IsNullOrWhiteSpace(_dataPathFs) && !string.IsNullOrWhiteSpace(companyId))
            {
                var candidate = Path.Combine(_dataPathFs, $"{companyId}{year}.accdb");
                if (File.Exists(candidate))
                    newDb = candidate;
            }

            PendingDatabasePath = newDb;

            // Vista previa (sin persistir)
            LblExercise.Text = year;
            LblDatabasePath.Text = OrDash(newDb);

            // Marcar sucio si cambia
            IsDirty = (PendingExerciseYear != _currentExerciseYear) ||
                      (!string.Equals(PendingDatabasePath, _currentDatabasePath, StringComparison.OrdinalIgnoreCase));
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

            // Validación: debe existir el ACCDB construido {CompanyId}{YYYY}.accdb
            if (PendingExerciseYear != null && string.IsNullOrWhiteSpace(PendingDatabasePath))
            {
                var owner = Window.GetWindow(this);
                MessageBox.Show(owner,
                    "No se encontró la base de datos para el ejercicio seleccionado.\n" +
                    "Asegúrate de que existe un archivo <CompanyId><Año>.accdb en la carpeta FS.",
                    "No se puede guardar",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                throw new InvalidOperationException("ACCDB inexistente para el ejercicio seleccionado.");
            }

            // Persistencia (solo si cambió)
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
