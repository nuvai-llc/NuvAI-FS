using System;
using System.Threading.Tasks;
using System.Windows;

namespace NuvAI_FS.src.Presentation.Views.Shared
{
    public static class Dialogs
    {
        /// <summary>
        /// Abre un LoadingDialog con el texto dado, ejecuta 'action' y cierra el diálogo al terminar.
        /// </summary>
        public static async Task RunWithLoadingAsync(Window owner, string text, Func<Task> action)
        {
            var dlg = new LoadingDialog
            {
                Owner = owner
            };
            dlg.SetText(text);
            dlg.Show();
            try
            {
                await action();
            }
            finally
            {
                try { dlg.Close(); } catch { /* noop */ }
            }
        }

        /// <summary>
        /// Versión con retorno de resultado.
        /// </summary>
        public static async Task<T> RunWithLoadingAsync<T>(Window owner, string text, Func<Task<T>> action)
        {
            var dlg = new LoadingDialog
            {
                Owner = owner
            };
            dlg.SetText(text);
            dlg.Show();
            try
            {
                return await action();
            }
            finally
            {
                try { dlg.Close(); } catch { /* noop */ }
            }
        }
    }
}
