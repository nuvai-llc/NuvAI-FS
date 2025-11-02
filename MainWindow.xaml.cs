using System;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace NuvAI_FS
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BuscarActualizacion_Click(object sender, RoutedEventArgs e)
        {
            TxtEstado.Text = "Buscando actualizaciones...";
            try
            {
                // IMPORTANTE: usa la URL completa del repo
                // Para repo privado: pon el token en el 2º parámetro en lugar de ""
                var source = new GithubSource(
                    repoUrl: "https://github.com/OWNER/REPO",
                    accessToken: "",
                    prerelease: false
                );

                var mgr = new UpdateManager(source);

                // Devuelve null si no hay update
                var info = await mgr.CheckForUpdatesAsync(); // null => ya estás al día
                if (info is null)
                {
                    TxtEstado.Text = "Ya estás en la última versión.";
                    return;
                }

                TxtEstado.Text = "Descargando actualización...";
                await mgr.DownloadUpdatesAsync(info); // prepara delta/full según corresponda

                TxtEstado.Text = "Aplicando y reiniciando...";
                // Tiene conversión implícita de UpdateInfo -> VelopackAsset
                mgr.ApplyUpdatesAndRestart(info);     // la app se cerrará y relanzará
            }
            catch (Exception ex)
            {
                TxtEstado.Text = $"Error al actualizar: {ex.Message}";
            }
        }
    }
}
