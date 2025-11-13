using System.Windows.Input;

namespace NuvAI_FS.Src.Presentation.Commands
{
    public static class UiCommands
    {
        public static readonly RoutedUICommand Restart =
            new RoutedUICommand("Reiniciar", "Restart", typeof(UiCommands));
    }
}
