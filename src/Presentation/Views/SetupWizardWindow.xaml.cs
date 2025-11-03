using NuvAI_FS.src.Presentation.Setup;
using NuvAI_FS.src.Presentation.Views.WizardPages;
using NuvAI_FS.src.Services;
using System.Windows;

namespace NuvAI_FS.src.Presentation.Views
{
    public partial class SetupWizardWindow : Window
    {
        private readonly SetupState _state = new();
        private readonly SettingsService _settings = new();

        private int _step = 1;
        private const int TOTAL_STEPS = 2;

        // nuevo: control de si el paso 1 ya fue validado explícitamente
        private bool _step1Validated = false;

        public SetupWizardWindow()
        {
            InitializeComponent();

            // Botones navegación
            BtnBack.Click += (_, __) => GoBack();
            BtnNext.Click += (_, __) => GoNext();
            BtnFinish.Click += (_, __) => Finish();

            // Cargar primera página con 2 callbacks: (validityChanged, validated)
            WizardFrame.Navigate(new SetupPathsPage(
                _state,
                OnStepValidityChanged,
                OnStep1Validated
            ));

            UpdateHeader();
            // al inicio, ocultamos "Siguiente" hasta que se pulse Validar con éxito
            UpdateActions(canNext: false, forceHideNext: true, showFinish: false);
        }

        // la página nos avisa si su estado actual es válido (para habilitar/bloquear Validar allí si quieres)
        private void OnStepValidityChanged(bool isValid)
        {
            // aquí no mostramos "Siguiente": solo lo mostramos cuando se pulsa Validar y pasa
            // podrías usar este callback para habilitar/deshabilitar un botón "Validar" dentro de la page si lo deseas
        }

        // la página nos avisa cuando el usuario pulsó "Validar" y todo está OK
        private void OnStep1Validated()
        {
            _step1Validated = true;
            // ahora sí, mostramos "Siguiente"
            UpdateActions(canNext: true, forceHideNext: false, showFinish: false);
        }

        private void GoBack()
        {
            if (_step <= 1) return;
            _step--;
            if (_step == 1)
            {
                WizardFrame.Navigate(new SetupPathsPage(_state, OnStepValidityChanged, OnStep1Validated));
                UpdateHeader();
                UpdateActions(canNext: _step1Validated, forceHideNext: !_step1Validated, showFinish: false);
            }
        }

        private void GoNext()
        {
            if (_step == 1)
            {
                // solo dejamos pasar si ya fue validado
                if (!_step1Validated) return;

                _step = 2;
                WizardFrame.Navigate(new SetupSummaryPage(_state));
                UpdateHeader();
                UpdateActions(canNext: true, forceHideNext: true, showFinish: true);
            }
        }

        private void Finish()
        {
            // Persistir en registro
            _settings.SetString("FactusolInstallPath", _state.FactusolInstallPath);
            _settings.SetString("FactusolDataPath", _state.DataPath);
            _settings.MarkSetupCompleted();

            try
            {
                var version = NuvAI_FS.src.Common.AppInfo.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(version))
                    _settings.SetInstalledVersion(version);
            }
            catch { }

            try { this.DialogResult = true; } catch { }
            this.Close();
        }

        private void UpdateHeader()
        {
            LblStepNumber.Text = _step.ToString();
            LblStepTitle.Text = _step switch
            {
                1 => "Rutas de Factusol",
                2 => "Resumen",
                _ => string.Empty
            };
        }

        private void UpdateActions(bool canNext, bool forceHideNext, bool showFinish)
        {
            BtnBack.IsEnabled = _step > 1;

            if (showFinish)
            {
                BtnNext.Visibility = Visibility.Collapsed;
                BtnFinish.Visibility = Visibility.Visible;
            }
            else
            {
                BtnFinish.Visibility = Visibility.Collapsed;
                BtnNext.Visibility = forceHideNext ? Visibility.Collapsed : Visibility.Visible;
                BtnNext.IsEnabled = canNext;
            }
        }
    }
}
