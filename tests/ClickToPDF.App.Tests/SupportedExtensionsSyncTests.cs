using System.Text.RegularExpressions;

namespace ClickToPDF.App.Tests;

// SupportedExtensions.h (ClickToPDF.ShellExtension) and SupportedImages.SupportedExtensions are
// independently maintained across the C++/C# boundary — nothing at build time enforces that
// they list the same extensions, only a comment on the C++ side. This test reads both array
// literals as plain text so drift between the two fails a build instead of silently mismatching
// which files the shell extension shows the command for versus which files the app will load.
public class SupportedExtensionsSyncTests
{
    [Fact]
    public void ShellExtensionAndAppListTheSameExtensions()
    {
        string repoRoot = RepoRoot.Find();

        string[] cppExtensions = ExtractQuotedItems(
            Path.Combine(repoRoot, "src", "ClickToPDF.ShellExtension", "SupportedExtensions.h"),
            "SupportedExtensions",
            "L\"([^\"]+)\"");

        string[] appExtensions = ExtractQuotedItems(
            Path.Combine(repoRoot, "src", "ClickToPDF.App", "ViewModels", "SupportedImages.cs"),
            "SupportedExtensions",
            "\"([^\"]+)\"");

        Assert.NotEmpty(cppExtensions);
        Assert.NotEmpty(appExtensions);

        Assert.Equal(
            cppExtensions.OrderBy(x => x, StringComparer.Ordinal),
            appExtensions.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string[] ExtractQuotedItems(string path, string arrayName, string quotedItemPattern)
    {
        string source = File.ReadAllText(path);

        Match arrayLiteral = Regex.Match(source, $@"{arrayName}\s*=\s*\{{(?<items>[^}}]*)\}}");
        Assert.True(arrayLiteral.Success, $"Could not find the {arrayName} array literal in {path}");

        return Regex.Matches(arrayLiteral.Groups["items"].Value, quotedItemPattern)
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }
}
