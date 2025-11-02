using System.Reflection;
using System.Resources;
using System.Windows;

// ---- Metadatos opcionales (útiles para “Detalles del archivo”) ----
[assembly: AssemblyTitle("NuvAI FS")]
[assembly: AssemblyCompany("NuvAI LLC")]
[assembly: AssemblyProduct("NuvAI FS")]

// ---- Versión unificada (edita solo aquí) ----
[assembly: AssemblyVersion("1.0.2.0")]            // CLR/bindings
[assembly: AssemblyFileVersion("1.0.2.0")]        // Versión de archivo
[assembly: AssemblyInformationalVersion("1.0.2")] // SemVer visible (UI, logs)
[assembly: NeutralResourcesLanguage("es-ES")]

// ---- Lo que ya tenías ----
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
