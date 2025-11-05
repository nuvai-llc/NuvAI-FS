using NuvAI_FS.Infrastructure.Access;
using NuvAI_FS.Src.Presentation.Setup;
using NuvAI_FS.Src.Presentation.Views.Shared;
using NuvAI_FS.Src.Services;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NuvAI_FS.Src.Presentation.Views.WizardPages
{
    [SupportedOSPlatform("windows")]
    public partial class SetupPathsPage : Page
    {
        private readonly SetupState _state;
        private readonly Action<bool> _validityChanged;
        private readonly Action _validatedCallback;
        private readonly Action<bool> _companySelectedChanged;

        // Rutas por defecto
        private const string DEFAULT_INSTALL = @"C:\Software DELSOL\FACTUSOL";
        private const string DEFAULT_DATOS = @"C:\Software DELSOL\FACTUSOL\Datos";

        // Glyphs
        private const string IconFolder = "\uE8B7";
        private const string IconTick = "\uE73E";

        private enum FieldState { Neutral, Valid, Invalid }
        private sealed class FieldControls
        {
            public Border Border { get; init; } = null!;
            public TextBlock Icon { get; init; } = null!;
            public Button BrowseBtn { get; init; } = null!;
        }

        private static readonly Brush NeutralBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));
        private static readonly Brush OkBrush = Brushes.ForestGreen;
        private static readonly Brush ErrBrush = Brushes.IndianRed;

        private FieldControls InstallControls => new() { Border = BorderInstall, Icon = IconInstall, BrowseBtn = BtnBrowseInstall };
        private FieldControls DataControls => new() { Border = BorderData, Icon = IconData, BrowseBtn = BtnBrowseData };

        // Estado de ocupación para evitar reentradas
        private bool _isBusy;

        // Ejercicio elegido + código de empresa actual (prefijo sin año)
        private int? _selectedExerciseYear;
        private string? _currentCompanyCode;

        public SetupPathsPage(
            SetupState state,
            Action<bool> validityChanged,
            Action validatedCallback,
            Action<bool> companySelectedChanged)
        {
            InitializeComponent();
            _state = state;
            _validityChanged = validityChanged;
            _validatedCallback = validatedCallback;
            _companySelectedChanged = companySelectedChanged;

            // Prefill desde estado (o defaults si viene vacío)
            if (string.IsNullOrWhiteSpace(_state.FactusolInstallPath))
                _state.FactusolInstallPath = DEFAULT_INSTALL;
            if (string.IsNullOrWhiteSpace(_state.DataPath))
                _state.DataPath = DEFAULT_DATOS;

            LblInstall.Text = _state.FactusolInstallPath;
            LblData.Text = _state.DataPath;

            SetFieldState(InstallControls, FieldState.Neutral);
            SetFieldState(DataControls, FieldState.Neutral);
            _validityChanged(false);

            // Restauración si ya estaba validado
            if (_state.PathsValidated &&
                Directory.Exists(_state.FactusolInstallPath) &&
                Directory.Exists(_state.DataPath))
            {
                SetFieldState(InstallControls, FieldState.Valid);
                SetFieldState(DataControls, FieldState.Valid);
                LblOk.Visibility = Visibility.Visible;
                BtnValidate.IsEnabled = false;

                RestoreCompaniesFast(_state.DataPath);
            }
        }

        public SetupPathsPage() : this(new SetupState(), _ => { }, () => { }, _ => { }) { }

        // ===== Helpers owner-safe =====
        private Window? TryGetOwner()
        {
            try
            {
                var w = Window.GetWindow(this);
                if (w != null && w.IsVisible) return w;
                var mw = Application.Current?.MainWindow;
                if (mw != null && mw.IsVisible) return mw;
            }
            catch { }
            return null;
        }

        private async Task RunWithLoadingSafeAsync(string text, Func<Task> work)
        {
            var owner = TryGetOwner();
            if (owner != null)
            {
                await Dialogs.RunWithLoadingAsync(owner, text, work);
            }
            else
            {
                await work();
            }
        }

        private void InfoSafe(string text, string title)
        {
            try { var o = TryGetOwner(); if (o != null) MessageBox.Show(o, text, title, MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
        }
        private void WarnSafe(string text, string title)
        {
            try { var o = TryGetOwner(); if (o != null) MessageBox.Show(o, text, title, MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
        }

        // ===== UI Events =====
        private void InstallInput_Click(object sender, MouseButtonEventArgs e)
        {
            if (!BorderInstall.IsEnabled) return;
            PickInstall();
        }

        private void DataInput_Click(object sender, MouseButtonEventArgs e)
        {
            if (!BorderData.IsEnabled) return;
            PickData();
        }

        private void BrowseInstall_Click(object sender, RoutedEventArgs e) => InstallInput_Click(sender, null!);
        private void BrowseData_Click(object sender, RoutedEventArgs e) => DataInput_Click(sender, null!);

        private async void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;
            SetInteractive(false);

            try
            {
                var installPath = (LblInstall.Text ?? string.Empty).Trim();
                var dataPath = (LblData.Text ?? string.Empty).Trim();

                bool okInstall = false, okData = false;

                await RunWithLoadingSafeAsync("Validando rutas...", async () =>
                {
                    await Task.Run(() =>
                    {
                        okInstall = Directory.Exists(installPath);
                        okData = Directory.Exists(dataPath);
                    });
                });

                ApplyInstallResult(okInstall, installPath);
                ApplyDataResult(okData, dataPath);

                bool allOk = okInstall && okData;
                _validityChanged(allOk);

                if (!allOk)
                {
                    string msg = "No se han encontrado:";
                    if (!okInstall) msg += "\n• Ruta de instalación de Factusol";
                    if (!okData) msg += "\n• Carpeta de Datos";
                    WarnSafe(string.IsNullOrWhiteSpace(msg) ? "Hay rutas no válidas." : msg, "Validación incompleta");

                    _state.PathsValidated = false;
                    return;
                }

                LblOk.Visibility = Visibility.Visible;
                BtnValidate.IsEnabled = false;

                _state.PathsValidated = true;
                _validatedCallback();

                // Cargar empresas (solo con nombre válido)
                await LoadCompaniesAsync(dataPath);

                InfoSafe("Las rutas han sido validadas correctamente.", "Validación correcta");
            }
            catch (Exception ex)
            {
                WarnSafe($"Error durante la validación: {ex.Message}", "Error");
                _state.PathsValidated = false;
                _validityChanged(false);
            }
            finally
            {
                SetInteractive(true);
                _isBusy = false;
            }
        }

        private void SetInteractive(bool enabled)
        {
            BorderInstall.IsEnabled = enabled;
            BorderData.IsEnabled = enabled;
            BtnBrowseInstall.IsEnabled = enabled;
            BtnBrowseData.IsEnabled = enabled;
            BtnValidate.IsEnabled = enabled && !_state.PathsValidated;
        }

        // ===== Field state helpers =====
        private static void SetFieldState(FieldControls c, FieldState state)
        {
            switch (state)
            {
                case FieldState.Neutral:
                    c.Border.BorderBrush = NeutralBorder;
                    c.Border.IsEnabled = true;
                    c.Border.Cursor = Cursors.Hand;
                    c.Icon.Text = IconFolder;
                    c.Icon.Foreground = Brushes.Black;
                    c.BrowseBtn.IsEnabled = true;
                    c.BrowseBtn.Opacity = 1.0;
                    break;

                case FieldState.Valid:
                    c.Border.BorderBrush = OkBrush;
                    c.Border.IsEnabled = false;
                    c.Border.Cursor = Cursors.Arrow;
                    c.Icon.Text = IconTick;
                    c.Icon.Foreground = OkBrush;
                    c.BrowseBtn.IsEnabled = false;
                    c.BrowseBtn.Opacity = 0.5;
                    break;

                case FieldState.Invalid:
                    c.Border.BorderBrush = ErrBrush;
                    c.Border.IsEnabled = true;
                    c.Border.Cursor = Cursors.Hand;
                    c.Icon.Text = IconFolder;
                    c.Icon.Foreground = ErrBrush;
                    c.BrowseBtn.IsEnabled = true;
                    c.BrowseBtn.Opacity = 1.0;
                    break;
            }
        }

        private void ApplyInstallResult(bool ok, string path)
        {
            if (ok)
            {
                _state.FactusolInstallPath = path;
                SetFieldState(InstallControls, FieldState.Valid);
            }
            else
            {
                _state.FactusolInstallPath = string.Empty;
                LblInstall.Text = string.Empty;
                SetFieldState(InstallControls, FieldState.Invalid);
                LblOk.Visibility = Visibility.Collapsed;
                CompanyBlock.Visibility = Visibility.Collapsed;
                ExerciseBlock.Visibility = Visibility.Collapsed;
                _selectedExerciseYear = null;
                _companySelectedChanged(false);
            }
        }

        private void ApplyDataResult(bool ok, string path)
        {
            if (ok)
            {
                _state.DataPath = path;
                SetFieldState(DataControls, FieldState.Valid);
            }
            else
            {
                _state.DataPath = string.Empty;
                LblData.Text = string.Empty;
                SetFieldState(DataControls, FieldState.Invalid);
                LblOk.Visibility = Visibility.Collapsed;
                CompanyBlock.Visibility = Visibility.Collapsed;
                ExerciseBlock.Visibility = Visibility.Collapsed;
                _selectedExerciseYear = null;
                _companySelectedChanged(false);
            }
        }

        // ===== Picking =====
        private void PickInstall()
        {
            var selected = PickFolder(LblInstall.Text);
            if (string.IsNullOrEmpty(selected)) return;

            LblInstall.Text = selected;
            _state.FactusolInstallPath = selected;
            SetFieldState(InstallControls, FieldState.Neutral);
            BtnValidate.IsEnabled = true;
            _state.PathsValidated = false;
            _selectedExerciseYear = null;
            ExerciseBlock.Visibility = Visibility.Collapsed;
            _companySelectedChanged(false);
            _validityChanged(false);
        }

        private void PickData()
        {
            var selected = PickFolder(LblData.Text);
            if (string.IsNullOrEmpty(selected)) return;

            LblData.Text = selected;
            _state.DataPath = selected;
            SetFieldState(DataControls, FieldState.Neutral);
            BtnValidate.IsEnabled = true;
            _state.PathsValidated = false;
            _selectedExerciseYear = null;
            ExerciseBlock.Visibility = Visibility.Collapsed;
            _companySelectedChanged(false);
            _validityChanged(false);
        }

        // ===== Empresas =====
        private sealed record CompanyItem(string Name, string Path);

        private void RestoreCompaniesFast(string dataRoot)
        {
            // Si tenías cache previa y ENRIQUECIDA, úsala
            if (_state.CompanyListEnriched && _state.CompanyList is { Count: > 0 } cached)
            {
                CompanyBlock.Visibility = Visibility.Visible;
                var items = new List<CompanyItem>(cached.Count);
                for (int i = 0; i < cached.Count; i++)
                    items.Add(new CompanyItem(cached[i].Name, cached[i].Path));

                CmbCompany.ItemsSource = items;
                CmbCompany.IsEnabled = items.Count > 0;
                LblCompanyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Reselección
                if (!string.IsNullOrWhiteSpace(_state.DatabasePath))
                    SelectCompanyByPath(_state.DatabasePath);
                else if (!string.IsNullOrWhiteSpace(_state.CompanyName))
                    SelectCompanyByName(_state.CompanyName);

                // Si ya hay empresa seleccionada, intentamos ejercicios
                if (CmbCompany.SelectedItem is CompanyItem ci)
                {
                    _ = LoadExercisesForCompanyAsync(_state.DataPath, ci.Path);
                }

                return;
            }

            // Sin cache enriquecida → recarga
            _ = LoadCompaniesAsync(dataRoot);
        }

        private async Task LoadCompaniesAsync(string dataRoot)
            => await LoadCompaniesInternalAsync(dataRoot, showAceInfoIfMissing: true);

        private async Task LoadCompaniesInternalAsync(string dataRoot, bool showAceInfoIfMissing)
        {
            var fsDir = System.IO.Path.Combine(dataRoot, "FS");
            var validItems = new List<CompanyItem>();

            // Requiere ACE para poder filtrar por CTT1TPV
            bool hasAce = AceRuntime.IsAceAvailable();
            if (!hasAce)
            {
                try
                {
                    var owner = TryGetOwner();
                    var ok = await AceRuntime.EnsureAceInstalledAsync(owner!);
                    hasAce = ok && AceRuntime.IsAceAvailable();
                }
                catch { hasAce = AceRuntime.IsAceAvailable(); }
            }

            if (Directory.Exists(fsDir))
            {
                if (hasAce)
                {
                    // 1) Agrupar .accdb por código de empresa (prefijo) y extraer años
                    var byCode = new Dictionary<string, List<(string path, int year)>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var file in Directory.EnumerateFiles(fsDir, "*.accdb", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var (code, year) = ExtractCompanyCodeAndYear(name);
                        if (string.IsNullOrWhiteSpace(code) || !year.HasValue) continue;

                        if (!byCode.TryGetValue(code, out var list))
                        {
                            list = new List<(string path, int year)>();
                            byCode[code] = list;
                        }
                        list.Add((file, year.Value));
                    }

                    // 2) Un solo item por empresa: usamos el accdb del año más alto para leer el nombre "amigable"
                    foreach (var kvp in byCode)
                    {
                        var code = kvp.Key;
                        var list = kvp.Value;
                        var best = list.OrderByDescending(x => x.year).First();

                        var friendly = TryGetCompanyNameFromAccdb(best.path);
                        var displayName = !string.IsNullOrWhiteSpace(friendly) ? friendly! : code;

                        validItems.Add(new CompanyItem(displayName, best.path));
                    }
                }
                else
                {
                    // Sin ACE no podemos conocer nombres → no mostramos nada
                    validItems.Clear();
                }
            }

            // Poblar UI
            CompanyBlock.Visibility = Visibility.Visible;
            CmbCompany.ItemsSource = validItems;
            CmbCompany.IsEnabled = validItems.Count > 0;
            LblCompanyHint.Visibility = validItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Cache solo con válidos
            var cache = new List<(string Name, string Path)>(validItems.Count);
            for (int i = 0; i < validItems.Count; i++)
                cache.Add((validItems[i].Name, validItems[i].Path));
            _state.CompanyList = cache;
            _state.CompanyListEnriched = true;

            // Autoselección si hay una sola
            if (string.IsNullOrWhiteSpace(_state.DatabasePath) && validItems.Count == 1)
                CmbCompany.SelectedIndex = 0;

            // Si tenemos empresa seleccionada, cargar ejercicios
            if (CmbCompany.SelectedItem is CompanyItem sel)
            {
                await LoadExercisesForCompanyAsync(dataRoot, sel.Path);
            }

            if (!hasAce && showAceInfoIfMissing)
            {
                InfoSafe(
                    "No se pudo leer el nombre interno de las empresas porque falta el motor de Access.\n" +
                    "Hasta instalarlo, la lista de empresas permanecerá vacía.",
                    "Información");
            }
        }

        private void SelectCompanyByPath(string accdbPath)
        {
            if (CmbCompany.ItemsSource is IEnumerable<CompanyItem> src)
            {
                int idx = 0, i = 0;
                foreach (var it in src)
                {
                    if (string.Equals(it.Path, accdbPath, StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                    i++;
                }
                CmbCompany.SelectedIndex = idx;
            }
        }

        private void SelectCompanyByName(string companyName)
        {
            if (CmbCompany.ItemsSource is IEnumerable<CompanyItem> src)
            {
                int idx = 0, i = 0;
                foreach (var it in src)
                {
                    if (string.Equals(it.Name?.Trim(), companyName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                    i++;
                }
                CmbCompany.SelectedIndex = idx;
            }
        }

        /// <summary>
        /// Lee el nombre de la empresa desde T_TPV.CTT1TPV (primera no vacía).
        /// </summary>
        private string? TryGetCompanyNameFromAccdb(string accdbPath)
        {
            try
            {
                var providers = new[]
                {
                    "Provider=Microsoft.ACE.OLEDB.16.0;Data Source={0};Persist Security Info=False;",
                    "Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};Persist Security Info=False;"
                };

                foreach (var fmt in providers)
                {
                    var cs = string.Format(fmt, accdbPath);
                    try
                    {
                        using var con = new OleDbConnection(cs);
                        con.Open();

                        using var cmd = new OleDbCommand(
                            "SELECT TOP 1 [CTT1TPV] " +
                            "FROM [T_TPV] " +
                            "WHERE [CTT1TPV] IS NOT NULL AND Trim([CTT1TPV]) <> ''",
                            con);

                        var v = cmd.ExecuteScalar();
                        if (v != null && v != DBNull.Value)
                        {
                            var s = v.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                    catch { /* probar siguiente provider */ }
                }
            }
            catch { /* noop */ }

            return null;
        }

        // Cambio de selección de empresa → persistir en estado + notificar + ejercicios
        private async void CmbCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCompany.SelectedItem is CompanyItem ci)
            {
                _state.DatabasePath = ci.Path;
                _state.CompanyName = ci.Name ?? string.Empty;
                _companySelectedChanged(true);

                // Fijar código de empresa actual a partir del filename
                var fn = Path.GetFileNameWithoutExtension(ci.Path);
                var (code, _) = ExtractCompanyCodeAndYear(fn);
                _currentCompanyCode = string.IsNullOrWhiteSpace(code) ? null : code;

                // Repoblar ejercicios para la empresa seleccionada
                _selectedExerciseYear = null;
                await LoadExercisesForCompanyAsync(_state.DataPath, ci.Path);
            }
            else
            {
                _state.DatabasePath = string.Empty;
                _state.CompanyName = string.Empty;
                _companySelectedChanged(false);

                // Reset ejercicios
                ExerciseBlock.Visibility = Visibility.Collapsed;
                CmbExercise.ItemsSource = null;
                CmbExercise.IsEnabled = false;
                LblExerciseHint.Visibility = Visibility.Collapsed;
                _selectedExerciseYear = null;
                _currentCompanyCode = null;
            }
        }

        // ==== EJERCICIOS ====

        private async Task LoadExercisesForCompanyAsync(string dataRoot, string selectedCompanyAccdbPath)
        {
            try
            {
                ExerciseBlock.Visibility = Visibility.Visible;
                CmbExercise.ItemsSource = null;
                CmbExercise.IsEnabled = false;
                LblExerciseHint.Visibility = Visibility.Collapsed;

                var fsDir = System.IO.Path.Combine(dataRoot ?? string.Empty, "FS");
                if (!Directory.Exists(fsDir) || !File.Exists(selectedCompanyAccdbPath))
                {
                    LblExerciseHint.Visibility = Visibility.Visible;
                    return;
                }

                // 1) Obtener código de empresa desde el fichero seleccionado
                var selectedFileName = Path.GetFileNameWithoutExtension(selectedCompanyAccdbPath);
                var (companyCode, selectedYear) = ExtractCompanyCodeAndYear(selectedFileName);
                if (string.IsNullOrEmpty(companyCode))
                {
                    LblExerciseHint.Visibility = Visibility.Visible;
                    return;
                }
                _currentCompanyCode ??= companyCode; // por si no estaba aún

                // 2) Reunir años disponibles para ese código
                var years = await Task.Run(() =>
                {
                    var list = new List<int>();
                    foreach (var file in Directory.EnumerateFiles(fsDir, "*.accdb", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var (code, year) = ExtractCompanyCodeAndYear(name);
                        if (string.Equals(code, companyCode, StringComparison.OrdinalIgnoreCase) && year.HasValue)
                        {
                            if (!list.Contains(year.Value))
                                list.Add(year.Value);
                        }
                    }
                    list.Sort();
                    return list;
                });

                if (years.Count == 0)
                {
                    LblExerciseHint.Visibility = Visibility.Visible;
                    return;
                }

                // 3) Poblar combo
                CmbExercise.ItemsSource = years;
                CmbExercise.IsEnabled = true;

                // 4) Selección (año del fichero seleccionado o el mayor) + persistencia inmediata
                int yearToSelect = selectedYear ?? years.Max();
                if (!years.Contains(yearToSelect))
                    yearToSelect = years.Max();

                CmbExercise.SelectedItem = yearToSelect;
                _selectedExerciseYear = yearToSelect;

                // Guardar path y ejercicio en Registro ya mismo
                SaveSelectedExercisePath(yearToSelect);
            }
            catch
            {
                // Fallback visual
                LblExerciseHint.Visibility = Visibility.Visible;
                CmbExercise.ItemsSource = null;
                CmbExercise.IsEnabled = false;
                _selectedExerciseYear = null;
            }
        }

        /// <summary>
        /// A partir de un nombre de archivo (sin extensión), intenta extraer:
        /// - companyCode: prefijo antes del ÚLTIMO bloque de 4 dígitos.
        /// - year: ese último bloque de 4 dígitos si es un año razonable (2000..2100).
        /// Ej.: "0012025" -> ("001", 2025) | "ACME2024" -> ("ACME", 2024)
        /// </summary>
        private static (string companyCode, int? year) ExtractCompanyCodeAndYear(string fileNameNoExt)
        {
            if (string.IsNullOrWhiteSpace(fileNameNoExt))
                return (string.Empty, null);

            var m = Regex.Match(fileNameNoExt, @"^(?<code>.+?)(?<year>\d{4})$");
            if (!m.Success)
                return (string.Empty, null);

            var code = (m.Groups["code"].Value ?? string.Empty).Trim();
            var yearStr = m.Groups["year"].Value;

            if (!int.TryParse(yearStr, out var year))
                return (string.Empty, null);

            if (year < 2000 || year > 2100)
                return (string.Empty, null);

            return (code, year);
        }

        // Al cambiar el ejercicio manualmente, persistimos ruta+ejercicio
        private void CmbExercise_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CmbExercise.SelectedItem is int y)
            {
                _selectedExerciseYear = y;
                SaveSelectedExercisePath(y);
            }
            else
            {
                _selectedExerciseYear = null;
            }
        }

        // ======= Helpers de ejercicio/registro =======

        private static string BuildAccdbPath(string dataRoot, string companyCode, int year)
        {
            var fsDir = Path.Combine(dataRoot ?? string.Empty, "FS");
            return Path.Combine(fsDir, $"{companyCode}{year}.accdb");
        }

        private void SaveSelectedExercisePath(int year)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_state.DataPath) || string.IsNullOrWhiteSpace(_currentCompanyCode))
                    return;

                var target = BuildAccdbPath(_state.DataPath, _currentCompanyCode!, year);
                if (!File.Exists(target)) return;

                // Actualiza el estado en memoria
                _state.DatabasePath = target;

                // Persistencia en Registro
                RegistryService.SetAppKey("DatabasePath", target);
                RegistryService.SetAppKey("ExerciseYear", year.ToString());
            }
            catch { /* noop */ }
        }

        // ==== Folder picker (Windows) ====
        private string PickFolder(string? initial)
        {
            if (!OperatingSystem.IsWindows()) return string.Empty;

            var dlg = new VistaFolderBrowserDialog
            {
                Description = "Selecciona una carpeta",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };

            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                dlg.SelectedPath = initial;

            var owner = TryGetOwner();
            return dlg.ShowDialog(owner) == true ? dlg.SelectedPath ?? string.Empty : string.Empty;
        }
    }
}
