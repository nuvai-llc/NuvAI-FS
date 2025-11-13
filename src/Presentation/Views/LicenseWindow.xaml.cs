// Src\Presentation\Views\LicenseWindow.xaml.cs
using NuvAI_FS.Src.Services;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace NuvAI_FS.Src.Presentation.Views
{
    public partial class LicenseWindow : Window
    {

        private readonly LicenseService _licenseService;
        private bool _updatingLicense; // evita loops en TextChanged
        private bool _isChecking;      // bloquea inputs mientras valida

        // ---- UX: recordar foco y caret para no perderlo tras validar ----
        private IInputElement? _lastFocused;
        private int _clientCaret = 0;       // caret en clientId
        private int _licenseRawCaret = 0;   // caret "raw" sin guiones en licencia


        public LicenseWindow()
        {
            InitializeComponent();

            _licenseService = new LicenseService();

            // Eventos de inputs
            TxtClientId.TextChanged += OnClientIdChanged;
            TxtLicense.TextChanged += OnLicenseChanged;

            // Recordar foco y caret en tiempo real
            TxtClientId.GotKeyboardFocus += (_, __) => _lastFocused = TxtClientId;
            TxtLicense.GotKeyboardFocus += (_, __) => _lastFocused = TxtLicense;
            TxtClientId.SelectionChanged += (_, __) => { if (TxtClientId.IsKeyboardFocused) _clientCaret = TxtClientId.SelectionStart; };
            TxtLicense.SelectionChanged += (_, __) =>
            {
                if (TxtLicense.IsKeyboardFocused)
                {
                    int f = TxtLicense.SelectionStart;
                    string before = (TxtLicense.Text ?? string.Empty).Substring(0, f);
                    _licenseRawCaret = CountAlnum(before); // caret sin contar guiones
                }
            };

            // Botón Activar
            AcceptBtn.Click += async (_, __) => await ActivateLicense();


            // Estado inicial
            AcceptBtn.IsEnabled = false;
            SetBordersNeutral();
            ShowInfo(null, null);
            ShowLoading(false);
        }


        private async Task ActivateLicense()
        {
            if (_isChecking) return; // evita carreras si justo está validando

            var clientId = (TxtClientId.Text ?? "").Trim();
            var license = (TxtLicense.Text ?? "").Trim();

            // seguridad extra (el botón ya se habilita solo cuando es válida, pero por si acaso)
            if (!IsClientIdValid(clientId) || !IsLicenseFormattedValid(license))
            {
                MessageBox.Show(this, "Completa un ID y una licencia válidos antes de activar.",
                                "Activación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _isChecking = true;
                CaptureFocusState();
                ShowInfo(null, null);
                ShowLoading(false);
                SetInputsEnabled(false);
                AcceptBtn.IsEnabled = false;

                var ok = await _licenseService.ActivateLicenseAsync(clientId, license);

                if (ok)
                {
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Licencia activada.", "Activación",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                    });

                    // Cerrar YA el diálogo para que App abra el MainWindow
                    try { this.DialogResult = true; } catch { /* por si no es modal */ }
                    this.Close();
                    return;
                }
                else
                {
                    ShowInfo("No se pudo activar la licencia.", Brushes.IndianRed);
                    SetBordersInvalid();
                    MessageBox.Show("No se pudo activar la licencia. Inténtalo de nuevo.", "Activación",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    AcceptBtn.IsEnabled = false;
                }
            }
            finally
            {
                ShowLoading(false);
                SetInputsEnabled(true);
                _isChecking = false;
                RestoreFocusState();
            }
        }

        // ======== Validaciones de estado ========
        private static bool IsClientIdValid(string s) => Regex.IsMatch(s ?? "", @"^\d{5}$");
        private static bool IsLicenseFormattedValid(string s) =>
            Regex.IsMatch(s ?? "", @"^[A-Z0-9]{4}(-[A-Z0-9]{4}){3}$"); // XXXX-XXXX-XXXX-XXXX

        private async Task CheckLicenseIfReadyAsync()
        {
            if (_isChecking) return;

            var clientId = (TxtClientId.Text ?? "").Trim();
            var license = (TxtLicense.Text ?? "").Trim();

            // Solo lanzamos si cumple exactamente: id 5 dígitos y licencia 19 chars formateada
            if (IsClientIdValid(clientId) && IsLicenseFormattedValid(license) && license.Length == 19)
            {
                try
                {
                    _isChecking = true;

                    // Capturar foco/caret ANTES de deshabilitar
                    CaptureFocusState();

                    // UI: loading + bordes neutros + botón deshabilitado + bloquear inputs
                    ShowInfo(null, null);
                    SetBordersNeutral();
                    AcceptBtn.IsEnabled = false;
                    ShowLoading(true);
                    SetInputsEnabled(false);

                    // Llamada real
                    var res = await _licenseService.ValidateLicenseAsync(clientId, license);

                    // 1) No encontrada (code 402 o status null)
                    if (res.code == 402 || string.IsNullOrEmpty(res.status))
                    {
                        ShowInfo("Licencia no encontrada.", Brushes.IndianRed);
                        SetBordersInvalid();
                        AcceptBtn.IsEnabled = false;
                        return;
                    }

                    // 2) Estados conocidos
                    var status = res.status!.ToLowerInvariant();
                    var usage = res.usage ?? 0;

                    if (status == "active")
                    {
                        if (usage == 0)
                        {
                            ShowInfo("Licencia válida.", Brushes.MediumSeaGreen);
                            SetBordersValid();
                            AcceptBtn.IsEnabled = true;   // habilitar Activar
                        }
                        else
                        {
                            ShowInfo("La licencia ya está en uso.", Brushes.IndianRed);
                            SetBordersInvalid();
                            AcceptBtn.IsEnabled = false;
                        }
                    }
                    else
                    {
                        string msg = status switch
                        {
                            "inactive" => "La licencia no está activa.",
                            "revoked" => "La licencia ha sido revocada.",
                            "expired" => "La licencia ha expirado.",
                            _ => "Licencia inválida."
                        };

                        ShowInfo(msg, Brushes.IndianRed);
                        SetBordersInvalid();
                        AcceptBtn.IsEnabled = false;
                    }
                }
                finally
                {
                    ShowLoading(false);
                    SetInputsEnabled(true);
                    _isChecking = false;

                    // Restaurar foco/caret exactamente donde estaba
                    RestoreFocusState();
                }
            }
            else
            {
                // No cumple -> feedback neutro, botón deshabilitado
                AcceptBtn.IsEnabled = false;
                if (!_isChecking)
                {
                    SetBordersNeutral();
                    ShowInfo(null, null);
                }
            }
        }

        private void OnClientIdChanged(object? sender, TextChangedEventArgs e)
        {
            var original = TxtClientId.Text ?? string.Empty;
            var digitsOnly = new string(original.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 5) digitsOnly = digitsOnly[..5];

            if (digitsOnly != original)
            {
                TxtClientId.Text = digitsOnly;
                TxtClientId.SelectionStart = System.Math.Min(_clientCaret, digitsOnly.Length);
            }

            _ = CheckLicenseIfReadyAsync(); // fire and forget

            // Si el ID ya tiene 5 dígitos, saltar automáticamente al input de licencia
            if (!_isChecking && IsClientIdValid(TxtClientId.Text ?? ""))
            {
                TxtLicense.Focus();
                // coloca el caret al final del texto actual (formateado con guiones si ya hay algo)
                TxtLicense.SelectionStart = (TxtLicense.Text ?? string.Empty).Length;
                TxtLicense.SelectionLength = 0;
            }
        }

        private void OnLicenseChanged(object? sender, TextChangedEventArgs e)
        {
            if (_updatingLicense) return;

            // 1) Calcular caret "raw" según selección formateada actual
            int caretFormatted = TxtLicense.SelectionStart;
            string beforeCaret = (TxtLicense.Text ?? string.Empty).Substring(0, caretFormatted);
            int rawCaretGuess = CountAlnum(beforeCaret);

            // 2) Normalizar a alfanumérico upper sin guiones (máx 16)
            string raw = NormalizeAlnumUpper(TxtLicense.Text);
            if (raw.Length > 16) raw = raw[..16];

            // 3) Formatear a XXXX-XXXX-XXXX-XXXX
            string formatted = FormatLicense(raw);

            // 4) Mapear caret raw -> caret formateado
            int newCaret = MapRawCaretToFormatted(rawCaretGuess, raw.Length);

            // 5) Aplicar
            _updatingLicense = true;
            TxtLicense.Text = formatted;
            TxtLicense.SelectionStart = newCaret;
            _updatingLicense = false;

            // Guardar caret raw para restauración de foco tras validar
            _licenseRawCaret = rawCaretGuess;

            _ = CheckLicenseIfReadyAsync(); // fire and forget
        }

        // ======== Helpers de licencia ========
        private static string NormalizeAlnumUpper(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var only = new string(s.Where(char.IsLetterOrDigit).ToArray());
            return only.ToUpperInvariant();
        }

        private static string FormatLicense(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var parts = Enumerable.Range(0, (raw.Length + 3) / 4)
                                  .Select(i => raw.Substring(i * 4, System.Math.Min(4, raw.Length - (i * 4))));
            return string.Join("-", parts);
        }

        private static int CountAlnum(string s)
        {
            int c = 0;
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) c++;
            return c;
        }

        private static int MapRawCaretToFormatted(int rawCaret, int rawLength)
        {
            if (rawCaret < 0) rawCaret = 0;
            if (rawCaret > rawLength) rawCaret = rawLength;
            return rawCaret + (rawCaret / 4);
        }

        // ======== UI helpers (loading, info, enable/disable, borders) ========
        private void SetInputsEnabled(bool enabled)
        {
            TxtClientId.IsEnabled = enabled;
            TxtLicense.IsEnabled = enabled;
        }

        private void ShowLoading(bool show)
        {
            if (PbLoading == null) return;
            PbLoading.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowInfo(string? text, Brush? color)
        {
            if (LblInfo == null) return;

            if (string.IsNullOrWhiteSpace(text))
            {
                LblInfo.Visibility = Visibility.Collapsed;
                LblInfo.Text = string.Empty;
            }
            else
            {
                LblInfo.Text = text;
                if (color != null) LblInfo.Foreground = color;
                LblInfo.Visibility = Visibility.Visible;
            }
        }

        private void SetBordersNeutral()
        {
            TxtClientId.ClearValue(BorderBrushProperty);
            TxtLicense.ClearValue(BorderBrushProperty);
        }

        private void SetBordersValid()
        {
            var green = Brushes.MediumSeaGreen;
            TxtClientId.BorderBrush = green;
            TxtLicense.BorderBrush = green;
        }

        private void SetBordersInvalid()
        {
            var red = Brushes.IndianRed;
            TxtClientId.BorderBrush = red;
            TxtLicense.BorderBrush = red;
        }

        // ======== Foco/caret: capturar y restaurar sin perder experiencia ========
        private void CaptureFocusState()
        {
            _lastFocused = Keyboard.FocusedElement;

            if (_lastFocused == TxtClientId)
            {
                _clientCaret = TxtClientId.SelectionStart;
            }
            else if (_lastFocused == TxtLicense)
            {
                int f = TxtLicense.SelectionStart;
                string before = (TxtLicense.Text ?? string.Empty).Substring(0, f);
                _licenseRawCaret = CountAlnum(before); // contar sin guiones
            }
        }

        private void RestoreFocusState()
        {
            if (_lastFocused == TxtLicense)
            {
                TxtLicense.Focus();

                // Mapear caret raw guardado a caret con guiones
                // Recalcular contra el raw actual por si cambió
                string raw = NormalizeAlnumUpper(TxtLicense.Text);
                if (raw.Length > 16) raw = raw[..16];
                int caretFormatted = MapRawCaretToFormatted(_licenseRawCaret, raw.Length);

                TxtLicense.SelectionStart = System.Math.Min(caretFormatted, (TxtLicense.Text ?? string.Empty).Length);
                TxtLicense.SelectionLength = 0;
            }
            else if (_lastFocused == TxtClientId)
            {
                TxtClientId.Focus();
                TxtClientId.SelectionStart = System.Math.Min(_clientCaret, (TxtClientId.Text ?? string.Empty).Length);
                TxtClientId.SelectionLength = 0;
            }
            else
            {
                // fallback: dejar el foco donde sea más útil
                if (IsClientIdValid(TxtClientId.Text ?? ""))
                {
                    TxtLicense.Focus();
                    TxtLicense.SelectionStart = (TxtLicense.Text ?? string.Empty).Length;
                }
                else
                {
                    TxtClientId.Focus();
                    TxtClientId.SelectionStart = (TxtClientId.Text ?? string.Empty).Length;
                }
            }
        }
    }
}
