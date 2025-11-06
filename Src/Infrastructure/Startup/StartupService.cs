#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NuvAI_FS.Src.Infrastructure.Startup
{
    /// <summary>Gestiona el registro HKCU\...\Run para iniciar con Windows.</summary>
    [SupportedOSPlatform("windows")]
    public static class StartupService
    {
        private const string RUN_SUBKEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Habilita/Deshabilita el inicio con Windows para la app actual.</summary>
        public static bool SetLaunchAtLogin(string appName, bool enabled, string? arguments = null)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_SUBKEY, writable: true)
                              ?? Registry.CurrentUser.CreateSubKey(RUN_SUBKEY);

                if (key is null) return false;

                if (!enabled)
                {
                    key.DeleteValue(appName, false);
                    return true;
                }

                // Ruta al EXE actual
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;

                var cmd = $"\"{exePath}\"";
                if (!string.IsNullOrWhiteSpace(arguments))
                    cmd += " " + arguments;

                key.SetValue(appName, cmd);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Devuelve true si está configurado para iniciar con Windows.</summary>
        public static bool IsLaunchAtLoginEnabled(string appName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_SUBKEY, writable: false);
                if (key is null) return false;
                var val = key.GetValue(appName) as string;
                return !string.IsNullOrWhiteSpace(val);
            }
            catch { return false; }
        }
    }
}
