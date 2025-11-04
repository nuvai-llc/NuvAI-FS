// src\Services\SettingsService.cs
using Microsoft.Win32;
using System.Reflection;
using System.Text.RegularExpressions;
using NuvAI_FS.src.Presentation.Setup;
using System.Runtime.Versioning;
using System;
using System.IO;

namespace NuvAI_FS.src.Services
{
    /// <summary>
    /// Servicio de settings en HKCU\Software\{Company}\{Product}
    /// - Robusto ante fallos (rollback si la escritura parcial falla).
    /// - Marca SetupCompleted SOLO al final (tras persistir todo).
    /// - Pensado para máquinas de muy bajos recursos (sin allocs excesivas ni async).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class SettingsService
    {
        private static readonly string Company =
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
            ?? "NuvAI LLC";

        private static readonly string Product =
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? "NuvAI FS";

        // Sanitiza segmentos de clave (evitar '\' y control chars)
        private static string SanitizeKeySegment(string s)
            => Regex.Replace(s ?? string.Empty, @"[\\\x00-\x1F]", "_");

        // HKCU\Software\<Company>\<Product>
        private static readonly string RegBasePath =
            $@"Software\{SanitizeKeySegment(Company)}\{SanitizeKeySegment(Product)}";

        // Nombres de valores
        private const string RegNameSetupCompleted = "SetupCompleted";     // DWORD 0/1
        private const string RegNameInstalledVersion = "InstalledVersion";   // STRING

        // Rutas/empresa
        public const string KeyFactusolInstallPath = "FactusolInstallPath";  // STRING
        public const string KeyFactusolDataPath = "FactusolDataPath";     // STRING
        public const string KeyDatabasePath = "DatabasePath";         // STRING (.accdb)
        public const string KeyCompanyName = "CompanyName";          // STRING

        // Candado simple para escrituras (evita reentradas concurrentes intra-proc)
        private static readonly object _lock = new();

        // ====== First-run / completed ======
        public bool IsFirstRun()
        {
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                if (reg is null) return true;

                var val = reg.GetValue(RegNameSetupCompleted);
                if (val is int i) return i == 0;
                if (val is string s && int.TryParse(s, out var si)) return si == 0;
                return true;
            }
            catch
            {
                // Si no podemos leer, preferimos tratar como first-run (flujo conservador)
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

        // ====== Version ======
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
                reg?.SetValue(RegNameInstalledVersion, version ?? string.Empty, RegistryValueKind.String);
            }
            catch { /* noop */ }
        }

        // ====== String helpers genéricos ======
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

        // ====== Atajos específicos ======
        public string? GetFactusolInstallPath() => GetString(KeyFactusolInstallPath);
        public void SetFactusolInstallPath(string path) => SetString(KeyFactusolInstallPath, path?.Trim() ?? string.Empty);

        public string? GetFactusolDataPath() => GetString(KeyFactusolDataPath);
        public void SetFactusolDataPath(string path) => SetString(KeyFactusolDataPath, path?.Trim() ?? string.Empty);

        public string? GetDatabasePath() => GetString(KeyDatabasePath);
        public void SetDatabasePath(string path) => SetString(KeyDatabasePath, path?.Trim() ?? string.Empty);

        public string? GetCompanyName() => GetString(KeyCompanyName);
        public void SetCompanyName(string name) => SetString(KeyCompanyName, name?.Trim() ?? string.Empty);

        // ====== Validación de estado previa a guardar ======
        public static bool IsStateValid(SetupState state)
        {
            // Directorios deben existir; DatabasePath opcional pero si viene debe apuntar a .accdb válido
            var installOk = !string.IsNullOrWhiteSpace(state.FactusolInstallPath) && Directory.Exists(state.FactusolInstallPath);
            var dataOk = !string.IsNullOrWhiteSpace(state.DataPath) && Directory.Exists(state.DataPath);

            var db = state.DatabasePath;
            var dbOk = string.IsNullOrWhiteSpace(db)
                       || (File.Exists(db) && string.Equals(Path.GetExtension(db), ".accdb", StringComparison.OrdinalIgnoreCase));

            // CompanyName puede ser vacío si aún no se seleccionó (lo guardamos como "")
            return installOk && dataOk && dbOk;
        }

