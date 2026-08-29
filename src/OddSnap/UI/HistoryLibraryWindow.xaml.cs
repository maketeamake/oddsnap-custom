using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Helpers;
using OddSnap.Services;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using ScreenshotCaptureMode = OddSnap.Models.CaptureMode;

namespace OddSnap.UI;

public partial class HistoryLibraryWindow : Window
{
    private readonly HistoryService _historyService;
    private readonly ImageSearchIndexService _imageSearchIndexService;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _indexRefreshTimer;
    private readonly ObservableCollection<LibraryImageItem> _visibleItems = new();
    private List<LibraryImageItem> _allItems = new();
    private LibraryFilterItem? _activeFilter;
    private bool _suppressFilterEvents;
    private int _previewLoadVersion;
    private bool _closed;
    private string? _pendingSelectionPath;
    private ScrollViewer? _filmstripScrollViewer;
    private bool _syncingFilmstripScrollBar;

    public HistoryLibraryWindow(HistoryService historyService, ImageSearchIndexService imageSearchIndexService)
    {
        _historyService = historyService;
        _imageSearchIndexService = imageSearchIndexService;
        InitializeComponent();
        InitializeInlineToolbarVisuals();

        Theme.Refresh();
        OddSnapUiCaptureVisibility.Track(this);
        FilmstripList.ItemsSource = _visibleItems;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilters();
        };
        _indexRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _indexRefreshTimer.Tick += (_, _) =>
        {
            _indexRefreshTimer.Stop();
            RefreshSearchTextFromIndex();
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                ApplyFilters();
        };

