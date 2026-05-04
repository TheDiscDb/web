namespace TheDiscDb.Chapters;

using System.Xml.Linq;

/// <summary>
/// Parses chapter names from the ChapterGrabber XML format.
/// Expects a &lt;chapterInfo&gt; root with &lt;chapters&gt; containing &lt;chapter name="..." /&gt; elements.
/// </summary>
public class XmlChapterParser : IChapterParser
{
    private static readonly XNamespace ChapterGrabberNamespace =
        "http://jvance.com/2008/ChapterGrabber";

    public bool TryParse(string text, out List<string> chapterNames)
    {
        chapterNames = [];

        try
        {
            var doc = XDocument.Parse(text);
            var root = doc.Root;
            if (root == null)
            {
                return false;
            }

            // Try with the ChapterGrabber namespace first, then without namespace
            var chaptersElement = root.Element(ChapterGrabberNamespace + "chapters")
                                 ?? root.Element("chapters");

            if (chaptersElement == null)
            {
                return false;
            }

            var chapterElements = chaptersElement.Elements(ChapterGrabberNamespace + "chapter")
                .Concat(chaptersElement.Elements("chapter"));

            foreach (var chapter in chapterElements)
            {
                var name = chapter.Attribute("name")?.Value;
                if (name != null)
                {
                    chapterNames.Add(name);
                }
            }

            return chapterNames.Count > 0;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }
}
