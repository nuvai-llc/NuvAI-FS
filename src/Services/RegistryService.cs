// src/Services/RegistryService.cs
#nullable enable
using Microsoft.Win32;
using NuvAI_FS.Src.Common; // AppInfo.Company / AppInfo.Product
using System;
using System.Linq;
using System.Runtime.Versioning;

namespace NuvAI_FS.Src.Services
{
    /// <summary>
    /// Helper genérico y reusable para trabajar con el Registro.
    /// Incluye 4 operaciones básicas (GET/SET/DELETE/UPDATE) y
    /// atajos "app-scoped" en HKCU\Software\<Company>\<Product>
    /// usando AppInfo.Company y AppInfo.Product.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class RegistryService
    {
        // ====== Sanitizado mínimo para subclaves/segmentos ======
        private static string Sanitize(string? s)
            => string.IsNullOrEmpty(s)
                ? string.Empty
                : new string(s.Select(ch => (ch == '\\' || ch < 32) ? '_' : ch).ToArray());

        // ====== Apertura segura de BaseKey ======
        private static RegistryKey? OpenBase(RegistryHive hive, RegistryView view)
        {
            try { return RegistryKey.OpenBaseKey(hive, view); }
            catch { return null; }
        }

        // ========================================================
        // 1) GET: lee un valor (objeto) desde el registro
        // ========================================================
        public static object? GetKey(
            RegistryHive hive,
            string subKeyPath,
            string valueName,
            RegistryView view = RegistryView.Default)
        {
            try
            {
                using var baseKey = OpenBase(hive, view);
                using var subKey = baseKey?.OpenSubKey(subKeyPath, writable: false);
                return subKey?.GetValue(valueName);
            }
            catch { return null; }
        }

        // ========================================================
        // 2) SET: crea o sobrescribe un valor
        // ========================================================
        public static bool SetKey(
            RegistryHive hive,
            string subKeyPath,
            string valueName,
            object? value,
            RegistryValueKind kind = RegistryValueKind.String,
            RegistryView view = RegistryView.Default)
        {
            try
            {
                using var baseKey = OpenBase(hive, view);
                using var subKey = baseKey?.CreateSubKey(subKeyPath);
                if (subKey is null) return false;

                subKey.SetValue(valueName, value ?? string.Empty, kind);
                return true;
            }
            catch { return false; }
        }

        // ========================================================
        // 3) DELETE: borra solo el valor (no la subclave)
        // ========================================================
        public static bool DeleteKey(
            RegistryHive hive,
            string subKeyPath,
            string valueName,
            RegistryView view = RegistryView.Default)
        {
            try
            {
                using var baseKey = OpenBase(hive, view);
                using var subKey = baseKey?.OpenSubKey(subKeyPath, writable: true);
                if (subKey is null) return true; // ya no existe
                subKey.DeleteValue(valueName, throwOnMissingValue: false);
                return true;
            }
            catch { return false; }
        }

        // ========================================================
        // 4) UPDATE: actualiza solo si el valor ya existe (no crea)
        // ========================================================
        public static bool UpdateKey(
            RegistryHive hive,
            string subKeyPath,
            string valueName,
            object? value,
            RegistryValueKind kind = RegistryValueKind.String,
            RegistryView view = RegistryView.Default)
        {
            try
            {
                using var baseKey = OpenBase(hive, view);
                using var subKey = baseKey?.OpenSubKey(subKeyPath, writable: true);
                if (subKey is null) return false;

                var exists = Array.Exists(subKey.GetValueNames(), n => string.Equals(n, valueName, StringComparison.Ordinal));
                if (!exists) return false;

                subKey.SetValue(valueName, value ?? string.Empty, kind);
                return true;
            }
            catch { return false; }
        }

        // ========================================================
        // Atajos “App-scoped” (HKCU\Software\<Company>\<Product>)
        // ========================================================

        // Valores crudos (para logs/UX)
        public static string AppRegSubKeyPathRaw => $@"Software\{AppInfo.Company}\{AppInfo.Product}";
        public static string AppRegFullPathRaw => $@"HKEY_CURRENT_USER\{AppRegSubKeyPathRaw}";

        // Ruta sanitizada para operaciones reales
        public static string AppSubKeyPath => $@"Software\{Sanitize(AppInfo.Company)}\{Sanitize(AppInfo.Product)}";

        // GET (objeto)
        public static object? GetAppKey(string valueName, RegistryView view = RegistryView.Default)
            => GetKey(RegistryHive.CurrentUser, AppSubKeyPath, valueName, view);

        // GET (string)
        public static string? GetAppKeyString(string valueName, RegistryView view = RegistryView.Default)
            => GetAppKey(valueName, view) as string;

        // SET
        public static bool SetAppKey(string valueName, object? value, RegistryValueKind kind = RegistryValueKind.String, RegistryView view = RegistryView.Default)
            => SetKey(RegistryHive.CurrentUser, AppSubKeyPath, valueName, value, kind, view);

        // DELETE
        public static bool DeleteAppKey(string valueName, RegistryView view = RegistryView.Default)
            => DeleteKey(RegistryHive.CurrentUser, AppSubKeyPath, valueName, view);

        // UPDATE
        public static bool UpdateAppKey(string valueName, object? value, RegistryValueKind kind = RegistryValueKind.String, RegistryView view = RegistryView.Default)
            => UpdateKey(RegistryHive.CurrentUser, AppSubKeyPath, valueName, value, kind, view);
    }
}
