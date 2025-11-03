// src/Presentation/Views/WizardPages/SetupSummaryPage.xaml.cs
using NuvAI_FS.src.Presentation.Setup;
using System.Windows.Controls;

namespace NuvAI_FS.src.Presentation.Views.WizardPages
{
    public partial class SetupSummaryPage : Page
    {
        public SetupSummaryPage(SetupState state)
        {
            InitializeComponent();

            LblInstall.Text = state.FactusolInstallPath;
            LblBackups.Text = state.DataPath;
        }
    }
}
