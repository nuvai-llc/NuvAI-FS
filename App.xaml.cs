using System.Windows;
using Velopack;

namespace NuvAI_FS
{
    public partial class App : Application
    {
<<<<<<< HEAD
        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build().Run();
            App app = new();
            app.InitializeComponent();
            app.Run();
        }
        // The rest of your App.xaml.cs code goes here
=======
        public App()
        {
            VelopackApp.Build().Run();
        }

>>>>>>> 55663132d599ae9c9e005ee37c7e19335329f3f1
    }
}
