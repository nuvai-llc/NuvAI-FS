using NuvAI_FS.Src.Common;
using NuvAI_FS.Src.Presentation.Setup;
using NuvAI_FS.Src.Presentation.Views.WizardPages;
using NuvAI_FS.Src.Services;
using System;
using System.Runtime.Versioning;
using System.Windows;

namespace NuvAI_FS.Src.Presentation.Views
{
    [SupportedOSPlatform("windows")]
    public partial class SetupWizardWindow : Window
    {
        private readonly SetupState _state = new();
        private readonly SettingsService _settings = new();

        // 1: Info, 2: Rutas, 3: Resumen
        private int _step = 1;
        private const int TOTAL_STEPS = 3;

        // Habilitación dinámica del paso 2
        private bool _step2Validated = false;   // rutas OK
        private bool _companySelected = false;  // empresa seleccionada

        // Anti reentradas (doble click en botones en máquinas lentas)
        private bool _isBusy = false;

        public SetupWizardWindow()
        {
            InitializeComponent();

            BtnBack.Click += (_, __) => GoBack();
            BtnNext.Click += (_, __) => GoNext();
            BtnFinish.Click += (_, __) => Finish();

            NavigateStep1();
            UpdateHeader();
            UpdateActions();
        }

        // ===== Navegación por pasos =====
        private void NavigateStep1()
        {
            _step = 1;
            WizardFrame.Navigate(new SetupInfoPage());
        }

        private void NavigateStep2()
        {
            _step = 2;

            // Restaurar estado previo (por si el usuario vuelve atrás)
            _step2Validated = _state.PathsValidated;
            _companySelected = !string.IsNullOrWhiteSpace(_state.DatabasePath);

            WizardFrame.Navigate(new SetupPathsPage(
                _state,
                validityChanged: _ => { /* no usado aquí para Next; controlamos por callbacks explícitos */ },
                validatedCallback: OnStep2Validated,               // cuando "Validar" termina bien (rutas)
                companySelectedChanged: OnCompanySelectedChanged   // cuando elige empresa en el combo
            ));
        }

        private void NavigateStep3()
        {
            _step = 3;
            WizardFrame.Navigate(new SetupSummaryPage(_state));
        }

        // ===== Callbacks del paso 2 =====
        private void OnStep2Validated()
        {
            _step2Validated = true;
            _state.PathsValidated = true; // persistimos en el estado compartido
            UpdateActions();
        }

        private void OnCompanySelectedChanged(bool hasCompany)
        {
            _companySelected = hasCompany;
            UpdateActions();
        }

        // ===== Botones =====
        private void GoBack()
        {
            if (_isBusy) return;
            if (_step == 1) return;

            _isBusy = true;
            SetAllButtonsEnabled(false);

            try
            {
                if (_step == 3) NavigateStep2();
                else if (_step == 2) NavigateStep1();

                UpdateHeader();
                UpdateActions();
            }
            finally
            {
                _isBusy = false;
                SetAllButtonsEnabled(true);
            }
        }

        private void GoNext()
        {
            if (_isBusy) return;

            _isBusy = true;
            SetAllButtonsEnabled(false);

            try
            {
                if (_step == 1)
                {
                    NavigateStep2();
                }
                else if (_step == 2)
                {
                    // Sólo avanzamos si ya se han validado rutas y hay empresa seleccionada
                    if (!(_step2Validated && _companySelected))
                    {
                        // No avanzamos, pero tampoco crasheamos
                        return;
                    }
                    NavigateStep3();
                }

                UpdateHeader();
                UpdateActions();
            }
            finally
            {
                _isBusy = false;
                SetAllButtonsEnabled(true);
            }
        }

        private void Finish()
        {
            if (_isBusy) return;

            _isBusy = true;
            SetAllButtonsEnabled(false);

            try
            {
                // Validación final defensiva (por si el usuario llegó aquí de forma anómala)
                if (!_state.PathsValidated || string.IsNullOrWhiteSpace(_state.DatabasePath))
                {
                    SafeInfo("Debes validar las rutas y seleccionar una empresa antes de finalizar.", "Faltan datos");
                    return;
                }

                if (!SettingsService.IsStateValid(_state))
                {
                    SafeInfo("Las rutas indicadas no son válidas o la base de datos seleccionada no existe.", "Datos no válidos");
                    return;
                }

                var version = AppInfo.InformationalVersion ?? string.Empty;

                // Guardado robusto con rollback interno (no lanza excepción)
                var ok = _settings.TrySaveSetup(_state, installedVersion: version, markCompleted: true);
                if (!ok)
                {
                    SafeInfo("No se pudo guardar la configuración. Revisa permisos o vuelve a intentarlo.", "Guardado fallido");
                    return;
                }

                try { this.DialogResult = true; } catch { /* noop */ }
                Close();
            }
            finally
            {
                _isBusy = false;
                // No re-habilitamos botones si vamos a cerrar, pero por seguridad:
                SetAllButtonsEnabled(true);
            }
        }

        // ===== UI util =====
        private void UpdateHeader()
        {
            LblStepNumber.Text = _step.ToString();
            LblStepTitle.Text = _step switch
            {
                1 => "Información",
                2 => "Rutas de Factusol",
                3 => "Resumen",
                _ => string.Empty
            };
        }

        private void UpdateActions()
        {
            // Visibilidad por paso
            BtnBack.Visibility = (_step == 1) ? Visibility.Collapsed : Visibility.Visible;
            BtnNext.Visibility = (_step == 3) ? Visibility.Collapsed : Visibility.Visible;
            BtnFinish.Visibility = (_step == 3) ? Visibility.Visible : Visibility.Collapsed;

            // Habilitación
            if (_step == 1)
            {
                BtnNext.IsEnabled = true;
            }
            else if (_step == 2)
            {
                BtnNext.IsEnabled = _step2Validated && _companySelected;
                BtnNext.Opacity = BtnNext.IsEnabled ? 1.0 : 0.5;
            }
            else if (_step == 3)
            {
                BtnFinish.IsEnabled = true;
            }
        }

        private void SetAllButtonsEnabled(bool enabled)
        {
            BtnBack.IsEnabled = enabled && BtnBack.Visibility == Visibility.Visible;
            BtnNext.IsEnabled = enabled && BtnNext.Visibility == Visibility.Visible;
            BtnFinish.IsEnabled = enabled && BtnFinish.Visibility == Visibility.Visible;
        }

        // ===== Mensajes seguros (sin crashear si no hay owner visible) =====
        private void SafeInfo(string text, string title)
        {
            try
            {
                var owner = this; // la propia ventana del wizard
                if (owner != null && owner.IsVisible)
                    MessageBox.Show(owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { /* noop */ }
        }
    }
}
