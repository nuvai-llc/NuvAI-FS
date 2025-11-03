// AppInfo.cs
using System.Reflection;

namespace NuvAI_FS
{
    public static class AppInfo
    {
        public static string ProductName =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? Assembly.GetExecutingAssembly().GetName().Name
            ?? "NuvAI FS";

        public static string InformationalVersion =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
