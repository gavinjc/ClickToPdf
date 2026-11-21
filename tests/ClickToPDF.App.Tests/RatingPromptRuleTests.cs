using ClickToPDF.App.ViewModels;

namespace ClickToPDF.App.Tests;

// Gavin's rule of 2026-10-31: an error holds the rating prompt back for the rest of the session,
// but skipping a file (unsupported or already added) isn't an error.
public class RatingPromptRuleTests
{
    // No dispatcher: these calls never touch the UI thread.
    private static MainViewModel NewViewModel() => new(dispatcherQueue: null!);

    [Fact]
    public void Error_HoldsBackThePrompt_EvenAfterALaterCleanAdd()
    {
        MainViewModel viewModel = NewViewModel();

        viewModel.ReportError("Disk full");
        viewModel.ReportSkippedFiles([], []);

        Assert.True(viewModel.HasErrorsThisSession);
    }

    // MarkLoadFailed is the one place LoadPageAsync turns a page into an error tile, from both its
    // decode and its read failure. LoadPageAsync itself needs a UI-thread DispatcherQueue the test
    // runner hasn't got, so that it calls MarkLoadFailed is checked by hand (an unreadable image in
    // the running app shows a "Can't load" tile and no rating prompt after saving).
    [Fact]
    public void ImageThatCouldNotBeOpened_HoldsBackThePrompt()
    {
        MainViewModel viewModel = NewViewModel();
        var page = new PageItem(Path.Combine(Path.GetTempPath(), "unreadable.jpg"));

        viewModel.MarkLoadFailed(page, detail: "Not an image", storeLinkUri: null);

        Assert.True(page.HasError);
        Assert.True(viewModel.HasErrorsThisSession);
    }

    [Fact]
    public void UnsupportedFileSkipped_DoesNotHoldBackThePrompt()
    {
        MainViewModel viewModel = NewViewModel();

        viewModel.ReportSkippedFiles(["notes.txt"], []);

        Assert.False(viewModel.HasErrorsThisSession);
    }

    [Fact]
    public void OnlyDuplicatesSkipped_DoesNotHoldBackThePrompt()
    {
        MainViewModel viewModel = NewViewModel();

        viewModel.ReportSkippedFiles([], ["photo.jpg"]);

        Assert.False(viewModel.HasErrorsThisSession);
    }
}
