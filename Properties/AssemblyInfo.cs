using System.Reflection;
using System.Resources;
using System.Windows;

// Metadatos (aparecen en Detalles del archivo)
[assembly: AssemblyTitle("NuvAI FS")]
[assembly: AssemblyCompany("NuvAI LLC")]
[assembly: AssemblyProduct("NuvAI FS")]

// Versión unificada (ajustada por CI)
[assembly: AssemblyVersion("1.0.16.0")]            // CLR/bindings
[assembly: AssemblyFileVersion("1.0.16.0")]        // Versión de archivo
[assembly: AssemblyInformationalVersion("1.0.16")] // SemVer visible (UI, logs)

[assembly: NeutralResourcesLanguage("es-ES")]

// WPF ThemeInfo
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
