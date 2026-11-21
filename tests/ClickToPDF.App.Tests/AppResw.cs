using System.Xml.Linq;

namespace ClickToPDF.App.Tests;

// The app's real English strings, read from Resources.resw as text: the tests can't use
// ResourceLoader, which needs the app's resources index.
internal static class AppResw
{
    public static readonly IReadOnlyDictionary<string, string> EnglishStrings = XDocument
        .Load(Path.Combine(RepoRoot.Find(), "src", "ClickToPDF.App", "Strings", "en-us", "Resources.resw"))
        .Root!
        .Elements("data")
        .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
}
