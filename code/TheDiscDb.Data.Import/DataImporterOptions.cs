namespace TheDiscDb.Data.Import
{
    using System.Collections.Generic;

    public class DataImporterOptions
    {
        public ICollection<CompanyNameMapping> CompanyNameMappings { get; private set; } = new List<CompanyNameMapping>();
        public bool CleanImport { get; set; } = false;
    }

    public class CompanyNameMapping
    {
        public string FullName { get; set; }
        public string ShortName { get; set; }
    }

    public class BlobStorageOptions
    {
        public string? ContainerName { get; set; }
    }
}
