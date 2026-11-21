using ClickToPDF.App.State;

namespace ClickToPDF.App.Tests;

// xunit runs unpackaged, so LocalRoot can only be checked on its fallback branch; the packaged
// branch (LocalState) was checked by hand against the installed MSIX. If package detection
// inverted, ApplicationData.Current would throw in the static initialiser and every test here
// would fail.
public class AppDataFoldersTests
{
    private static string NewTempFolder() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void LocalRoot_Unpackaged_IsClickToPdfFolderUnderLocalAppData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickTo PDF");

        Assert.Equal(expected, AppDataFolders.LocalRoot);
    }

    [Fact]
    public void MoveLegacyData_CarriesSettingsOverAndDeletesOldFolderWithLogs()
    {
        string legacyRoot = NewTempFolder();
        string localRoot = NewTempFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(legacyRoot, "logs"));
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "old");
            File.WriteAllText(Path.Combine(legacyRoot, "logs", "2026-09-30.log"), "log");

            AppDataFolders.MoveLegacyData(legacyRoot, localRoot);

            Assert.Equal("old", File.ReadAllText(Path.Combine(localRoot, "settings.json")));
            Assert.False(Directory.Exists(legacyRoot));
        }
        finally
        {
            Directory.Delete(localRoot, recursive: true);
        }
    }

    [Fact]
    public void MoveLegacyData_ExistingSettingsInNewFolder_AreNotOverwritten()
    {
        string legacyRoot = NewTempFolder();
        string localRoot = NewTempFolder();
        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(localRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "old");
            File.WriteAllText(Path.Combine(localRoot, "settings.json"), "new");

            AppDataFolders.MoveLegacyData(legacyRoot, localRoot);

            Assert.Equal("new", File.ReadAllText(Path.Combine(localRoot, "settings.json")));
            Assert.False(Directory.Exists(legacyRoot));
        }
        finally
        {
            Directory.Delete(localRoot, recursive: true);
        }
    }

    [Fact]
    public void MoveLegacyData_NoOldFolder_DoesNothing()
    {
        string legacyRoot = NewTempFolder();
        string localRoot = NewTempFolder();

        AppDataFolders.MoveLegacyData(legacyRoot, localRoot);

        Assert.False(Directory.Exists(localRoot));
    }
}
