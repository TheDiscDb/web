using IMDbApiLib.Models;
using TheDiscDb.ImportModels;
using TheDiscDb.InputModels;

namespace TheDiscDb.Data.Import.Pipeline;

public class ImportItem
{
    public MediaItem MediaItem { get; set; }
    public Boxset Boxset { get; set; }
    public MetadataFile Metadata { get; set; }
    public TitleData ImdbData { get; set; }
    public string BasePath { get; set; }
}