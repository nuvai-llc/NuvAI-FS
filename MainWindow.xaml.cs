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
                // La carpeta DEBE contener paquetes + releases.json (o releases.{channel}.json)
                var mgr = new UpdateManager(@"C:\Temp\NuvAI LLC");

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
