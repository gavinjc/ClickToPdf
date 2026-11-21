using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using ClickToPDF.App.Diagnostics;
using ClickToPDF.App.State;
using ClickToPDF.Core.Imaging;
using ClickToPDF.Core.Pdf;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Storage.Streams;

namespace ClickToPDF.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxConcurrentThumbnailLoads = 4;

    // WIC's "no codec registered for this container" HRESULT. On a clean Windows install this is
    // what optional formats fail with until the matching codec pack is installed from the
    // Microsoft Store — HEIC/HEIF needs both "HEIF Image Extensions" and a separate HEVC decoder
    // (HEIC's pixel data is HEVC-coded; HEIF Image Extensions alone can't decode it), while WEBP
    // needs only its own codec pack. Worth an actionable message rather than a bare "Can't load"
    // (spec §8 item 7) — see CodecFixLinks for the extension→fix-link mapping.
    private const int WinCodecErrComponentNotFound = unchecked((int)0x88982F50);

    private static readonly ResourceLoader ResourceLoader = new();

    private readonly UndoStack<Action> _undoStack = new(capacity: 20);

    // Bumped on every user-visible change to the page set; a save records the value it
    // snapshotted, so edits made while that save was still running still count as unsaved.
    private int _changeVersion;
    private int _savedVersion;
    private string? _folderPath;
    private readonly SemaphoreSlim _thumbnailSemaphore = new(MaxConcurrentThumbnailLoads);
    private readonly DispatcherQueue _dispatcherQueue;

    // Set around every programmatic Pages mutation, so OnPagesCollectionChanged leaves it to the
    // caller to renumber. Left false only while GridView's own drag-reorder is mutating Pages
    // directly — that's the one case this VM didn't initiate.
    private bool _suppressCollectionTracking;

    // The order when a drag-reorder began; null when no drag is in progress.
    private List<PageItem>? _orderBeforeDrag;

    private bool _isSaving;

    public MainViewModel(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        Pages.CollectionChanged += OnPagesCollectionChanged;
    }

    public ObservableCollection<PageItem> Pages { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isLoading;

    // Saving mid-load would snapshot pages whose EXIF rotation hasn't been applied yet.
    public bool CanSave => !IsLoading && !IsSaving;

    // Private setter, so only SaveAsync can lock and unlock editing.
    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(CanEdit));
            }
        }
    }

    // While a save runs, nothing that changes the pages or options can be used (Gavin,
    // 2026-11-04). The save works from a snapshot, so this is about not letting the window show
    // something other than what is being written. Cancel stays usable.
    public bool CanEdit => !IsSaving;

    // Events rather than observable strings, so the same message raised twice shows again after
    // the user closed its row.
    public event Action<string>? ErrorRaised;

    // Raised on every add, with empty lists when nothing was skipped this time.
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>>? SkippedFilesReported;

    // Sticky for the session: an error (failed save, nothing to save, unexpected error) or an image
    // that couldn't be opened holds back the rating prompt even after the page is removed or a
    // later save works. Skipping an unsupported file or an "already added" one isn't an error and
    // doesn't count. (Decided by Gavin 2026-10-31, reversing the family rule of 2026-10-29.)
    public bool HasErrorsThisSession { get; private set; }

    // Empty state (spec §4.2 Startup) — true only until either constructor is given something to
    // load. Set false immediately (before any async load starts) rather than waiting for Pages to
    // gain items, so there's no flash of empty-state ahead of the "Loading…" tile placeholders.
    [ObservableProperty]
    private bool _showEmptyState = true;

    // Save options (spec §4.2) — defaults produce a clean, correctly-oriented A4 document with
    // zero interaction. Bound from MainWindow's options expander.
    [ObservableProperty]
    private PageSizeOption _pageSize = PageSizeOption.A4;

    [ObservableProperty]
    private MarginsOption _margins = MarginsOption.None;

    [ObservableProperty]
    private OrientationOption _orientation = OrientationOption.Auto;

    [ObservableProperty]
    private QualityOption _quality = QualityOption.Original;

    [ObservableProperty]
    private bool _greyscale;

    [ObservableProperty]
    private bool _searchable;

    [ObservableProperty]
    private bool _deskew;

    // Spec §4.2 "Remember the last-used choice" — set once at startup from AppSettings by
    // MainWindow before any load call, and re-applied after the initial (always-natural) load
    // completes. See LoadPagesAsync.
    [ObservableProperty]
    private SortOrder _currentSortOrder = SortOrder.NameNatural;

    // Observable so MainWindow can show the folder in its title, which is how two open windows
    // are told apart in the taskbar and Alt+Tab.
    public string? FolderPath
    {
        get => _folderPath;
        private set => SetProperty(ref _folderPath, value);
    }

    public bool HasUnsavedChanges => Pages.Count > 0 && _changeVersion != _savedVersion;

    public PdfOptions CurrentOptions => new(PageSize, Margins, Orientation, Quality, Greyscale, Searchable, Deskew);

    public async Task LoadFolderAsync(string folderPath)
    {
        FolderPath = folderPath;

        // Directory.EnumerateFiles validates the path eagerly (throws synchronously at the call
        // site, not lazily during enumeration) — catch it here rather than letting it escape the
        // fire-and-forget call in MainWindow's constructor, which previously crashed the app
        // natively (0xc000027b) instead of showing an error like every other load failure.
        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(folderPath).Where(SupportedImages.IsSupported).ToList();
        }
        catch (Exception ex)
        {
            AppLog.LogError("LoadFolderAsync", ex);
            ReportError(ex.Message);
            return;
        }

        await LoadPagesAsync(paths);
    }

    // Explicit file list from the shell extension's temp-file handshake (spec §4.1). filePaths is
    // already all-supported-type by construction; skippedNames are the rest of the original
    // selection (unsupported files, or a directly-selected folder) that GetState now lets through
    // rather than hiding the whole menu entry for.
    public Task LoadFilesAsync(IReadOnlyList<string> filePaths, IReadOnlyList<string> skippedNames)
    {
        FolderPath = filePaths.Count > 0 ? Path.GetDirectoryName(filePaths[0]) : null;
        ReportSkippedFiles(skippedNames, Array.Empty<string>());
        return LoadPagesAsync(filePaths);
    }

    // Picked or dropped files and folders. Onto the empty state this is the first load; onto a
    // loaded set it appends, never replaces (Bureau.Common app-conventions.md, Add button).
    // namesWithoutPath are dropped items with no file-system path; they are only reported as skipped.
    public Task AddAsync(IReadOnlyList<string> paths, IReadOnlyList<string> namesWithoutPath)
    {
        if (!CanEdit)
        {
            return Task.CompletedTask;
        }

        ImagesToAdd toAdd = ImagesToAdd.From(paths, Pages.Select(p => p.SourcePath));
        ReportSkippedFiles(namesWithoutPath.Concat(toAdd.SkippedNames).ToList(), toAdd.AlreadyAddedNames);
        if (Pages.Count > 0)
        {
            return AppendPagesAsync(toAdd.Paths);
        }

        FolderPath = toAdd.Paths.Count > 0 ? Path.GetDirectoryName(toAdd.Paths[0]) : null;
        return LoadPagesAsync(toAdd.Paths);
    }

    // Adds more images to an already-loaded set. Deliberately separate from LoadPagesAsync, which
    // always clears the undo stack and re-loads every page's thumbnail (right for a fresh load,
    // wrong here — appending should be one undoable step on top of whatever the user has already
    // done, and only the newly-added items need their thumbnails decoded).
    private async Task AppendPagesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            List<PageItem> previousOrder = Pages.ToList();
            List<PageItem> newItems = filePaths
                .OrderBy(p => p, NaturalSortComparer.Instance)
                .Select(p => new PageItem(p))
                .ToList();

            _suppressCollectionTracking = true;
            foreach (PageItem item in newItems)
            {
                Pages.Add(item);
            }
            _suppressCollectionTracking = false;
            RenumberPages();

            _undoStack.Push(() => ReplacePages(previousOrder));

            await Task.WhenAll(newItems.Select(LoadPageAsync));

            // Re-sort the whole set (existing + new) per the active sort choice, so appended
            // images land in the right position rather than always tacking on at the end. Only
            // after loading — before it, the new items have no DateTaken to sort by. Not its own
            // undo entry: undoing the append above already restores the pre-append order.
            if (CurrentSortOrder != SortOrder.NameNatural)
            {
                ReplacePages(ComputeSortedOrder(CurrentSortOrder));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ReportSkippedFiles(IReadOnlyList<string> skippedNames, IReadOnlyList<string> alreadyAddedNames) =>
        SkippedFilesReported?.Invoke(skippedNames, alreadyAddedNames);

    public void ReportError(string message)
    {
        HasErrorsThisSession = true;
        ErrorRaised?.Invoke(message);
    }

    private async Task LoadPagesAsync(IEnumerable<string> paths)
    {
        IsLoading = true;
        try
        {
            List<string> ordered = paths.OrderBy(p => p, NaturalSortComparer.Instance).ToList();

            _suppressCollectionTracking = true;
            foreach (string path in ordered)
            {
                Pages.Add(new PageItem(path));
            }
            _suppressCollectionTracking = false;
            RenumberPages();
            _undoStack.Clear();

            await Task.WhenAll(Pages.Select(LoadPageAsync));

            // Initial order is always natural sort per spec §4.2; re-apply a remembered non-default
            // choice now that per-page metadata (dates) exists. Skipped for NameNatural to avoid a
            // redundant no-op resort (and an unnecessary undo-stack entry) on every load.
            if (CurrentSortOrder != SortOrder.NameNatural)
            {
                ApplySort(CurrentSortOrder);
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("LoadPagesAsync", ex);
            ReportError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplySort(SortOrder order)
    {
        if (!CanEdit)
        {
            return;
        }

        CurrentSortOrder = order;
        ApplyOrder(ComputeSortedOrder(order));
    }

    private List<PageItem> ComputeSortedOrder(SortOrder order)
    {
        IOrderedEnumerable<PageItem> ordered = order switch
        {
            SortOrder.DateTaken => Pages.OrderBy(p => p.DateTaken ?? p.FileModifiedUtc),
            SortOrder.DateModified => Pages.OrderBy(p => p.FileModifiedUtc),
            _ => Pages.OrderBy(p => p.FileName, NaturalSortComparer.Instance),
        };
        return ordered.ToList();
    }

    public void ReverseOrder()
    {
        if (!CanEdit)
        {
            return;
        }

        ApplyOrder(Pages.Reverse().ToList());
    }

    // Shared by ApplySort/ReverseOrder: the whole reorder is one undo step, not one per moved item.
    private void ApplyOrder(List<PageItem> newOrder)
    {
        List<PageItem> previous = Pages.ToList();
        ReplacePages(newOrder);
        _undoStack.Push(() => ReplacePages(previous));
    }

    // Clear and re-add under _suppressCollectionTracking, so OnPagesCollectionChanged doesn't
    // renumber once per item.
    private void ReplacePages(IReadOnlyList<PageItem> order)
    {
        _suppressCollectionTracking = true;
        Pages.Clear();
        foreach (PageItem item in order)
        {
            Pages.Add(item);
        }
        _suppressCollectionTracking = false;
        RenumberPages();
    }

    // GridView's drag-reorder doesn't raise a single Move: it removes the item and inserts it
    // again, so the old Move-only tracking never recorded a drag at all. Comparing the order before
    // and after the whole drag makes one drag one undo step, however GridView carries it out.
    public void StartDragReorder() => _orderBeforeDrag = Pages.ToList();

    public void CompleteDragReorder()
    {
        if (_orderBeforeDrag is null)
        {
            return;
        }

        List<PageItem> previous = _orderBeforeDrag;
        _orderBeforeDrag = null;
        if (!previous.SequenceEqual(Pages))
        {
            _undoStack.Push(() => ReplacePages(previous));
        }
    }

    public void RotateSelected(IReadOnlyList<PageItem> selected, bool clockwise)
    {
        if (selected.Count == 0 || !CanEdit)
        {
            return;
        }

        List<PageItem> rotated = selected.ToList();
        _changeVersion++;
        foreach (PageItem item in rotated)
        {
            if (clockwise)
            {
                item.RotateClockwise();
            }
            else
            {
                item.RotateCounterClockwise();
            }
        }

        // Undo rotates back by the same step rather than restoring a remembered absolute value —
        // a still-loading tile's EXIF rotation is added after this runs (PageItem.ApplyImageInfo),
        // and restoring the pre-rotate value would wipe that out.
        _undoStack.Push(() =>
        {
            _changeVersion++;
            foreach (PageItem item in rotated)
            {
                if (clockwise)
                {
                    item.RotateCounterClockwise();
                }
                else
                {
                    item.RotateClockwise();
                }
            }
        });
    }

    public void RemoveSelected(IReadOnlyList<PageItem> selected)
    {
        if (!CanEdit)
        {
            return;
        }

        List<(PageItem Item, int Index)> removed = selected
            .Select(item => (Item: item, Index: Pages.IndexOf(item)))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        _suppressCollectionTracking = true;
        foreach ((PageItem item, _) in removed)
        {
            Pages.Remove(item);
        }
        _suppressCollectionTracking = false;
        RenumberPages();

        _undoStack.Push(() =>
        {
            _suppressCollectionTracking = true;
            foreach ((PageItem item, int index) in removed)
            {
                Pages.Insert(Math.Min(index, Pages.Count), item);
            }
            _suppressCollectionTracking = false;
            RenumberPages();
        });
    }

    // Ctrl+Left/Right: shifts the whole selection by one slot. If any selected tile is already at
    // the boundary in that direction, the whole move is a no-op rather than partially shifting.
    public void MoveSelected(IReadOnlyList<PageItem> selected, int delta)
    {
        if (delta is not (-1 or 1) || !CanEdit)
        {
            return;
        }

        List<int> indices = selected
            .Select(item => Pages.IndexOf(item))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();

        if (indices.Count == 0)
        {
            return;
        }

        if (delta > 0)
        {
            indices.Reverse();
        }

        foreach (int index in indices)
        {
            int target = index + delta;
            if (target < 0 || target >= Pages.Count)
            {
                return;
            }
        }

        var moves = new List<(int From, int To)>();
        _suppressCollectionTracking = true;
        foreach (int index in indices)
        {
            int target = index + delta;
            Pages.Move(index, target);
            moves.Add((index, target));
        }
        _suppressCollectionTracking = false;
        RenumberPages();

        _undoStack.Push(() =>
        {
            _suppressCollectionTracking = true;
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                Pages.Move(moves[i].To, moves[i].From);
            }
            _suppressCollectionTracking = false;
            RenumberPages();
        });
    }

    public void Undo()
    {
        if (CanEdit && _undoStack.TryPop(out Action? action))
        {
            action?.Invoke();
        }
    }

    // Error tiles are left out: their source can't be decoded, so composing one would fail the
    // whole save. MainWindow reports them as skipped.
    public IReadOnlyList<PageItem> SavablePages() => Pages.Where(p => !p.HasError).ToList();

    // Snapshots the pages on the calling (UI) thread, then composes on the thread pool.
    public async Task SaveAsync(string outputPath, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        var pageSources = SavablePages().Select(p => new PdfPageSource(p.SourcePath, p.RotationDegrees)).ToList();
        PdfOptions options = CurrentOptions;
        int snapshotVersion = _changeVersion;
        IsSaving = true;
        try
        {
            await Task.Run(() => ComposeViaTempFileAsync(pageSources, outputPath, options, progress, cancellationToken));
        }
        finally
        {
            IsSaving = false;
        }
        _savedVersion = snapshotVersion;
    }

    // Composes into a sibling temp file and only replaces outputPath once the PDF is complete, so a
    // cancelled or failed save never touches a file the user chose to overwrite.
    private static async Task ComposeViaTempFileAsync(
        IReadOnlyList<PdfPageSource> pageSources,
        string outputPath,
        PdfOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        string tempPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await PdfComposer.ComposeAsync(pageSources, tempPath, options, progress, cancellationToken);
            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void OnPagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // GridView's own drag-reorder mutated Pages directly. Its undo step is recorded once the
        // drag completes (CompleteDragReorder).
        if (!_suppressCollectionTracking)
        {
            RenumberPages();
        }
    }

    // The one place called after every single Pages mutation (load, remove, sort, reorder, and
    // each operation's undo) — the single choke point for keeping ShowEmptyState correct, rather
    // than re-deriving it ad hoc at each call site and risking a path that forgets to (which is
    // exactly how removing every page previously left the empty state hidden with nothing to
    // recover with). Also the choke point for HasUnsavedChanges, for the same reason.
    private void RenumberPages()
    {
        ShowEmptyState = Pages.Count == 0;
        _changeVersion++;

        for (int i = 0; i < Pages.Count; i++)
        {
            Pages[i].PageNumber = i + 1;
        }
    }

    private async Task LoadPageAsync(PageItem item)
    {
        await _thumbnailSemaphore.WaitAsync();
        try
        {
            ImageInfo info = await ImageInspector.InspectAsync(item.SourcePath);

            // Applied here, on the UI thread, before any thumbnail work — so this task completing
            // means DateTaken and the EXIF rotation are in place. Callers sort by DateTaken right
            // after awaiting this, and Save reads RotationDegrees; previously both could run before
            // the (later, queued) bitmap decode had got round to applying it.
            item.ApplyImageInfo(info);

            byte[] thumbnailBytes = await ImageInspector.GetThumbnailAsync(item.SourcePath);

            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(thumbnailBytes.AsBuffer());
                    stream.Seek(0);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);

                    item.ThumbnailSource = bitmap;
                    item.IsLoading = false;
                }
                catch (Exception)
                {
                    MarkLoadFailed(item, detail: null, storeLinkUri: null);
                }
            });
        }
        catch (Exception ex)
        {
            // Per-tile error state, not a crash (spec acceptance criteria) — corrupt/unreadable
            // source files shouldn't take down the whole load. Warning, not error: an individual
            // bad file is expected user-data noise, not an app defect worth treating the same as
            // an unhandled exception.
            AppLog.LogWarning("LoadPageAsync", $"{item.FileName}: {ex}");
            (string detail, Uri? storeLinkUri) = DescribeLoadError(ex, item.SourcePath);
            _dispatcherQueue.TryEnqueue(() => MarkLoadFailed(item, detail, storeLinkUri));
        }
        finally
        {
            _thumbnailSemaphore.Release();
        }
    }

    // The one place a page becomes an error tile, so it can't forget the rating rule: an image
    // that couldn't be opened holds the prompt back for the session. Internal for the tests, which
    // can't run LoadPageAsync itself: it needs a UI-thread DispatcherQueue.
    internal void MarkLoadFailed(PageItem item, string? detail, Uri? storeLinkUri)
    {
        item.HasError = true;
        item.ErrorDetail = detail;
        item.ErrorStoreLinkUri = storeLinkUri;
        item.IsLoading = false;
        HasErrorsThisSession = true;
    }

    private static (string Message, Uri? StoreLinkUri) DescribeLoadError(Exception ex, string sourcePath)
    {
        if (ex.HResult == WinCodecErrComponentNotFound &&
            CodecFixLinks.GetFixLink(Path.GetExtension(sourcePath)) is (string messageKey, Uri storeLinkUri))
        {
            return (ResourceLoader.GetString(messageKey), storeLinkUri);
        }

        return (ex.Message, null);
    }
}
