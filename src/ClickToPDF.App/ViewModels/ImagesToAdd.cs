namespace ClickToPDF.App.ViewModels;

/// <summary>
/// What a pick or drop adds to the set: the images, the names skipped as unusable, and the names
/// skipped because that file is already in the set.
/// </summary>
public sealed record ImagesToAdd(
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> SkippedNames,
    IReadOnlyList<string> AlreadyAddedNames)
{
    // A folder expands one level, the way the shell extension's ExpandFolder does, so a dropped
    // folder and a right-clicked one give the same pages: its subfolders are ignored, its other
    // files are skipped, and a folder with no image directly inside is skipped by its own name.
    // A file counts as already added by full path, ignoring case, whether it was loaded earlier or
    // came earlier in this same drop (a folder dropped together with one of its own images).
    public static ImagesToAdd From(IEnumerable<string> pickedPaths, IEnumerable<string> loadedPaths)
    {
        var seen = new HashSet<string>(loadedPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        var skippedNames = new List<string>();
        var alreadyAddedNames = new List<string>();

        void AddImage(string path)
        {
            if (seen.Add(Path.GetFullPath(path)))
            {
                paths.Add(path);
            }
            else
            {
                alreadyAddedNames.Add(Path.GetFileName(path));
            }
        }

        foreach (string path in pickedPaths)
        {
            if (Directory.Exists(path))
            {
                List<string> files = FilesDirectlyIn(path);
                List<string> images = files.Where(SupportedImages.IsSupported).ToList();
                if (images.Count == 0)
                {
                    skippedNames.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
                    continue;
                }

                images.ForEach(AddImage);
                skippedNames.AddRange(files.Where(f => !SupportedImages.IsSupported(f)).Select(f => Path.GetFileName(f)));
            }
            else if (SupportedImages.IsSupported(path))
            {
                AddImage(path);
            }
            else
            {
                skippedNames.Add(Path.GetFileName(path));
            }
        }

        return new ImagesToAdd(paths, skippedNames, alreadyAddedNames);
    }

    // A folder that can't be listed gives nothing, so it is skipped by name like an empty one.
    private static List<string> FilesDirectlyIn(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }
}
