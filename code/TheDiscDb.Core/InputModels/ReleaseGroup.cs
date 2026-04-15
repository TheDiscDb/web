namespace TheDiscDb.InputModels
{
    public class ReleaseGroup
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public int Id { get; set; }
        public int ReleaseId { get; set; }
        public int GroupId { get; set; }

        [HotChocolate.Data.UseFiltering]
        [HotChocolate.Data.UseSorting]
        public Release? Release { get; set; }

        [HotChocolate.Data.UseFiltering]
        [HotChocolate.Data.UseSorting]
        public Group? Group { get; set; }
    }
}
