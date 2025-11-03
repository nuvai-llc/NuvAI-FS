// src\Services\SettingsService.cs
using Microsoft.Win32;
using System.Reflection;

namespace NuvAI_FS.src.Services
{
    public sealed class SettingsService
    {
        private static readonly string Company =
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
            ?? "NuvAI LLC";

        private static readonly string Product =
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? "NuvAI FS";

        // HKCU\Software\<Company>\<Product>
        private static readonly string RegBasePath = $@"Software\{Company}\{Product}";

        private const string RegNameSetupCompleted = "SetupCompleted";      // DWORD 0/1
        private const string RegNameInstalledVersion = "InstalledVersion";  // string

        public bool IsFirstRun()
        {
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                if (reg == null) return true;
                var val = reg.GetValue(RegNameSetupCompleted);
                if (val is int i) return i == 0;
                if (val is string s && int.TryParse(s, out var si)) return si == 0;
                return val == null; // si no existe, es primera vez
            }
            catch
            {
                // Si hay error leyendo, por seguridad tratamos como primera vez
                return true;
            }
        }

        public void MarkSetupCompleted()
        {
            try
            {
                using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
                reg?.SetValue(RegNameSetupCompleted, 1, RegistryValueKind.DWord);
            }
            catch { /* noop */ }
        }

        public string? GetInstalledVersion()
        {
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                return reg?.GetValue(RegNameInstalledVersion) as string;
            }
            catch { return null; }
        }

        public void SetInstalledVersion(string version)
        {
            try
            {
                using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
                reg?.SetValue(RegNameInstalledVersion, version ?? "", RegistryValueKind.String);
            }
            catch { /* noop */ }
        }

        // helpers genéricos clave/valor string
        public string? GetString(string key)
        {
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                return reg?.GetValue(key) as string;
            }
            catch { return null; }
        }

        public void SetString(string key, string value)
        {
            try
            {
                using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
                reg?.SetValue(key, value ?? string.Empty, RegistryValueKind.String);
            }
            catch { /* noop */ }
        }

    }
}