        // ====== Guardado robusto (con rollback) ======
        /// <summary>
        /// Guarda de una vez todos los datos relevantes del setup.
        /// Escribe primero todas las cadenas; SOLO si todo va bien, marca SetupCompleted.
        /// Si falla, restaura valores anteriores (best-effort) y NO marca SetupCompleted.
        /// </summary>
        public bool TrySaveSetup(SetupState state, string? installedVersion = null, bool markCompleted = true)
        {
            if (state is null) return false;

            // Normaliza/trim
            var install = state.FactusolInstallPath?.Trim() ?? string.Empty;
            var datos = state.DataPath?.Trim() ?? string.Empty;
            var db = state.DatabasePath?.Trim() ?? string.Empty;
            var name = state.CompanyName?.Trim() ?? string.Empty;

            // Validación rápida (evita escribir basura)
            if (!IsStateValid(state)) return false;

            lock (_lock)
            {
                // Snapshot previo para rollback
                var snapshot = ReadSnapshot();

                try
                {
                    using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
                    if (reg is null) return false;

                    reg.SetValue(KeyFactusolInstallPath, install, RegistryValueKind.String);
                    reg.SetValue(KeyFactusolDataPath, datos, RegistryValueKind.String);
                    reg.SetValue(KeyDatabasePath, db, RegistryValueKind.String);
                    reg.SetValue(KeyCompanyName, name, RegistryValueKind.String);

                    if (!string.IsNullOrWhiteSpace(installedVersion))
                        reg.SetValue(RegNameInstalledVersion, installedVersion, RegistryValueKind.String);

                    if (markCompleted)
                        reg.SetValue(RegNameSetupCompleted, 1, RegistryValueKind.DWord);

                    return true;
                }
                catch
                {
                    // Intento de rollback
                    try { RestoreSnapshot(snapshot); } catch { /* noop */ }
                    return false;
                }
            }
        }

        /// <summary>
        /// Versión síncrona clásica (mantiene compat.), deja de marcar completado si falla.
        /// </summary>
        public void SaveSetup(SetupState state, string? installedVersion = null, bool markCompleted = true)
        {
            _ = TrySaveSetup(state, installedVersion, markCompleted);
        }

