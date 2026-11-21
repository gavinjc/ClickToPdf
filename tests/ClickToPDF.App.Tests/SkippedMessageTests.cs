using ClickToPDF.App.ViewModels;

namespace ClickToPDF.App.Tests;

// Runs against the app's real English strings, so a wording change in Resources.resw that breaks
// the family's skipped wording fails here.
public class SkippedMessageTests
{
    private static string? Compose(string[] skipped, string[] alreadyAdded) =>
        SkippedMessage.Compose(skipped, alreadyAdded, key => AppResw.EnglishStrings[key]);

    [Fact]
    public void NothingSkipped_GivesNoMessage()
    {
        Assert.Null(Compose([], []));
    }

    [Fact]
    public void OneUnsupportedFile()
    {
        Assert.Equal("Skipped notes.txt", Compose(["notes.txt"], []));
    }

    [Fact]
    public void OneDuplicate()
    {
        Assert.Equal("Skipped photo.jpg: already added", Compose([], ["photo.jpg"]));
    }

    [Fact]
    public void SeveralFiles_CountsAndListsThem_DuplicatesMarked()
    {
        Assert.Equal(
            "3 files skipped: notes.txt, already added: a.jpg, already added: b.png",
            Compose(["notes.txt"], ["a.jpg", "b.png"]));
    }
}
