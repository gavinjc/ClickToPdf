namespace ClickToPDF.App.ViewModels;

// Kept out of MainViewModel so tests can reach it: MainViewModel's static ResourceLoader throws
// under the test runner.
public static class SupportedImages
{
    // Matches the shell extension's registered file types (spec §4.1) — broader than M1's
    // JPEG-only scope, since thumbnail decode via WIC works uniformly across all of these.
    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".tif", ".tiff", ".bmp", ".webp", ".gif" };

    public static IReadOnlyList<string> FileTypeFilters => SupportedExtensions;

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
