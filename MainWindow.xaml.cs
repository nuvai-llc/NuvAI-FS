using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"{AppInfo.ProductName} v{AppInfo.InformationalVersion}";
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateBtn.IsEnabled = false;
            try
            {
<<<<<<< HEAD
                // IMPORTANTE: usa la URL completa del repo
                // Para repo privado: pon el token en el 2º parámetro en lugar de ""
                var source = new GithubSource(
                    repoUrl: "https://github.com/nuvai-llc/NuvAI-FS",
                    accessToken: "",
                    prerelease: false
                );
=======
                // La carpeta DEBE contener paquetes + releases.json (o releases.{channel}.json)
                var mgr = new UpdateManager(@"https://pub-ad842211e29b462e97dfbfd5bb04312c.r2.dev/fs");
>>>>>>> 55663132d599ae9c9e005ee37c7e19335329f3f1

                var info = await mgr.CheckForUpdatesAsync();
                if (info == null)
                {
                    MessageBox.Show("Ya estás en la última versión.");
                    return;
                }

                await mgr.DownloadUpdatesAsync(info);
                mgr.ApplyUpdatesAndRestart(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error al actualizar");
            }
            finally
            {
                UpdateBtn.IsEnabled = true;
            }
        }


        private void SubmitBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Hola, {MainTxt.Text}");
        }

    }
}