        Loaded += HistoryLibraryWindow_Loaded;
        Closed += HistoryLibraryWindow_Closed;
    }

    private void HistoryLibraryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _historyService.Changed += HistoryService_Changed;
        _imageSearchIndexService.Changed += ImageSearchIndexService_Changed;
        RefreshLibrary();
        if (!string.IsNullOrWhiteSpace(_pendingSelectionPath))
        {
            var pending = _pendingSelectionPath;
            _pendingSelectionPath = null;
            SelectCapture(pending);
        }
    }

    private void HistoryLibraryWindow_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _searchTimer.Stop();
        _indexRefreshTimer.Stop();
        _historyService.Changed -= HistoryService_Changed;
        _imageSearchIndexService.Changed -= ImageSearchIndexService_Changed;
        if (_filmstripScrollViewer is not null)
            _filmstripScrollViewer.ScrollChanged -= FilmstripScrollViewer_ScrollChanged;
        PreviewImage.Source = null;
        foreach (var item in _allItems)
            item.Thumbnail = null;
        DisposeInlineEditorProject();
    }

    private void HistoryService_Changed()
    {
        if (_suppressHistoryRefresh)
            return;
        _ = Dispatcher.BeginInvoke(RefreshLibrary, DispatcherPriority.Background);
    }

    private void ImageSearchIndexService_Changed()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_closed)
                return;
            _indexRefreshTimer.Stop();
            _indexRefreshTimer.Start();
        }, DispatcherPriority.Background);
    }

    private void RefreshLibrary()
    {
        if (_closed)
            return;

        var selectedPath = (FilmstripList.SelectedItem as LibraryImageItem)?.Entry.FilePath;
        var previousItems = _allItems.ToDictionary(item => item.Entry.FilePath, StringComparer.OrdinalIgnoreCase);
        _allItems = _historyService.ImageEntries
            .OrderByDescending(entry => entry.CapturedAt)
            .Select(entry =>
            {
                if (previousItems.TryGetValue(entry.FilePath, out var existing))
                {
                    existing.Entry = entry;
                    existing.RefreshSearchText(_imageSearchIndexService);
                    return existing;
                }

                return new LibraryImageItem(entry, _imageSearchIndexService);
            })
            .ToList();

        RefreshFilterLists();
        ApplyFilters(selectedPath);
    }

    private void RefreshSearchTextFromIndex()
    {
        foreach (var item in _allItems)
            item.RefreshSearchText(_imageSearchIndexService);
    }

    private void RefreshFilterLists()
    {
        var activeKind = _activeFilter?.Kind ?? LibraryFilterKind.All;
        var activeKey = _activeFilter?.Key ?? "all";
        _suppressFilterEvents = true;
        try
        {
            var all = new LibraryFilterItem(LibraryFilterKind.All, "all", $"All captures ({_allItems.Count})", "All saved screenshots");
            var dates = new List<LibraryFilterItem> { all };
            foreach (var yearGroup in _allItems.GroupBy(item => item.Entry.CapturedAt.Year).OrderByDescending(group => group.Key))
            {
                dates.Add(new LibraryFilterItem(
                    LibraryFilterKind.Year,
                    yearGroup.Key.ToString(CultureInfo.InvariantCulture),
                    $"{yearGroup.Key} ({yearGroup.Count()})",
                    $"Screenshots from {yearGroup.Key}"));

                foreach (var monthGroup in yearGroup.GroupBy(item => new DateTime(item.Entry.CapturedAt.Year, item.Entry.CapturedAt.Month, 1)).OrderByDescending(group => group.Key))
                {
                    dates.Add(new LibraryFilterItem(
                        LibraryFilterKind.Month,
                        monthGroup.Key.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                        $"   {monthGroup.Key.ToString("MMMM", CultureInfo.CurrentCulture)} ({monthGroup.Count()})",
                        monthGroup.Key.ToString("MMMM yyyy", CultureInfo.CurrentCulture)));

                    foreach (var dayGroup in monthGroup.GroupBy(item => item.Entry.CapturedAt.Date).OrderByDescending(group => group.Key))
                    {
                        dates.Add(new LibraryFilterItem(
                            LibraryFilterKind.Day,
                            dayGroup.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            $"      {dayGroup.Key.ToString("MMM d, dddd", CultureInfo.CurrentCulture)} ({dayGroup.Count()})",
                            dayGroup.Key.ToString("D", CultureInfo.CurrentCulture)));
                    }
                }
            }

            var folders = new List<LibraryFilterItem> { all };
            folders.AddRange(_allItems
                .GroupBy(item => Path.GetDirectoryName(item.Entry.FilePath) ?? "", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Max(item => item.Entry.CapturedAt))
                .Select(group => new LibraryFilterItem(
                    LibraryFilterKind.Folder,
                    group.Key,
                    $"{GetFolderDisplayName(group.Key)} ({group.Count()})",
                    group.Key)));

            var applications = new List<LibraryFilterItem> { all };
            applications.AddRange(_allItems
                .GroupBy(item => NormalizeApplicationName(item.Entry.ForegroundProcessName), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new LibraryFilterItem(
                    LibraryFilterKind.Application,
                    group.Key,
                    $"{group.Key} ({group.Count()})",
                    $"Screenshots captured from {group.Key}")));

            DateFilterList.ItemsSource = dates;
            FolderFilterList.ItemsSource = folders;
            ApplicationFilterList.ItemsSource = applications;

            _activeFilter = dates.Concat(folders).Concat(applications)
                .FirstOrDefault(item => item.Kind == activeKind && item.Key.Equals(activeKey, StringComparison.OrdinalIgnoreCase))
                ?? all;

            var owner = GetFilterList(_activeFilter.Kind);
            owner.SelectedItem = owner.Items.Cast<LibraryFilterItem>()
                .FirstOrDefault(item => item.Kind == _activeFilter.Kind && item.Key.Equals(_activeFilter.Key, StringComparison.OrdinalIgnoreCase))
                ?? owner.Items.Cast<LibraryFilterItem>().FirstOrDefault();
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void FilterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || sender is not ListBox list || list.SelectedItem is not LibraryFilterItem selected)
            return;

        _activeFilter = selected;
        _suppressFilterEvents = true;
        try
        {
            foreach (var other in new[] { DateFilterList, FolderFilterList, ApplicationFilterList })
            {
                if (!ReferenceEquals(other, list))
                    other.SelectedItem = null;
            }
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        ApplyFilters();
    }

    private ListBox GetFilterList(LibraryFilterKind kind) => kind switch
    {
        LibraryFilterKind.Folder => FolderFilterList,
        LibraryFilterKind.Application => ApplicationFilterList,
        _ => DateFilterList
    };

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void ApplyFilters(string? preferredPath = null)
    {
        var currentPath = preferredPath ?? (FilmstripList.SelectedItem as LibraryImageItem)?.Entry.FilePath;
        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = _allItems
            .Where(MatchesActiveFilter)
            .Select(item => new
            {
                Item = item,
                Score = string.IsNullOrWhiteSpace(query)
                    ? 1
                    : ImageSearchQueryMatcher.Score(query, item.SearchText, item.Entry.FileName)
            })
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Item.Entry.CapturedAt)
            .Select(result => result.Item)
            .ToList();

        _visibleItems.Clear();
        foreach (var item in filtered)
            _visibleItems.Add(item);

        CountText.Text = $"{filtered.Count} of {_allItems.Count}";
        var selected = !string.IsNullOrWhiteSpace(currentPath)
            ? filtered.FirstOrDefault(item => item.Entry.FilePath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            : null;
        FilmstripList.SelectedItem = selected ?? filtered.FirstOrDefault();
        if (FilmstripList.SelectedItem is not null)
            FilmstripList.ScrollIntoView(FilmstripList.SelectedItem);
        else
            ClearPreview();
    }

    private bool MatchesActiveFilter(LibraryImageItem item)
    {
        if (_activeFilter is null || _activeFilter.Kind == LibraryFilterKind.All)
            return true;

        var captured = item.Entry.CapturedAt;
        return _activeFilter.Kind switch
        {
            LibraryFilterKind.Year => captured.Year.ToString(CultureInfo.InvariantCulture) == _activeFilter.Key,
            LibraryFilterKind.Month => captured.ToString("yyyy-MM", CultureInfo.InvariantCulture) == _activeFilter.Key,
            LibraryFilterKind.Day => captured.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == _activeFilter.Key,
            LibraryFilterKind.Folder => string.Equals(Path.GetDirectoryName(item.Entry.FilePath) ?? "", _activeFilter.Key, StringComparison.OrdinalIgnoreCase),
            LibraryFilterKind.Application => string.Equals(NormalizeApplicationName(item.Entry.ForegroundProcessName), _activeFilter.Key, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private async void FilmstripList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilmstripList.SelectedItem is not LibraryImageItem item)
        {
            ClearPreview();
            return;
        }

        await LoadPreviewAsync(item);
    }

    private void FilmstripList_Loaded(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(AttachFilmstripScrollViewer, DispatcherPriority.Loaded);

    private void AttachFilmstripScrollViewer()
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(FilmstripList);
        if (ReferenceEquals(scrollViewer, _filmstripScrollViewer))
        {
            UpdateFilmstripScrollBar();
            return;
        }

        if (_filmstripScrollViewer is not null)
            _filmstripScrollViewer.ScrollChanged -= FilmstripScrollViewer_ScrollChanged;
        _filmstripScrollViewer = scrollViewer;
        if (_filmstripScrollViewer is not null)
            _filmstripScrollViewer.ScrollChanged += FilmstripScrollViewer_ScrollChanged;
        UpdateFilmstripScrollBar();
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } descendant)
                return descendant;
        }
        return null;
    }

    private void FilmstripScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateFilmstripScrollBar();

    private void FilmstripList_SizeChanged(object sender, SizeChangedEventArgs e)
        => Dispatcher.BeginInvoke(UpdateFilmstripScrollBar, DispatcherPriority.Loaded);

    private void UpdateFilmstripScrollBar()
    {
        if (_filmstripScrollViewer is null)
            return;

        _syncingFilmstripScrollBar = true;
        try
        {
            FilmstripScrollBar.Maximum = Math.Max(0d, _filmstripScrollViewer.ScrollableWidth);
            FilmstripScrollBar.ViewportSize = Math.Max(1d, _filmstripScrollViewer.ViewportWidth);
            FilmstripScrollBar.LargeChange = Math.Max(116d, _filmstripScrollViewer.ViewportWidth * 0.8d);
            FilmstripScrollBar.Value = Math.Clamp(
                _filmstripScrollViewer.HorizontalOffset,
                FilmstripScrollBar.Minimum,
                FilmstripScrollBar.Maximum);
            FilmstripScrollBar.IsEnabled = FilmstripScrollBar.Maximum > 0.5d;
        }
        finally
        {
            _syncingFilmstripScrollBar = false;
        }
    }

    private void FilmstripScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncingFilmstripScrollBar && _filmstripScrollViewer is not null)
            _filmstripScrollViewer.ScrollToHorizontalOffset(e.NewValue);
    }

    private void FilmstripList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_filmstripScrollViewer is null || _filmstripScrollViewer.ScrollableWidth <= 0)
            return;

        double itemSteps = Math.Max(1d, Math.Abs(e.Delta) / 120d);
        double offset = _filmstripScrollViewer.HorizontalOffset - Math.Sign(e.Delta) * 116d * itemSteps;
        _filmstripScrollViewer.ScrollToHorizontalOffset(Math.Clamp(offset, 0d, _filmstripScrollViewer.ScrollableWidth));
        e.Handled = true;
    }

    private async Task LoadPreviewAsync(LibraryImageItem item)
    {
        var version = ++_previewLoadVersion;
        PreviewImage.Source = null;
        PreviewEmptyText.Text = "Loading...";
        PreviewEmptyText.Visibility = Visibility.Visible;
        UpdatePreviewMetadata(item.Entry);

        try
        {
            var source = await Task.Run(() => LoadBitmap(item.Entry.FilePath, 2600));
            if (_closed || version != _previewLoadVersion || !ReferenceEquals(FilmstripList.SelectedItem, item))
                return;

            PreviewImage.Source = source;
            PreviewEmptyText.Visibility = Visibility.Collapsed;
            await LoadInlineEditorProjectAsync(item, version);
        }
        catch (Exception ex)
        {
            if (version != _previewLoadVersion)
                return;
            PreviewEmptyText.Text = $"Could not load this screenshot\n{ex.Message}";
            PreviewEmptyText.Visibility = Visibility.Visible;
        }
    }

    private async void FilmstripImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image { DataContext: LibraryImageItem item } || item.Thumbnail is not null)
            return;
        if (Interlocked.Exchange(ref item.ThumbnailLoading, 1) != 0)
            return;

        try
        {
            var thumbnail = await Task.Run(() => LoadBitmap(item.Entry.FilePath, 220));
            if (!_closed)
                item.Thumbnail = thumbnail;
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref item.ThumbnailLoading, 0);
        }
    }

    private void UpdatePreviewMetadata(HistoryEntry entry)
    {
        PreviewSourceText.Text = BuildCaptureSourceText(entry);
        PreviewDateText.Text = entry.CapturedAt.ToString("dddd, d MMMM yyyy · HH:mm:ss", CultureInfo.CurrentCulture);
        PreviewTagsText.Text = BuildTagsText(entry.Tags);
        PreviewTagsText.Visibility = string.IsNullOrWhiteSpace(PreviewTagsText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ClearPreview()
    {
        DisposeInlineEditorProject();
        _previewLoadVersion++;
        PreviewImage.Source = null;
        PreviewEmptyText.Text = _allItems.Count == 0 ? "No screenshots yet" : "No screenshots match this filter";
        PreviewEmptyText.Visibility = Visibility.Visible;
        PreviewSourceText.Text = "";
        PreviewDateText.Text = "";
        PreviewTagsText.Text = "";
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
        => CopyCurrentLibraryImage();

    private void CopyFullPath_Click(object sender, RoutedEventArgs e)
    {
        if (FilmstripList.SelectedItem is not LibraryImageItem item)
            return;

        try
        {
            ClipboardService.CopyTextToClipboard(Path.GetFullPath(item.Entry.FilePath));
            ToastWindow.Show("Path copied", "Full file path copied to clipboard");
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Copy path failed", ex.Message);
        }
    }

    private bool CopyCurrentLibraryImage()
    {
        if (FilmstripList.SelectedItem is not LibraryImageItem item)
            return false;

        try
        {
            using var bitmap = _inlineProject is not null &&
                               string.Equals(_inlineProjectPath, item.Entry.FilePath, StringComparison.OrdinalIgnoreCase)
                ? RegionOverlayForm.RenderEditorProject(_inlineProject.BaseImage, _inlineAnnotations, strokeShadow: false)
                : BitmapPerf.LoadDetached(item.Entry.FilePath);
            ClipboardService.CopyToClipboard(bitmap, item.Entry.FilePath);
            ToastWindow.Show("Copied", "Screenshot copied to clipboard");
            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Copy failed", ex.Message);
            return false;
        }
    }

    private void SelectTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Select);
    private void ArrowTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Arrow);
    private void CropTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Crop);
    private void TextTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Text);
    private void HighlightTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Highlight);
    private void BlurTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Blur);
    private void StepTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.StepNumber);
    private void DrawTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Draw);
    private void CurvedArrowTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.CurvedArrow);
    private void LineTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Line);
    private void RulerTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Ruler);
    private void RectangleTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.RectShape);
    private void CircleTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.CircleShape);
    private void EraserTool_Click(object sender, RoutedEventArgs e) => SetInlineEditorTool(ScreenshotCaptureMode.Eraser);

    private void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if (FilmstripList.SelectedItem is not LibraryImageItem item ||
            !HistoryTagsDialog.TryEdit(this, item.Entry.Tags, out var editedTags))
        {
            return;
        }

        var normalized = HistoryEntryUtilities.NormalizeTags(editedTags);
        if (string.Equals(normalized, HistoryEntryUtilities.NormalizeTags(item.Entry.Tags), StringComparison.Ordinal))
            return;

        item.Entry.Tags = normalized;
        _historyService.SaveEntry(item.Entry);
        _imageSearchIndexService.NotifyHistoryMetadataChanged();
        item.RefreshSearchText(_imageSearchIndexService);
        UpdatePreviewMetadata(item.Entry);
        ApplyFilters(item.Entry.FilePath);
        ToastWindow.Show("Tags saved", string.IsNullOrWhiteSpace(normalized) ? "Tags removed" : normalized);
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FilmstripList.SelectedItem is not LibraryImageItem item)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Entry.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Could not open folder", ex.Message);
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedScreenshot();
    }

    private void DeleteFilmstripItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: LibraryImageItem item })
            return;
        FilmstripList.SelectedItem = item;
        DeleteScreenshot(item);
    }

    private void DeleteSelectedScreenshot()
    {
        if (FilmstripList.SelectedItem is LibraryImageItem item)
            DeleteScreenshot(item);
    }

    private void DeleteScreenshot(LibraryImageItem item)
    {
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Delete screenshot?",
                "This removes the image and its editable annotation project from OddSnap history.",
                "Delete",
                "Cancel"))
        {
            return;
        }

        EditableScreenshotService.DeleteProject(item.Entry.FilePath);
        _historyService.DeleteEntry(item.Entry);
        _imageSearchIndexService.NotifyHistoryMetadataChanged();
        ToastWindow.Show("Screenshot deleted", "");
    }

    public void RefreshEditedImage(string filePath)
    {
        var item = _allItems.FirstOrDefault(candidate => candidate.Entry.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;
        item.Thumbnail = null;
        _previewLoadVersion++;
        _ = LoadPreviewAsync(item);
    }

    public void SelectCapture(string filePath)
    {
        if (!IsLoaded)
        {
            _pendingSelectionPath = filePath;
            return;
        }

        RefreshLibrary();
        SearchBox.Text = "";
        var selectedCapture = _allItems.FirstOrDefault(candidate =>
            candidate.Entry.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        string? dayKey = selectedCapture?.Entry.CapturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayFilter = DateFilterList.Items.Cast<LibraryFilterItem>().FirstOrDefault(candidate =>
            candidate.Kind == LibraryFilterKind.Day &&
            string.Equals(candidate.Key, dayKey, StringComparison.OrdinalIgnoreCase));
        _activeFilter = dayFilter ?? new LibraryFilterItem(
            LibraryFilterKind.All,
            "all",
            "All captures",
            "All saved screenshots");
        _suppressFilterEvents = true;
        try
        {
            DateFilterList.SelectedItem = dayFilter ?? DateFilterList.Items.Cast<LibraryFilterItem>().FirstOrDefault();
            FolderFilterList.SelectedItem = null;
            ApplicationFilterList.SelectedItem = null;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
        ApplyFilters(filePath);
    }

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        e.Handled = true;
    }

    private static BitmapSource LoadBitmap(string path, int decodePixelWidth)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string BuildCaptureSourceText(HistoryEntry entry)
    {
        var app = string.IsNullOrWhiteSpace(entry.ForegroundProcessName)
            ? "Unknown application"
            : entry.ForegroundProcessName.Trim();
        var title = entry.ForegroundWindowTitle?.Trim();
        return string.IsNullOrWhiteSpace(title) || title.Equals(app, StringComparison.OrdinalIgnoreCase)
            ? app
            : $"{app} · {title}";
    }

    private static string BuildTagsText(string? tags)
    {
        var normalized = HistoryEntryUtilities.NormalizeTags(tags);
        return string.IsNullOrWhiteSpace(normalized)
            ? ""
            : "#" + normalized.Replace(", ", "  #", StringComparison.Ordinal);
    }

    private static string NormalizeApplicationName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown application" : value.Trim();

    private static string GetFolderDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Unknown folder";
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }
}