        // ====== Carga de settings a State ======
        /// <summary>
        /// Intenta cargar lo guardado hacia un SetupState existente.
        /// </summary>
        public bool TryLoadSetup(SetupState target)
        {
            if (target is null) return false;
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                if (reg is null) return false;

                target.FactusolInstallPath = (reg.GetValue(KeyFactusolInstallPath) as string) ?? string.Empty;
                target.DataPath = (reg.GetValue(KeyFactusolDataPath) as string) ?? string.Empty;
                target.DatabasePath = (reg.GetValue(KeyDatabasePath) as string) ?? string.Empty;
                target.CompanyName = (reg.GetValue(KeyCompanyName) as string) ?? string.Empty;

                // Derivar flag de validación básica (no asumimos completado)
                target.PathsValidated = !string.IsNullOrWhiteSpace(target.FactusolInstallPath)
                                        && !string.IsNullOrWhiteSpace(target.DataPath)
                                        && Directory.Exists(target.FactusolInstallPath)
                                        && Directory.Exists(target.DataPath);

                return true;
            }
            catch { return false; }
        }

        // ====== Reset (para re-hacer el wizard) ======
        /// <summary>
        /// Limpia los valores de setup (no elimina InstalledVersion).
        /// </summary>
        public bool ResetSetup()
        {
            lock (_lock)
            {
                try
                {
                    using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
                    if (reg is null) return false;

                    reg.DeleteValue(KeyFactusolInstallPath, throwOnMissingValue: false);
                    reg.DeleteValue(KeyFactusolDataPath, throwOnMissingValue: false);
                    reg.DeleteValue(KeyDatabasePath, throwOnMissingValue: false);
                    reg.DeleteValue(KeyCompanyName, throwOnMissingValue: false);
                    reg.DeleteValue(RegNameSetupCompleted, throwOnMissingValue: false);
                    return true;
                }
                catch { return false; }
            }
        }

        // ====== Snapshot para rollback ======
        private readonly struct Snapshot
        {
            public readonly bool Exists;
            public readonly string? Install;
            public readonly string? Datos;
            public readonly string? Db;
            public readonly string? Name;
            public readonly object? Completed;      // guardamos el raw por si era string/int
            public readonly string? Version;

            public Snapshot(bool exists,
                            string? install, string? datos, string? db, string? name,
                            object? completed, string? version)
            {
                Exists = exists; Install = install; Datos = datos; Db = db; Name = name; Completed = completed; Version = version;
            }
        }

        private Snapshot ReadSnapshot()
        {
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(RegBasePath, writable: false);
                if (reg is null) return new Snapshot(false, null, null, null, null, null, null);

                return new Snapshot(
                    true,
                    reg.GetValue(KeyFactusolInstallPath) as string,
                    reg.GetValue(KeyFactusolDataPath) as string,
                    reg.GetValue(KeyDatabasePath) as string,
                    reg.GetValue(KeyCompanyName) as string,
                    reg.GetValue(RegNameSetupCompleted),
                    reg.GetValue(RegNameInstalledVersion) as string
                );
            }
            catch
            {
                return new Snapshot(false, null, null, null, null, null, null);
            }
        }

        private void RestoreSnapshot(Snapshot s)
        {
            using var reg = Registry.CurrentUser.CreateSubKey(RegBasePath);
            if (reg is null) return;

            if (!s.Exists)
            {
                // No había valores: elimina los que hayamos podido crear
                reg.DeleteValue(KeyFactusolInstallPath, throwOnMissingValue: false);
                reg.DeleteValue(KeyFactusolDataPath, throwOnMissingValue: false);
                reg.DeleteValue(KeyDatabasePath, throwOnMissingValue: false);
                reg.DeleteValue(KeyCompanyName, throwOnMissingValue: false);
                reg.DeleteValue(RegNameSetupCompleted, throwOnMissingValue: false);
                // Ojo: mantenemos InstalledVersion si la había antes (s.Version == null → no tocar)
                return;
            }

            // Restaura valores previos
            if (s.Install is null) reg.DeleteValue(KeyFactusolInstallPath, false); else reg.SetValue(KeyFactusolInstallPath, s.Install, RegistryValueKind.String);
            if (s.Datos is null) reg.DeleteValue(KeyFactusolDataPath, false); else reg.SetValue(KeyFactusolDataPath, s.Datos, RegistryValueKind.String);
            if (s.Db is null) reg.DeleteValue(KeyDatabasePath, false); else reg.SetValue(KeyDatabasePath, s.Db, RegistryValueKind.String);
            if (s.Name is null) reg.DeleteValue(KeyCompanyName, false); else reg.SetValue(KeyCompanyName, s.Name, RegistryValueKind.String);

            // SetupCompleted podía ser string o dword previamente; respetar tipo si es posible
            if (s.Completed is null)
            {
                reg.DeleteValue(RegNameSetupCompleted, false);
            }
            else
            {
                if (s.Completed is int i)
                    reg.SetValue(RegNameSetupCompleted, i, RegistryValueKind.DWord);
                else if (s.Completed is string sc && int.TryParse(sc, out var si))
                    reg.SetValue(RegNameSetupCompleted, si, RegistryValueKind.DWord);
                else
                    reg.SetValue(RegNameSetupCompleted, s.Completed, RegistryValueKind.String);
            }

            if (s.Version is null)
                reg.DeleteValue(RegNameInstalledVersion, false);
            else
                reg.SetValue(RegNameInstalledVersion, s.Version, RegistryValueKind.String);
        }
    }
}
