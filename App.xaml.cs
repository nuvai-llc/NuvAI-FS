using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    public partial class App : Application
    {
        public App()
        {
            VelopackApp.Build().Run();
        }

    }
}
