using ClickToPDF.App.ViewModels;

namespace ClickToPDF.App.Tests;

public sealed class ImagesToAddTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ImagesToAddTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CreateFile(params string[] parts)
    {
        string path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    [Fact]
    public void ImageAlreadyLoaded_IsSkippedAsAlreadyAdded_NotAddedAgain()
    {
        string photo = CreateFile("photo.jpg");
        string other = CreateFile("other.png");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { photo, other }, loadedPaths: new[] { photo });

        Assert.Equal(new[] { other }, toAdd.Paths);
        Assert.Equal(new[] { "photo.jpg" }, toAdd.AlreadyAddedNames);
        Assert.Empty(toAdd.SkippedNames);
    }

    [Fact]
    public void AlreadyLoaded_MatchesFullPathIgnoringCase()
    {
        string photo = CreateFile("Photo.JPG");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { photo.ToLowerInvariant() }, loadedPaths: new[] { photo });

        Assert.Empty(toAdd.Paths);
        Assert.Single(toAdd.AlreadyAddedNames);
    }

    [Fact]
    public void SameNameInAnotherFolder_IsNotADuplicate()
    {
        string first = CreateFile("a", "photo.jpg");
        string second = CreateFile("b", "photo.jpg");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { second }, loadedPaths: new[] { first });

        Assert.Equal(new[] { second }, toAdd.Paths);
        Assert.Empty(toAdd.AlreadyAddedNames);
    }

    [Fact]
    public void FolderDroppedWithOneOfItsOwnImages_AddsThatImageOnce()
    {
        string photo = CreateFile("Holiday", "photo.jpg");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { Path.Combine(_root, "Holiday"), photo }, loadedPaths: []);

        Assert.Equal(new[] { photo }, toAdd.Paths);
        Assert.Equal(new[] { "photo.jpg" }, toAdd.AlreadyAddedNames);
    }

    [Fact]
    public void Folder_AddsItsImagesOneLevelDeep_SkipsItsOtherFiles_IgnoresSubfolders()
    {
        string jpg = CreateFile("Holiday", "beach.jpg");
        string png = CreateFile("Holiday", "map.PNG");
        CreateFile("Holiday", "notes.txt");
        CreateFile("Holiday", "Nested", "deep.jpg");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { Path.Combine(_root, "Holiday") }, loadedPaths: []);

        Assert.Equal(new[] { jpg, png }.Order(), toAdd.Paths.Order());
        Assert.Equal(new[] { "notes.txt" }, toAdd.SkippedNames);
        Assert.Empty(toAdd.AlreadyAddedNames);
    }

    [Fact]
    public void FolderWithNoImageDirectlyInside_IsSkippedByItsOwnName()
    {
        CreateFile("Documents", "notes.txt");
        CreateFile("Documents", "Nested", "deep.jpg");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { Path.Combine(_root, "Documents") + Path.DirectorySeparatorChar }, loadedPaths: []);

        Assert.Empty(toAdd.Paths);
        Assert.Equal(new[] { "Documents" }, toAdd.SkippedNames);
    }

    [Fact]
    public void UnsupportedFile_IsSkippedByName()
    {
        string notes = CreateFile("notes.txt");
        string photo = CreateFile("photo.heic");

        ImagesToAdd toAdd = ImagesToAdd.From(new[] { notes, photo }, loadedPaths: []);

        Assert.Equal(new[] { photo }, toAdd.Paths);
        Assert.Equal(new[] { "notes.txt" }, toAdd.SkippedNames);
    }
}
