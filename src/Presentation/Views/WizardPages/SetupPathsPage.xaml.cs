using NuvAI_FS.Infrastructure.Access;
using NuvAI_FS.src.Presentation.Setup;
using NuvAI_FS.src.Presentation.Views.Shared;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NuvAI_FS.src.Presentation.Views.WizardPages
{
    public partial class SetupPathsPage : Page
    {
        private readonly SetupState _state;
        private readonly Action<bool> _validityChanged;
        private readonly Action _validatedCallback;

        // Rutas por defecto
        private const string DEFAULT_INSTALL = @"C:\Software DELSOL\FACTUSOL";
        private const string DEFAULT_DATOS = @"C:\Software DELSOL\FACTUSOL\Datos";

        // Glyphs MDL2
        private const string IconFolder = "\uE8B7";
        private const string IconTick = "\uE73E";

        // Estado visual de campos
        private enum FieldState { Neutral, Valid, Invalid }
        private sealed class FieldControls
        {
            public Border Border { get; init; } = null!;
            public TextBlock Icon { get; init; } = null!;
            public Button BrowseBtn { get; init; } = null!;
        }

        // Colores reutilizables
        private static readonly Brush NeutralBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));
        private static readonly Brush OkBrush = Brushes.ForestGreen;
        private static readonly Brush ErrBrush = Brushes.IndianRed;

        private FieldControls InstallControls => new() { Border = BorderInstall, Icon = IconInstall, BrowseBtn = BtnBrowseInstall };
        private FieldControls DataControls => new() { Border = BorderData, Icon = IconData, BrowseBtn = BtnBrowseData };

        private Window? OwnerWindow => Window.GetWindow(this);

        public SetupPathsPage(SetupState state, Action<bool> validityChanged, Action validatedCallback)
        {
            InitializeComponent();
            _state = state;
            _validityChanged = validityChanged;
            _validatedCallback = validatedCallback;

            // Prefill si no hay valores en estado
            if (string.IsNullOrWhiteSpace(_state.FactusolInstallPath))
                _state.FactusolInstallPath = DEFAULT_INSTALL;

            if (string.IsNullOrWhiteSpace(_state.DataPath))
                _state.DataPath = DEFAULT_DATOS;

            LblInstall.Text = _state.FactusolInstallPath;
            LblData.Text = _state.DataPath;

            SetFieldState(InstallControls, FieldState.Neutral);
            SetFieldState(DataControls, FieldState.Neutral);
            _validityChanged(false);
        }

        public SetupPathsPage() : this(new SetupState(), _ => { }, () => { }) { }

        // ===== UI events =====
        [SupportedOSPlatform("windows")]
        private void InstallInput_Click(object sender, MouseButtonEventArgs e)
        {
            if (!BorderInstall.IsEnabled) return;
            PickInstall();
        }

        [SupportedOSPlatform("windows")]
        private void DataInput_Click(object sender, MouseButtonEventArgs e)
        {
            if (!BorderData.IsEnabled) return;
            PickData();
        }

        [SupportedOSPlatform("windows")]
        private void BrowseInstall_Click(object sender, RoutedEventArgs e) => InstallInput_Click(sender, null!);

        [SupportedOSPlatform("windows")]
        private void BrowseData_Click(object sender, RoutedEventArgs e) => DataInput_Click(sender, null!);

        [SupportedOSPlatform("windows")]
        private async void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            var installPath = (LblInstall.Text ?? string.Empty).Trim();
            var dataPath = (LblData.Text ?? string.Empty).Trim();

            bool okInstall = false, okData = false;

            await Dialogs.RunWithLoadingAsync(OwnerWindow, "Validando rutas...", async () =>
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

            if (allOk)
            {
                LblOk.Visibility = Visibility.Visible;
                BtnValidate.IsEnabled = false;
                _validatedCallback();

                // Cargar empresas desde Datos\FS
                await LoadCompaniesAsync(dataPath);

                MessageBox.Show(OwnerWindow,
                    "Las rutas han sido validadas correctamente.",
                    "Validación correcta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                string msg = "No se han encontrado:";
                if (!okInstall) msg += "\n• Ruta de instalación de Factusol";
                if (!okData) msg += "\n• Carpeta de Datos";

                MessageBox.Show(OwnerWindow,
                    string.IsNullOrWhiteSpace(msg) ? "Hay rutas no válidas." : msg,
                    "Validación incompleta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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
            }
        }

        // ===== Picking =====
        [SupportedOSPlatform("windows")]
        private void PickInstall()
        {
            var selected = PickFolder(LblInstall.Text);
            if (string.IsNullOrEmpty(selected)) return;

            LblInstall.Text = selected;
            _state.FactusolInstallPath = selected;
            SetFieldState(InstallControls, FieldState.Neutral);
            BtnValidate.IsEnabled = true;
            _validityChanged(false);
        }

        [SupportedOSPlatform("windows")]
        private void PickData()
        {
            var selected = PickFolder(LblData.Text);
            if (string.IsNullOrEmpty(selected)) return;

            LblData.Text = selected;
            _state.DataPath = selected;
            SetFieldState(DataControls, FieldState.Neutral);
            BtnValidate.IsEnabled = true;
            _validityChanged(false);
        }

        // ===== Empresas (Datos\FS\*.accdb) =====
        private sealed record CompanyItem(string Name, string Path);

        [SupportedOSPlatform("windows")]
        private async Task LoadCompaniesAsync(string dataRoot)
        {
            var fsDir = System.IO.Path.Combine(dataRoot, "FS");
            var items = new List<CompanyItem>();

            // 1) Enumerar archivos .accdb (siempre)
            if (Directory.Exists(fsDir))
            {
                foreach (var file in Directory.EnumerateFiles(fsDir, "*.accdb", SearchOption.TopDirectoryOnly))
                    items.Add(new CompanyItem(Path.GetFileNameWithoutExtension(file), file));
            }

            // 2) Asegurar ACE (si falta, intentamos instalar x64/x86)
            bool hadAce = AceRuntime.IsAceAvailable();
            if (!hadAce)
            {
                var ok = await AceRuntime.EnsureAceInstalledAsync(OwnerWindow);
                hadAce = ok && AceRuntime.IsAceAvailable();
            }

            // 3) Enriquecer nombres (T_TPV.CTT1TPV)
            if (hadAce)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var friendly = TryGetCompanyNameFromAccdb(items[i].Path);
                    if (!string.IsNullOrWhiteSpace(friendly))
                        items[i] = items[i] with { Name = friendly! };
                }
            }

            // 4) Poblar UI
            CompanyBlock.Visibility = Visibility.Visible;
            CmbCompany.ItemsSource = items;
            CmbCompany.IsEnabled = items.Count > 0;
            LblCompanyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (items.Count == 1) CmbCompany.SelectedIndex = 0;

            // 5) Aviso si no pudimos enriquecer
            if (!hadAce && items.Count > 0)
            {
                MessageBox.Show(OwnerWindow,
                    "No se pudo leer el nombre interno de las empresas porque falta el motor de Access.\n" +
                    "Se mostrarán los nombres de archivo. Puedes instalar el motor más tarde desde el Setup.",
                    "Información",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Lee el nombre de la empresa desde T_TPV.CTT1TPV (primera no vacía).
        /// </summary>
        [SupportedOSPlatform("windows")]
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
                    catch
                    {
                        // probar siguiente provider
                    }
                }
            }
            catch { /* noop */ }

            return null;
        }

        // Dump de T_TPV (uno por fila con MessageBox) al seleccionar empresa
        [SupportedOSPlatform("windows")]
        private async Task ShowCompanyDumpAsync(string accdbPath)
        {
            await Task.Run(() =>
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

                        using var cmdAll = new OleDbCommand("SELECT * FROM [T_TPV];", con);
                        using var reader = cmdAll.ExecuteReader();

                        int rowIndex = 0;
                        while (reader != null && reader.Read())
                        {
                            var parts = new List<string>(reader.FieldCount);
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string colName = reader.GetName(i);
                                string? val = reader.IsDBNull(i) ? "<NULL>" : reader.GetValue(i)?.ToString();
                                parts.Add($"{colName}: {val}");
                            }

                            string text = string.Join("\n", parts);
                            string title = $"T_TPV — {System.IO.Path.GetFileName(accdbPath)} (fila {++rowIndex})";

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show(OwnerWindow,
                                                text,
                                                title,
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                            });
                        }
                        return; // provider correcto → no seguimos probando
                    }
                    catch
                    {
                        // probar siguiente provider
                    }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(OwnerWindow,
                                    "No se pudo abrir el archivo Access con ACE 16.0/12.0.",
                                    "Error de lectura",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                });
            });
        }

        // Al elegir empresa en el ComboBox → mostrar dump + guardar en estado
        [SupportedOSPlatform("windows")]
        private async void CmbCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCompany.SelectedItem is CompanyItem ci && !string.IsNullOrWhiteSpace(ci.Path))
            {
                await ShowCompanyDumpAsync(ci.Path);
                TrySetStateProperty("CompanyDbPath", ci.Path);
                TrySetStateProperty("CompanyName", ci.Name);
            }
        }

        private void TrySetStateProperty(string prop, string value)
        {
            try
            {
                var pi = _state.GetType().GetProperty(prop);
                if (pi != null && pi.CanWrite && pi.PropertyType == typeof(string))
                    pi.SetValue(_state, value);
            }
            catch { /* noop */ }
        }

        // ==== Folder picker (Windows) ====
        [SupportedOSPlatform("windows")]
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

            return dlg.ShowDialog(OwnerWindow) == true ? dlg.SelectedPath ?? string.Empty : string.Empty;
        }

        // Por si decides desactivar/activar rápido el conjunto de controles
        private void SetUiEnabled(bool enabled)
        {
            BorderInstall.IsEnabled = enabled && IconInstall.Text != IconTick;
            BtnBrowseInstall.IsEnabled = enabled && IconInstall.Text != IconTick;

            BorderData.IsEnabled = enabled && IconData.Text != IconTick;
            BtnBrowseData.IsEnabled = enabled && IconData.Text != IconTick;

            BtnValidate.IsEnabled = enabled;
            CmbCompany.IsEnabled = enabled && CmbCompany.Items.Count > 0;
        }
    }
}
