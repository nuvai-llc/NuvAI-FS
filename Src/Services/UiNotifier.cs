using System.Diagnostics;
using System.Windows;

namespace NuvAI_FS.Src.Services
{
    public interface IUiNotifier
    {
        void ShowInfo(string title, string text);
    }

    public sealed class WpfUiNotifier : IUiNotifier
    {
        public void ShowInfo(string title, string text)
        {
            var app = Application.Current;
            if (app?.Dispatcher is not null)
            {
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information); }
                    catch (Exception ex) { Debug.WriteLine("[UiNotifier] " + ex.Message); }
                }));
            }
            else
            {
                Debug.WriteLine($"[UiNotifier] {title}\n{text}");
            }
        }
    }
}
