// src/Common/AppInfo.cs
using System.Reflection;

namespace NuvAI_FS.Src.Common
{
    public static class AppInfo
    {
        public const string SemVer = "1.0.22";     // único sitio a tocar
        public const string FileVer = SemVer + ".0";
        public const string AssemblyVer = SemVer + ".0";

        public static string ProductName =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? Assembly.GetExecutingAssembly().GetName().Name
            ?? "NuvAI FS";

        // NUEVO: nombre de compañía (para claves de registro, etc.)
        public static string Company =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
            ?? "NuvAI LLC";

        // NUEVO: product “limpio” si lo quieres distinto al de ProductName
        public static string Product =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? "NuvAI FS";

        public static string InformationalVersion =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
