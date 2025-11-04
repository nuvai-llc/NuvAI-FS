namespace NuvAI_FS.src.Presentation.Setup
{
    public sealed class SetupState
    {
        public string FactusolInstallPath { get; set; } = @"C:\Software DELSOL\FACTUSOL";
        public string DataPath { get; set; } = @"C:\Software DELSOL\FACTUSOL\Datos";
        public string DatabasePath { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;

        public bool PathsValidated { get; set; }

        // Cache empresas
        public List<(string Name, string Path)>? CompanyList { get; set; }
        public bool CompanyListEnriched { get; set; }
    }

}
