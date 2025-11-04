// src/Infrastructure/Access/AceRuntime.cs
using Microsoft.Win32;
using System;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NuvAI_FS.Infrastructure.Access
{
    [SupportedOSPlatform("windows")]
    public static class AceRuntime
    {
        // URLs oficiales (válidas):
        private const string ACCESS_2016_X64 =
            "https://download.microsoft.com/download/3/5/c/35c84c36-661a-44e6-9324-8786b8dbe231/accessdatabaseengine_X64.exe";
        private const string ACCESS_2016_X86 =
            "https://download.microsoft.com/download/3/5/c/35c84c36-661a-44e6-9324-8786b8dbe231/accessdatabaseengine.exe";

        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Comprueba si existe el proveedor ACE (16.0 o 12.0).
        /// </summary>
        public static bool IsAceAvailable()
        {
            // 1) Intentar enumerar proveedores OLE DB
            try
            {
                using var dt = OleDbEnumerator.GetRootEnumerator();
                while (dt.Read())
                {
                    var prov = dt["SOURCES_NAME"]?.ToString() ?? "";
                    if (string.Equals(prov, "Microsoft.ACE.OLEDB.16.0", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(prov, "Microsoft.ACE.OLEDB.12.0", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch
            {
                // 2) Fallback: comprobar claves típicas en el registro
                if (HasClassesKey(@"Microsoft.ACE.OLEDB.16.0")) return true;
                if (HasClassesKey(@"Microsoft.ACE.OLEDB.12.0")) return true;
            }
            return false;

            static bool HasClassesKey(string subkey)
            {
                try { using var k = Registry.ClassesRoot.OpenSubKey(subkey); return k != null; }
                catch { return false; }
            }
        }

        public static string CurrentBitness() => Environment.Is64BitOperatingSystem ? "x64" : "x86";

        /// <summary>
        /// Asegura que ACE esté disponible. Si no lo está, pregunta al usuario y lo instala.
        /// En sistemas x64 intenta primero X64 y, si no queda disponible (conflictos), ofrece probar X86.
        /// </summary>
        public static async Task<bool> EnsureAceInstalledAsync(Window? owner, CancellationToken ct = default)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            if (IsAceAvailable())
                return true;

            // Preguntar si desea instalar
            var ask = MessageBox.Show(owner ?? Application.Current?.MainWindow,
                "Falta Microsoft Access Database Engine (ACE OLEDB), necesario para leer las bases de datos de Factusol.\n\n" +
                "¿Quieres descargar e instalarlo ahora?",
                "Componente necesario",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes)
                return false;

            bool is64 = Environment.Is64BitOperatingSystem;
            var primaryUrl = is64 ? ACCESS_2016_X64 : ACCESS_2016_X86;

            // Intento principal
            var ok = await DownloadAndInstallAsync(primaryUrl, is64, owner, ct);
            if (ok && IsAceAvailable())
                return true;

            // Ofrecer alternativa (arquitectura opuesta) — conflictos Office x86/x64
            var offerFallback = MessageBox.Show(owner ?? Application.Current?.MainWindow,
                "No se pudo completar la instalación en la arquitectura principal o ACE sigue sin detectarse.\n" +
                "En equipos con Office de distinta arquitectura a veces es necesario instalar el runtime alternativo.\n\n" +
                "¿Quieres probar con la otra arquitectura?",
                "Intentar alternativa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (offerFallback != MessageBoxResult.Yes)
                return IsAceAvailable();

            var altUrl = is64 ? ACCESS_2016_X86 : ACCESS_2016_X64;
            ok = await DownloadAndInstallAsync(altUrl, !is64, owner, ct);

            return ok && IsAceAvailable();
        }

        /// <summary>
        /// Descarga el instalador y lo ejecuta en modo silencioso. Devuelve true si el proceso terminó sin error (ExitCode 0).
        /// </summary>
        private static async Task<bool> DownloadAndInstallAsync(string url, bool x64, Window? owner, CancellationToken ct)
        {
            string archLabel = x64 ? "64-bit" : "32-bit (x86)";
            string tempFile = Path.Combine(Path.GetTempPath(),
                $"accessruntime_{(x64 ? "x64" : "x86")}_{DateTime.UtcNow:yyyyMMddHHmmss}.exe");

            try
            {
                // Descarga con HttpClient
                using (var http = new HttpClient() { Timeout = HttpTimeout })
                using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        MessageBox.Show(owner ?? Application.Current?.MainWindow,
                            $"No se pudo descargar el instalador ({archLabel}). Código HTTP: {(int)resp.StatusCode}",
                            "Descarga fallida",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return false;
                    }

                    await using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                    await resp.Content.CopyToAsync(fs, ct);
                }

                // Ejecutar instalador de forma silenciosa y elevada
                var psi = new ProcessStartInfo
                {
                    FileName = tempFile,
                    Arguments = "/quiet /norestart",   // silencioso
                    UseShellExecute = true,            // necesario para Verb=runas
                    Verb = "runas"                     // solicitar UAC si precisa
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    MessageBox.Show(owner ?? Application.Current?.MainWindow,
                        "No se pudo iniciar el instalador.",
                        "Instalación",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                // Esperar a que termine (10 min máx.)
                await Task.Run(() => p.WaitForExit(10 * 60 * 1000), ct);

                if (!p.HasExited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* noop */ }
                    MessageBox.Show(owner ?? Application.Current?.MainWindow,
                        "El instalador tardó demasiado y se detuvo.",
                        "Instalación",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                if (p.ExitCode == 0)
                    return true;

                MessageBox.Show(owner ?? Application.Current?.MainWindow,
                    $"El instalador ({archLabel}) terminó con código {p.ExitCode}.",
                    "Instalación incompleta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner ?? Application.Current?.MainWindow,
                    $"Error instalando el runtime ({archLabel}): {ex.Message}",
                    "Instalación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* noop */ }
            }
        }

        /// <summary>
        /// Ofrece instalación interactiva según bitness actual (atajo directo, si prefieres).
        /// </summary>
        public static async Task<bool> OfferInstallAsync(Window owner, CancellationToken ct = default)
        {
            if (IsAceAvailable()) return true;

            var bit = CurrentBitness();
            var res = MessageBox.Show(owner,
                $"Para leer las bases de datos .accdb necesitamos el motor de Access (ACE) {bit}.\n\n" +
                "¿Quieres instalarlo ahora? (recomendado)",
                "Motor Access no disponible",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return false;

            var url = bit == "x64" ? ACCESS_2016_X64 : ACCESS_2016_X86;
            var ok = await DownloadAndInstallAsync(url, bit == "x64", owner, ct);

            // Revalidar
            if (!ok || !IsAceAvailable())
            {
                MessageBox.Show(owner,
                    "El instalador terminó pero no se detecta ACE. Si tienes Office de distinta arquitectura, " +
                    "prueba a instalar el runtime alternativo (x86/x64).",
                    "Aviso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            MessageBox.Show(owner, "Motor de Access instalado correctamente.", "Listo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
    }
}
