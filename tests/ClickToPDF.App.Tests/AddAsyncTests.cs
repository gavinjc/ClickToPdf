using ClickToPDF.App.ViewModels;

namespace ClickToPDF.App.Tests;

public class AddAsyncTests
{
    // A dropped item with no file-system path must still reach the skipped row by name, never
    // vanish silently. No paths means no page loads, so no dispatcher is needed.
    [Fact]
    public async Task DroppedItemWithNoPath_IsReportedAsSkippedByName()
    {
        var viewModel = new MainViewModel(dispatcherQueue: null!);
        IReadOnlyList<string>? reportedSkipped = null;
        viewModel.SkippedFilesReported += (skipped, _) => reportedSkipped = skipped;

        await viewModel.AddAsync([], ["IMG_0042.jpg"]);

        Assert.Equal(new[] { "IMG_0042.jpg" }, reportedSkipped);
        Assert.Empty(viewModel.Pages);
    }
}
