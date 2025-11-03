using System.Windows;

namespace NuvAI_FS.src.Presentation.Views.Shared
{
    public partial class LoadingDialog : Window
    {
        public LoadingDialog()
        {
            InitializeComponent();
        }

        public void SetText(string text)
        {
            LblText.Text = text ?? "Procesando...";
        }
    }
}
