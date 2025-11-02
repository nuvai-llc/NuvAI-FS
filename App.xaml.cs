using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            VelopackApp.Build().Run();
        }

    }

}
