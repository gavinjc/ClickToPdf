using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Bureau.Common;
using Bureau.Common.Help;
using Bureau.Common.Messages;
using ClickToPDF.App.Diagnostics;
using ClickToPDF.App.State;
using ClickToPDF.App.ViewModels;
using ClickToPDF.Core.Imaging;
using ClickToPDF.Core.Pdf;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace ClickToPDF.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private static readonly ResourceLoader ResourceLoader = new();

    // Message bar keys: one per kind of message, so a second one replaces the first.
    private const string SkippedMessageKey = "skipped";
    private const string SavedMessageKey = "saved";
    private const string ErrorMessageKey = "error";

    private CancellationTokenSource? _saveCts;
    private PageItem? _previewItem;
    private bool _closeConfirmed;

    public MainWindow(string? folderPath)
    {
        ViewModel = InitializeCommon();

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            _ = ViewModel.LoadFolderAsync(folderPath);
        }
    }

    // Shell extension launch path (spec §4.1 --list handshake) — an explicit file selection
    // rather than a whole folder. skippedNames are files the shell extension's GetState/Invoke
    // let through the selection but excluded as unsupported (or a folder) — surfaced in the
    // message bar's skipped row rather than silently dropped.
    public MainWindow(IReadOnlyList<string> filePaths, IReadOnlyList<string> skippedNames)
    {
        ViewModel = InitializeCommon();

        if (filePaths.Count > 0)
        {
            _ = ViewModel.LoadFilesAsync(filePaths, skippedNames);
        }
    }

    private MainViewModel InitializeCommon()
    {
        InitializeComponent();
        Title = ResourceLoader.GetString("WindowTitle");
        AboutDialog.ProductName = ResourceLoader.GetString("WindowTitle");
        AboutDialog.Description = ResourceLoader.GetString("AppDescriptionText");
        AboutDialog.LogDirectoryPath = AppLog.LogDirectoryPath;
        var viewModel = new MainViewModel(DispatcherQueue);
        RootGrid.DataContext = viewModel;
        viewModel.ErrorRaised += message => Messages.Show(ErrorMessageKey, MessageSeverity.Error, message);
        viewModel.SkippedFilesReported += ShowSkippedMessage;

        // handledEventsToo: true — GridViewItem marks Enter as handled internally for its own
        // selection-toggle behavior before a normal bubbled KeyDown subscription would ever see it
        // (a KeyboardAccelerator on the Save button didn't intercept it either). This is the only
        // reliable way to still receive Enter as a window-wide "Save" shortcut (spec §4.2) regardless
        // of what has focus. Ctrl+Z is here too: in PagesGridView_KeyDown it only worked with focus
        // on a page, so it did nothing just after clicking Undo, Sort or an option.
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), handledEventsToo: true);

        // On RootGrid, so F1 also reaches Help while the preview overlay (a child of it) is open.
        HelpKey.Attach(RootGrid, ShowHelp);

        // Package.appxmanifest's Square44x44Logo only covers the Start tile/pinned-shortcut
        // icon and (when packaged) the taskbar — the running window's own titlebar icon needs
        // this explicit call regardless of packaging state, or it falls back to WinUI's
        // generic default icon.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Closing += AppWindow_Closing;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.FolderPath))
            {
                Title = FormatWindowTitle(viewModel.FolderPath);
            }
        };
        appWindow.SetIcon("Assets\\ClickToPdf.ico");

        TitleBarTheme.Follow(this, RootGrid);

        AppSettingsData settings = AppSettings.Load();
        ZoomSlider.Value = settings.ZoomValue;
        viewModel.CurrentSortOrder = settings.LastSortOrder;

        // Save-on-close rather than save-on-every-drag-tick: a continuous Slider.ValueChanged
        // during a drag would otherwise hammer disk I/O for no benefit — the setting only needs
        // to survive across sessions, not live-sync mid-drag.
        Closed += (_, _) => AppSettings.Save(new AppSettingsData(ZoomSlider.Value, viewModel.CurrentSortOrder));

        return viewModel;
    }

    // Each add replaces the previous add's skipped row, and a clean add closes it.
    private void ShowSkippedMessage(IReadOnlyList<string> skippedNames, IReadOnlyList<string> alreadyAddedNames)
    {
        string? message = SkippedMessage.Compose(skippedNames, alreadyAddedNames, ResourceLoader.GetString);
        if (message is null)
        {
            Messages.Close(SkippedMessageKey);
        }
        else
        {
            Messages.Show(SkippedMessageKey, MessageSeverity.Warning, message);
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _ = SaveThenOfferRatingAsync();
        }
        else if (e.Key == VirtualKey.Z && IsCtrlDown() && e.OriginalSource is not TextBox)
        {
            ViewModel.Undo();
            e.Handled = true;
        }
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

    private void PagesGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e) =>
        ViewModel.StartDragReorder();

    private void PagesGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        ViewModel.CompleteDragReorder();

    // Source of truth stays GridView.SelectedItems; this just mirrors it onto each PageItem so
    // the DataTemplate can draw its own selection highlight (see MainWindow.xaml's ChromePill
    // comment for why the container-level template can't do this itself).
    private void PagesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (PageItem item in e.RemovedItems.OfType<PageItem>())
        {
            item.IsSelected = false;
        }

        foreach (PageItem item in e.AddedItems.OfType<PageItem>())
        {
            item.IsSelected = true;
        }
    }

    // Handled on the tile's root Grid rather than per-button: PointerEntered/Exited only fire at
    // the boundary of the element they're attached to, so attaching them here means moving the
    // pointer between the tile and its own buttons never fires Exited — the flicker the old
    // per-button Visibility toggle had (see IconButtonStyle's comment in the XAML).
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).FindName("ChromePill") is Border pill)
        {
            pill.Opacity = 1.0;
        }
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).FindName("ChromePill") is Border pill)
        {
            pill.Opacity = 0.55;
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => ViewModel.Undo();

    private bool _optionsExpanded;

    private void OptionsHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        _optionsExpanded = !_optionsExpanded;
        OptionsBody.Visibility = _optionsExpanded ? Visibility.Visible : Visibility.Collapsed;
        OptionsHeaderChevron.Glyph = _optionsExpanded ? "\uE70E" : "\uE70D";
    }

    private void SortButton_Click(SplitButton sender, SplitButtonClickEventArgs args) =>
        ApplySortAndPersist(ViewModel.CurrentSortOrder);

    private void SortByName_Click(object sender, RoutedEventArgs e) => ApplySortAndPersist(SortOrder.NameNatural);

    private void SortByDateTaken_Click(object sender, RoutedEventArgs e) => ApplySortAndPersist(SortOrder.DateTaken);

    private void SortByDateModified_Click(object sender, RoutedEventArgs e) => ApplySortAndPersist(SortOrder.DateModified);

    private void ReverseOrder_Click(object sender, RoutedEventArgs e) => ViewModel.ReverseOrder();

    private void ApplySortAndPersist(SortOrder order)
    {
        ViewModel.ApplySort(order);
        AppSettings.Save(new AppSettingsData(ZoomSlider.Value, order));
    }

    private void RotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            ViewModel.RotateSelected(new[] { item }, clockwise: true);
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            ViewModel.RemoveSelected(new[] { item });
        }
    }

    private async void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // DoubleTapped is routed and bubbles from the chrome buttons up through this tile's
        // root Grid — a Button's Click handler doesn't mark it handled, so two quick clicks
        // on Preview/Rotate/Remove also opened the preview underneath. Ignore double-taps
        // that originated inside a button rather than on the tile/image itself.
        if (e.OriginalSource is DependencyObject originalSource && HasButtonAncestor(originalSource))
        {
            return;
        }

        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            await ShowPreviewAsync(item);
        }
    }

    private static bool HasButtonAncestor(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            await ShowPreviewAsync(item);
        }
    }

    private async Task ShowPreviewAsync(PageItem item)
    {
        try
        {
            byte[] bytes = await ImageInspector.GetThumbnailAsync(item.SourcePath, requestedLongEdge: 1600);

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            _previewItem = item;
            PreviewImage.Source = bitmap;
            PreviewImageRotation.Angle = item.RotationDegrees;
            PreviewOverlay.Visibility = Visibility.Visible;
            UpdatePreviewIndicator();
        }
        catch (Exception)
        {
            // Preview is best-effort — leave the overlay closed on failure rather than crash.
        }
    }

    // Left/Right while the overlay is open (wired from PagesGridView_KeyDown) — clamps at the
    // ends rather than wrapping, which is the less surprising choice for stepping through a
    // finite page order.
    private async Task StepPreviewAsync(int delta)
    {
        if (_previewItem is null)
        {
            return;
        }

        int index = ViewModel.Pages.IndexOf(_previewItem);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= ViewModel.Pages.Count)
        {
            return;
        }

        await ShowPreviewAsync(ViewModel.Pages[target]);
    }

    private void UpdatePreviewIndicator()
    {
        if (_previewItem is null)
        {
            return;
        }

        int index = ViewModel.Pages.IndexOf(_previewItem);
        PreviewIndicatorText.Text = string.Format(
            ResourceLoader.GetString("PreviewIndicatorFormat"), index + 1, ViewModel.Pages.Count);
    }

    private void PreviewOverlay_Tapped(object sender, TappedRoutedEventArgs e) => ClosePreview();

    private void PreviewCloseButton_Click(object sender, RoutedEventArgs e) => ClosePreview();

    private void PreviewRotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewItem is not null)
        {
            ViewModel.RotateSelected(new[] { _previewItem }, clockwise: true);
            PreviewImageRotation.Angle = _previewItem.RotationDegrees;
        }
    }

    // The overlay's own Background is Tapped-enabled to close on click-outside — since Tapped
    // bubbles, a control placed inside the overlay (close button, rotate button) needs to stop
    // that bubble or every click on them would also close the overlay it just acted on.
    private void StopTappedPropagation(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void ClosePreview()
    {
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        _previewItem = null;
    }

    // ComboBox's SelectedIndex="0" in XAML fires SelectionChanged synchronously during
    // InitializeComponent() — before the constructor below has assigned ViewModel — so these
    // guard against a null ViewModel rather than relying on ordering. (A NullReferenceException
    // thrown from inside that synchronous callback doesn't surface as a normal managed exception;
    // it escapes through the WinRT/XAML boundary as an unhandled native crash, 0xc000027b.)
    //
    // Read via each ComboBoxItem's Tag rather than SelectedIndex — the Tag names the enum member
    // directly, so reordering or inserting a ComboBoxItem in XAML can't silently desync from the
    // enum the way a positional index cast could.
    private static T SelectedOption<T>(ComboBox combo) where T : struct, Enum =>
        Enum.Parse<T>((string)((ComboBoxItem)combo.SelectedItem).Tag);

    private void PageSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PageSize = SelectedOption<PageSizeOption>(PageSizeCombo);
        }
    }

    private void MarginsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Margins = SelectedOption<MarginsOption>(MarginsCombo);
        }
    }

    private void OrientationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Orientation = SelectedOption<OrientationOption>(OrientationCombo);
        }
    }

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Quality = SelectedOption<QualityOption>(QualityCombo);
        }
    }

    private async void ChooseImagesButton_Click(object sender, RoutedEventArgs e) => await PickImagesAsync();

    // Floating "+" — always visible, not just on the empty state: lets images be added to a set
    // the shell extension already launched with, not just to a from-scratch session.
    private async void AddMoreButton_Click(object sender, RoutedEventArgs e) => await PickImagesAsync();

    private async Task PickImagesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        foreach (string extension in SupportedImages.FileTypeFilters)
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        AddImages(files.Select(f => f.Path).ToList(), Array.Empty<string>());
    }

    // An add supersedes an earlier error (a failed folder launch, nothing to save), the way a fresh
    // load cleared it before 1.0.3; a save that worked closes it too (ShowSavedMessage).
    private void AddImages(IReadOnlyList<string> paths, IReadOnlyList<string> namesWithoutPath)
    {
        Messages.Close(ErrorMessageKey);
        _ = ViewModel.AddAsync(paths, namesWithoutPath);
    }

    // Spec §4.2 Startup — "accept drag-and-drop of files onto the window" — extended to also
    // accept drops onto an already-loaded set, and folders (ViewModel.AddAsync).
    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) && ViewModel.CanEdit)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0)
        {
            // Path is empty for an item with no file-system path (a phone over MTP, say). There is
            // nothing on disk to read, so it goes to the skipped row by name.
            AddImages(
                items.Where(item => item.Path.Length > 0).Select(item => item.Path).ToList(),
                items.Where(item => item.Path.Length == 0).Select(item => item.Name).ToList());
        }
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        AboutDialog.XamlRoot = RootGrid.XamlRoot;
        await AboutDialog.ShowAsync();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowHelp();

    private void ShowHelp() => HelpWindow.Show(
        this,
        ResourceLoader.GetString("WindowTitle"),
        HelpTopics.Load(ResourceLoader.GetString, HelpTopicKeys.InOrder));

    // Folder first, so it survives the taskbar truncating a long title.
    private static string FormatWindowTitle(string? folderPath)
    {
        string appTitle = ResourceLoader.GetString("WindowTitle");
        if (folderPath is null)
        {
            return appTitle;
        }

        // GetFileName returns "" for a drive root such as D:\, so fall back to the path itself.
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        return string.Format(
            ResourceLoader.GetString("WindowTitleWithFolderFormat"),
            folderName.Length > 0 ? folderName : folderPath,
            appTitle);
    }

    private async void AppWindow_Closing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !ViewModel.HasUnsavedChanges)
        {
            return;
        }

        args.Cancel = true;

        // Only one ContentDialog can be open per window — ShowAsync throws if About is still up.
        AboutDialog.Hide();
        CloseConfirmDialog.XamlRoot = RootGrid.XamlRoot;
        ContentDialogResult result = await CloseConfirmDialog.ShowAsync();

        bool shouldClose = result switch
        {
            ContentDialogResult.Primary => await SaveAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };

        if (shouldClose)
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveThenOfferRatingAsync();

    // Only a save the user asked for directly, never the save-then-close path, where a Store dialog
    // would hold the closing window open. And only in a session with no error: a page that failed
    // to decode (a missing codec included) or any error means the app let the user down, and the
    // Store prompt must never reach them then (Bureau.Common RatingPrompt). A skipped unsupported
    // or "already added" file isn't an error and doesn't count.
    private async Task SaveThenOfferRatingAsync()
    {
        bool hadErrors = ViewModel.HasErrorsThisSession;
        if (await SaveAsync() && !hadErrors)
        {
            await RatingPrompt.RequestOnceAsync(this);
        }
    }

    private void CancelSaveButton_Click(object sender, RoutedEventArgs e) => _saveCts?.Cancel();

    // Returns true only when a PDF was actually written.
    private async Task<bool> SaveAsync()
    {
        if (ViewModel.Pages.Count == 0 || !ViewModel.CanSave || _saveCts is not null)
        {
            return false;
        }

        IReadOnlyList<PageItem> savablePages = ViewModel.SavablePages();
        List<string> errorPageNames = ViewModel.Pages.Where(p => p.HasError).Select(p => p.FileName).ToList();
        if (savablePages.Count == 0)
        {
            ViewModel.ReportError(ResourceLoader.GetString("NothingToSaveMessage"));
            return false;
        }

        var picker = new FileSavePicker
        {
            // SuggestedStartLocation only applies the very first time — once SettingsIdentifier
            // is set, Windows itself remembers the last folder the user actually saved to under
            // that identifier and starts there on every subsequent save, with no persistence
            // code needed on our side.
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SettingsIdentifier = "SaveFolder",
        };
        picker.FileTypeChoices.Add("PDF Document", new List<string> { ".pdf" });

        string desiredName = SaveFileNaming.ComputeDefaultFileName();
        string resolvedName = ViewModel.FolderPath is not null
            ? SaveFileNaming.ResolveCollision(ViewModel.FolderPath, desiredName)
            : desiredName;
        // SuggestedFileName excludes the extension — the picker appends it from FileTypeChoices.
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(resolvedName);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        bool showProgress = savablePages.Count > 10;
        SaveProgressPanel.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        SaveProgressBar.Maximum = savablePages.Count;
        SaveProgressBar.Value = 0;

        _saveCts = new CancellationTokenSource();
        var progress = new Progress<int>(count => DispatcherQueue.TryEnqueue(() => SaveProgressBar.Value = count));

        try
        {
            await ViewModel.SaveAsync(file.Path, _saveCts.Token, progress);
            ShowSavedMessage(file.Path);
            if (errorPageNames.Count > 0)
            {
                ViewModel.ReportSkippedFiles(errorPageNames, Array.Empty<string>());
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            DeleteEmptySavePlaceholder(file.Path);
        }
        catch (Exception ex)
        {
            DeleteEmptySavePlaceholder(file.Path);
            AppLog.LogError("SaveAsync", ex);
            ViewModel.ReportError(ex.Message);
        }
        finally
        {
            SaveProgressPanel.Visibility = Visibility.Collapsed;
            _saveCts.Dispose();
            _saveCts = null;
        }

        return false;
    }

    // FileSavePicker creates an empty file at a newly-chosen path the moment the user picks it, so a
    // failed or cancelled save leaves that behind. Only an empty file is removed: a non-empty one is
    // a PDF the user chose to overwrite, which the temp-file write never touched.
    private static void DeleteEmptySavePlaceholder(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length == 0)
            {
                info.Delete();
            }
        }
        catch (IOException)
        {
            // Best-effort — an empty leftover stub is untidy but harmless.
        }
    }

    // A save that worked supersedes an earlier error (nothing to save, a failed save), so that row
    // closes rather than sitting beside "Saved".
    private void ShowSavedMessage(string savedPath)
    {
        Messages.Close(ErrorMessageKey);
        Messages.Show(
            SavedMessageKey,
            MessageSeverity.Success,
            string.Format(ResourceLoader.GetString("SavedFormat"), Path.GetFileName(savedPath)),
            new MessageAction(
                ResourceLoader.GetString("OpenSavedFileAction"),
                () => Process.Start(new ProcessStartInfo(savedPath) { UseShellExecute = true })),
            new MessageAction(
                ResourceLoader.GetString("ShowInFolderAction"),
                () => Process.Start("explorer.exe", $"/select,\"{savedPath}\"")));
    }

    private void PagesGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsCtrlDown();
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        var selected = PagesGridView.SelectedItems.OfType<PageItem>().ToList();

        switch (e.Key)
        {
            case VirtualKey.Escape:
                ClosePreview();
                e.Handled = true;
                break;

            case VirtualKey.Left when !ctrl && PreviewOverlay.Visibility == Visibility.Visible:
                _ = StepPreviewAsync(-1);
                e.Handled = true;
                break;

            case VirtualKey.Right when !ctrl && PreviewOverlay.Visibility == Visibility.Visible:
                _ = StepPreviewAsync(1);
                e.Handled = true;
                break;

            case VirtualKey.Left when ctrl:
                ViewModel.MoveSelected(selected, -1);
                e.Handled = true;
                break;

            case VirtualKey.Right when ctrl:
                ViewModel.MoveSelected(selected, 1);
                e.Handled = true;
                break;

            case VirtualKey.R:
                ViewModel.RotateSelected(selected, clockwise: !shift);
                e.Handled = true;
                break;

            case VirtualKey.Delete:
                ViewModel.RemoveSelected(selected);
                e.Handled = true;
                break;

            case VirtualKey.A when ctrl:
                PagesGridView.SelectAll();
                e.Handled = true;
                break;
        }
    }
}
