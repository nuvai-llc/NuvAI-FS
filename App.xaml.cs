using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    public partial class App : Application
    {
        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build().Run();
            App app = new();
            app.InitializeComponent();
            app.Run();
        }
        // The rest of your App.xaml.cs code goes here
    }
}
