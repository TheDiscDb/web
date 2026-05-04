namespace TheDiscDb.Chapters;

using System.Xml.Linq;
using TheDiscDb.InputModels;

/// <summary>
/// Formats chapters as ChapterGrabber XML format.
/// </summary>
public class XmlChapterFormatter : IChapterFormatter
{
    private static readonly XNamespace ChapterGrabberNamespace =
        "http://jvance.com/2008/ChapterGrabber";

    public string FormatName => "XML";

    public string Format(IEnumerable<Chapter> chapters)
    {
        var chaptersElement = new XElement(ChapterGrabberNamespace + "chapters");
        foreach (var chapter in chapters)
        {
            chaptersElement.Add(new XElement(ChapterGrabberNamespace + "chapter",
                new XAttribute("time", "00:00:00"),
                new XAttribute("name", chapter.Title ?? string.Empty)));
        }

        var doc = new XDocument(
            new XElement(ChapterGrabberNamespace + "chapterInfo",
                new XAttribute("version", "2"),
                chaptersElement));

        return doc.ToString();
    }
}
