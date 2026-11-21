using ClickToPDF.App.State;
using ClickToPDF.App.ViewModels;

namespace ClickToPDF.App.Tests;

// Pages are added to the collection directly: AddAsync would load thumbnails, which needs a
// UI-thread DispatcherQueue. PageItem only stats its path, so the files needn't exist.
public class UndoAndSaveLockTests
{
    private static MainViewModel ViewModelWithPages(params string[] names)
    {
        var viewModel = new MainViewModel(dispatcherQueue: null!);
        foreach (string name in names)
        {
            viewModel.Pages.Add(new PageItem(Path.Combine(Path.GetTempPath(), name)));
        }
        return viewModel;
    }

    private static string[] Order(MainViewModel viewModel) => viewModel.Pages.Select(p => p.FileName).ToArray();

    // GridView's drag is a Remove then an Insert. The old tracking recorded only Move, so Undo
    // skipped the drag entirely.
    [Fact]
    public void Undo_AfterDragDoneAsRemoveAndInsert_RestoresOrderBeforeTheDrag()
    {
        MainViewModel viewModel = ViewModelWithPages("a.jpg", "b.jpg", "c.jpg");
        PageItem dragged = viewModel.Pages[0];

        viewModel.StartDragReorder();
        viewModel.Pages.Remove(dragged);
        viewModel.Pages.Insert(2, dragged);
        viewModel.CompleteDragReorder();

        Assert.Equal(new[] { "b.jpg", "c.jpg", "a.jpg" }, Order(viewModel));
        viewModel.Undo();
        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, Order(viewModel));
        Assert.Equal(new[] { 1, 2, 3 }, viewModel.Pages.Select(p => p.PageNumber));
    }

    // A drag dropped back where it started must not take up an undo step, or the next Undo
    // would appear to do nothing.
    [Fact]
    public void Undo_AfterDragThatEndsWhereItStarted_UndoesTheChangeBeforeIt()
    {
        MainViewModel viewModel = ViewModelWithPages("a.jpg", "b.jpg", "c.jpg");
        viewModel.ReverseOrder();

        viewModel.StartDragReorder();
        PageItem dragged = viewModel.Pages[1];
        viewModel.Pages.Remove(dragged);
        viewModel.Pages.Insert(1, dragged);
        viewModel.CompleteDragReorder();

        viewModel.Undo();
        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, Order(viewModel));
    }

    // The edits are attempted from the IsSaving change itself, which SaveAsync raises before it
    // starts composing. The page file doesn't exist, so the save then fails, and editing must
    // come back however the save ended.
    [Fact]
    public async Task EditsWhileSaving_AreIgnored_AndEditingReturnsWhenTheSaveFails()
    {
        MainViewModel viewModel = ViewModelWithPages("missing-a.jpg", "missing-b.jpg");
        viewModel.ReverseOrder();
        bool? canEditDuringSave = null;
        bool? canSaveDuringSave = null;
        bool addReachedSkippedReport = false;
        viewModel.SkippedFilesReported += (_, _) => addReachedSkippedReport = true;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSaving) && viewModel.IsSaving)
            {
                canEditDuringSave = viewModel.CanEdit;
                canSaveDuringSave = viewModel.CanSave;
                List<PageItem> all = viewModel.Pages.ToList();
                viewModel.RotateSelected(all, clockwise: true);
                viewModel.MoveSelected([all[0]], 1);
                viewModel.ApplySort(SortOrder.DateModified);
                viewModel.ReverseOrder();
                // A path-less name is reported as skipped before any page load, so an unguarded
                // AddAsync shows up here without needing a dispatcher.
                _ = viewModel.AddAsync([], ["dropped-during-save.jpg"]);
                viewModel.Undo();
                viewModel.RemoveSelected(all);
            }
        };
        string outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");

        await Assert.ThrowsAnyAsync<Exception>(() => viewModel.SaveAsync(outputPath, CancellationToken.None));

        Assert.False(canEditDuringSave);
        Assert.False(canSaveDuringSave);
        Assert.Equal(new[] { "missing-b.jpg", "missing-a.jpg" }, Order(viewModel));
        Assert.All(viewModel.Pages, p => Assert.Equal(0, p.RotationDegrees));
        Assert.Equal(SortOrder.NameNatural, viewModel.CurrentSortOrder);
        Assert.False(addReachedSkippedReport);
        Assert.False(viewModel.IsSaving);
        Assert.True(viewModel.CanEdit);

        viewModel.Undo();
        Assert.Equal(new[] { "missing-a.jpg", "missing-b.jpg" }, Order(viewModel));
    }
}