internal enum LibraryFilterKind
{
    All,
    Year,
    Month,
    Day,
    Folder,
    Application
}

internal sealed record LibraryFilterItem(LibraryFilterKind Kind, string Key, string Label, string ToolTip);

internal sealed class LibraryImageItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;

    public LibraryImageItem(HistoryEntry entry, ImageSearchIndexService imageSearchIndexService)
    {
        Entry = entry;
        RefreshSearchText(imageSearchIndexService);
    }

    public HistoryEntry Entry { get; set; }
    public string SearchText { get; private set; } = "";
    public int ThumbnailLoading;

    public string ToolTip => $"{Entry.CapturedAt:g} · {BuildSourceTooltip()}";

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
                return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public void RefreshSearchText(ImageSearchIndexService imageSearchIndexService)
    {
        SearchText = string.Join(
            ' ',
            new[]
            {
                imageSearchIndexService.BuildSearchText(Entry.FilePath, Entry.FileName),
                HistoryEntryUtilities.BuildMetadataSearchText(Entry)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTip)));
    }

    private string BuildSourceTooltip()
    {
        if (!string.IsNullOrWhiteSpace(Entry.ForegroundWindowTitle))
            return Entry.ForegroundWindowTitle;
        if (!string.IsNullOrWhiteSpace(Entry.ForegroundProcessName))
            return Entry.ForegroundProcessName;
        return "Unknown application";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
