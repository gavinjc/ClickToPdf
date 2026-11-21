namespace ClickToPDF.App.ViewModels;

// The family's skipped wording (Bureau.Common app-conventions.md, "Skipped wording"): one item
// reads "Skipped notes.txt" or "Skipped photo.jpg: already added"; several read "2 files skipped:
// notes.txt, already added: photo.jpg". One string for one and one for many, never "(s)". The
// strings come in through getString because the app's ResourceLoader throws under the test runner.
public static class SkippedMessage
{
    public static string? Compose(
        IReadOnlyList<string> skippedNames,
        IReadOnlyList<string> alreadyAddedNames,
        Func<string, string> getString)
    {
        int count = skippedNames.Count + alreadyAddedNames.Count;
        if (count == 0)
        {
            return null;
        }

        if (count == 1)
        {
            return skippedNames.Count == 1
                ? string.Format(getString("SkippedOneFormat"), skippedNames[0])
                : string.Format(getString("SkippedOneAlreadyAddedFormat"), alreadyAddedNames[0]);
        }

        IEnumerable<string> entries = skippedNames.Concat(
            alreadyAddedNames.Select(name => string.Format(getString("AlreadyAddedEntryFormat"), name)));
        return string.Format(getString("SkippedManyFormat"), count, string.Join(", ", entries));
    }
}
