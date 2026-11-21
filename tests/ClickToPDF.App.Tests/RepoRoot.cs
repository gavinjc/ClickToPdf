namespace ClickToPDF.App.Tests;

// For tests that read the app's source files as text (array literals, .resw strings).
internal static class RepoRoot
{
    public static string Find()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClickToPDF.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
