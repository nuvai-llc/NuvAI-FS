using NuvAI_FS.src.Common;
using System.Reflection;
using System.Resources;
using System.Windows;

// Metadatos (aparecen en Detalles del archivo)
[assembly: AssemblyTitle("NuvAI FS")]
[assembly: AssemblyCompany("NuvAI LLC")]
[assembly: AssemblyProduct("NuvAI FS")]

// Versión unificada (ajustada por CI)
[assembly: AssemblyVersion(AppInfo.AssemblyVer)]            // CLR/bindings
[assembly: AssemblyFileVersion(AppInfo.FileVer)]        // Versión de archivo
[assembly: AssemblyInformationalVersion(AppInfo.SemVer)] // SemVer visible (UI, logs)

[assembly: NeutralResourcesLanguage("es-ES")]

// WPF ThemeInfo
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
