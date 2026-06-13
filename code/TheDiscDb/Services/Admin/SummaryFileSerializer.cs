using Fantastic.FileSystem;
using TheDiscDb.Import;

namespace TheDiscDb.Services.Admin;

public class SummaryFileSerializer
{
    public static Task SerializeAsync(Stream stream, IEnumerable<SummaryFileItem> items, IFileSystem fileSystem, SummaryFileMetadata metadata)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);
        foreach (var item in items)
        {
            writer.WriteLine($"Name: {item.Name}");
            writer.WriteLine($"Source file name: {item.SourceFileName}");
            writer.WriteLine($"Duration: {item.Duration}");
            writer.WriteLine($"Chapters count: {item.ChapterCount}");
            writer.WriteLine($"Size: {item.Size}");
            writer.WriteLine($"Segment count: {item.SegmentCount}");
            writer.WriteLine($"Segment Map: {item.SegmentMap}");

            if (!string.IsNullOrEmpty(item.Description))
            {
                writer.WriteLine($"Description: {item.Description}");
            }

            if (!string.IsNullOrEmpty(item.Upc))
            {
                writer.WriteLine($"UPC: {item.Upc}");
            }

            if (!string.IsNullOrEmpty(item.Year))
            {
                writer.WriteLine($"Year: {item.Year}");
            }

            if (!string.IsNullOrEmpty(item.Season))
            {
                writer.WriteLine($"Season: {item.Season}");
            }

            if (!string.IsNullOrEmpty(item.Episode))
            {
                writer.WriteLine($"Episode: {item.Episode}");
            }

            foreach (var track in item.AudioTracks)
            {
                writer.WriteLine($"AudioTrack[{track.Index}]: {track.Name}");
            }

            if (item.Chapters.Any())
            {
                writer.WriteLine("Chapters:");
                foreach (var chapter in item.Chapters.OrderBy(c => c.Index))
                {
                    writer.WriteLine($"-{chapter.Name}");
                }
            }

            writer.WriteLine($"Type: {item.Type}");
            if (item.Type == "MainMovie")
            {
                writer.WriteLine($"Year: {metadata.Year}");
            }

            writer.WriteLine($"File name: {item.BuildFileName(fileSystem, metadata)}");
            writer.WriteLine();
        }

        return Task.CompletedTask;
    }
}

public record SummaryFileMetadata(int Year, string Resolution);

public class SummaryFileItem
{
    public string Name { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string ChapterCount { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string SegmentCount { get; set; } = string.Empty;
    public string SegmentMap { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Upc { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public List<SummaryFileChildItem> AudioTracks { get; set; } = new();
    public List<SummaryFileChildItem> Chapters { get; set; } = new();

    public string BuildFileName(IFileSystem fileSystem, SummaryFileMetadata metadata)
    {
        if (this.Type == "Episode")
        {
            int.TryParse(Season, out int seasonNumber);
            int.TryParse(Episode, out int episodeNumber);
            return fileSystem.CleanPath($"S{seasonNumber:00}.E{episodeNumber:00}.{Name}.mkv");
        }
        else if (this.Type == "MainMovie")
        {
            return $"{Name} ({metadata.Year}) [{metadata.Resolution}].mkv";
        }

        return fileSystem.CleanPath($"{Name}.mkv");
    }
}

public class SummaryFileChildItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}
