namespace TheDiscDb.ImportModels
{
    public class BoxSetDisc
    {
        public int Index { get; set; }
        public string? Name { get; set; }
        public string? Format { get; set; }
        public string? Slug { get; set; }
        // Copied discs can have a different slug in the source bundle than in this boxset.
        public string? SourceDiscSlug { get; set; }
        public string? TitleSlug { get; set; }
    }
}
