using NuvAI_FS.Src.Presentation.Setup;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NuvAI_FS.Src.Presentation.Views.WizardPages
{
    public partial class SetupSummaryPage : Page
    {
        private readonly SetupState _state;

        public SetupSummaryPage(SetupState state)
        {
            InitializeComponent();
            _state = state;

            // Rellenar campos básicos
            LblInstall.Text = _state.FactusolInstallPath ?? "";
            LblDatos.Text = _state.DataPath ?? "";

            // Empresa (si hay .accdb seleccionado)
            var dbPath = _state.DatabasePath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                CompanyBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompanyBlock.Visibility = Visibility.Visible;
                LblDbPath.Text = dbPath;

                // Intentar leer CompanyName si existe en el estado; si no, usar nombre de archivo
                string? friendly = null;
                var pi = _state.GetType().GetProperty("CompanyName");
                if (pi != null && pi.PropertyType == typeof(string))
                {
                    friendly = pi.GetValue(_state) as string;
                }

                if (string.IsNullOrWhiteSpace(friendly))
                    friendly = Path.GetFileNameWithoutExtension(dbPath);

                LblCompanyName.Text = friendly ?? "(Empresa no especificada)";
            }
        }
    }
}
