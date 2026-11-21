using System.Runtime.InteropServices;
using Windows.Storage;

namespace ClickToPDF.App.State;

/// <summary>
/// Where the app keeps its settings and logs. Packaged, that is the MSIX package's own LocalState
/// folder, which Windows deletes on uninstall. A plain %LOCALAPPDATA% path is NOT redirected into
/// the package for a full-trust app (verified 2026-10-02: the installed package's settings.json
/// was in the real profile folder, and ClickTo: Slideshow found that folder left behind after an
/// uninstall), so it has to be asked for explicitly. Unpackaged runs (dotnet run, tests) have no
/// package identity and ApplicationData.Current throws there, hence the plain-folder fallback.
/// Same approach as ClickTo: Slideshow's AppDataFolders.
/// </summary>
public static class AppDataFolders
{
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);

    // Where 1.0.0 and 1.0.1 wrote settings and logs, packaged or not. Field order matters: static
    // initialisers run top to bottom, and LocalRoot reads both fields above it.
    private static readonly string LegacyRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickTo PDF");

    // Where the shell extension wrote its file lists before they moved to the package's TempState.
    // Keeps the old code name: it is the folder existing installs actually have.
    private static readonly string LegacyTempRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "Img2PDF");

    private static readonly bool IsPackaged = HasPackageIdentity();

    // No fallback to LegacyRoot if ApplicationData.Current throws: packaged, MoveLegacyDataIntoPackage
    // would then pass the same folder twice and delete the live settings on every launch.
    public static readonly string LocalRoot = IsPackaged ? ApplicationData.Current.LocalFolder.Path : LegacyRoot;

    /// <summary>
    /// For installs updated from 1.0.1 or earlier: carries the old settings into the package folder
    /// and deletes the old folder, logs included, and the shell extension's old temp folder, so that
    /// uninstalling leaves nothing behind.
    /// Unpackaged, both old folders are still the live ones, so this does nothing.
    /// </summary>
    public static void MoveLegacyDataIntoPackage()
    {
        if (IsPackaged)
        {
            MoveLegacyData(LegacyRoot, LocalRoot);
            DeleteLegacyTempFolder();
        }
    }

    // Safe to delete while Explorer is in use: the packaged shell extension now writes to
    // TempState, so anything here is an orphan from 1.0.1 or earlier. The one exception is a dev
    // machine that also runs the regsvr32-registered shell extension, which still writes here.
    private static void DeleteLegacyTempFolder()
    {
        if (Directory.Exists(LegacyTempRoot))
        {
            Directory.Delete(LegacyTempRoot, recursive: true);
        }
    }

    // Separate from the packaged check so the tests, which always run unpackaged, can reach it.
    // An existing settings file in the new folder wins: it is newer than anything left behind.
    public static void MoveLegacyData(string legacyRoot, string localRoot)
    {
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        string legacySettingsPath = Path.Combine(legacyRoot, AppSettings.FileName);
        string settingsPath = Path.Combine(localRoot, AppSettings.FileName);
        if (File.Exists(legacySettingsPath) && !File.Exists(settingsPath))
        {
            Directory.CreateDirectory(localRoot);
            File.Move(legacySettingsPath, settingsPath);
        }

        Directory.Delete(legacyRoot, recursive: true);
    }

    // A null buffer of length 0 makes the call report ERROR_INSUFFICIENT_BUFFER when a package
    // name exists and APPMODEL_ERROR_NO_PACKAGE when it doesn't, so nothing needs allocating.
    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, nint.Zero) != AppModelErrorNoPackage;
    }
}
