using System.Drawing;
using System.IO;
using OddSnap.Capture;
using OddSnap.Models;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

public partial class App
{
    private void OnImagePreviewEditRequested(string filePath)
    {
        var entry = EnsureHistoryService().ImageEntries.FirstOrDefault(candidate =>
            candidate.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            ToastWindow.ShowError("Edit unavailable", "This screenshot is not in OddSnap history.", filePath);
            return;
        }

        EditHistoryCapture(entry);
    }

    private void ShowQuickCaptureEditor(
        Bitmap baseImage,
        IReadOnlyList<Annotation> annotations,
        bool useAiRedirect)
    {
        RegionOverlayForm? overlay = null;
        bool baseImageCloseHandlerAttached = false;
        try
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            var work = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
            var left = work.Left + Math.Max(0, (work.Width - baseImage.Width) / 2);
            var top = work.Top + Math.Max(0, (work.Height - baseImage.Height) / 2);
            var bounds = new Rectangle(left, top, baseImage.Width, baseImage.Height);
            var settings = _settingsService!.Settings;

            overlay = new RegionOverlayForm(
                baseImage,
                bounds,
                CaptureMode.Arrow,
                WindowDetectionMode.Off,
                CenterSelectionAspectRatio.Free,
                editorMode: true,
                editorAnnotations: annotations)
            {
                ShowCrosshairGuides = false,
                DetectWindows = false,
                ShowCaptureMagnifier = false,
                AnnotationStrokeShadow = settings.AnnotationStrokeShadow,
                CaptureDockSide = settings.CaptureDockSide,
                UiScale = settings.UiScale
            };

            ConfigureScreenshotEditorToolbar(overlay, settings);

            bool saveStarted = false;
            overlay.EditorSaveRequested += () =>
            {
                if (saveStarted)
                    return;

                saveStarted = true;
                Bitmap? flattened = null;
                EditableCaptureData? editableCapture = null;
                try
                {
                    flattened = overlay.RenderAnnotatedBitmap();
                    var snapshot = overlay.CreateEditorProjectSnapshot();
                    if (snapshot.Annotations.Count > 0)
                    {
                        editableCapture = new EditableCaptureData(snapshot.BaseImage, snapshot.Annotations);
                    }
                    else
                    {
                        snapshot.BaseImage.Dispose();
                    }

                    overlay.CloseAfterEditorSave();
                    HandleCaptureResult(flattened, useAiRedirect, editableCapture);
                    flattened = null;
                    editableCapture = null;
                }
                catch (Exception ex)
                {
                    flattened?.Dispose();
                    editableCapture?.Dispose();
                    saveStarted = false;
                    if (overlay.IsDisposed || overlay.Disposing)
                        ResetCapturing();
                    AppDiagnostics.LogError("quick-editor.save", ex);
                    ToastWindow.ShowError("Save failed", ex.Message);
                }
            };
            overlay.FormClosed += (_, _) =>
            {
                baseImage.Dispose();
                if (!saveStarted)
                    ResetCapturing();
            };
            baseImageCloseHandlerAttached = true;
            overlay.Show();
        }
        catch (Exception ex)
        {
            if (!baseImageCloseHandlerAttached)
                baseImage.Dispose();
            if (overlay is not null && !overlay.IsDisposed && !overlay.Disposing)
                overlay.Close();
            ResetCapturing();
            AppDiagnostics.LogError("quick-editor.open", ex);
            ToastWindow.ShowError("Editor failed", ex.Message);
        }
    }

    private void EditHistoryCapture(
        HistoryEntry entry,
        CaptureMode initialMode = CaptureMode.Select,
        bool autoCopyOnChange = false)
    {
        if (!File.Exists(entry.FilePath))
        {
            ToastWindow.ShowError("Edit failed", "The screenshot file no longer exists.");
            return;
        }

        try
        {
            var project = EditableScreenshotService.Load(entry.FilePath);
            var cursor = System.Windows.Forms.Cursor.Position;
            var work = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
            var left = work.Left + Math.Max(0, (work.Width - project.BaseImage.Width) / 2);
            var top = work.Top + Math.Max(0, (work.Height - project.BaseImage.Height) / 2);
            var bounds = new Rectangle(left, top, project.BaseImage.Width, project.BaseImage.Height);
            var settings = _settingsService!.Settings;

            var overlay = new RegionOverlayForm(
                project.BaseImage,
                bounds,
                initialMode,
                WindowDetectionMode.Off,
                CenterSelectionAspectRatio.Free,
                editorMode: true,
                editorAnnotations: project.Annotations)
            {
                ShowCrosshairGuides = settings.ShowCrosshairGuides,
                DetectWindows = false,
                ShowCaptureMagnifier = false,
                AnnotationStrokeShadow = settings.AnnotationStrokeShadow,
                CaptureDockSide = settings.CaptureDockSide,
                UiScale = settings.UiScale
            };

            ConfigureScreenshotEditorToolbar(overlay, settings);

            bool saveInProgress = false;
            bool SaveEditorState(bool closeAfterSave, bool copyToClipboard)
            {
                if (saveInProgress)
                    return false;

                saveInProgress = true;
                try
                {
                    using var flattened = overlay.RenderAnnotatedBitmap();
                    var snapshot = overlay.CreateEditorProjectSnapshot();
                    using var baseImage = snapshot.BaseImage;
                    EditableScreenshotService.SaveProject(entry.FilePath, baseImage, snapshot.Annotations);
                    EditableScreenshotService.SaveFlattenedImage(entry.FilePath, flattened, settings.JpegQuality);

                    var info = new FileInfo(entry.FilePath);
                    entry.Width = flattened.Width;
                    entry.Height = flattened.Height;
                    entry.FileSizeBytes = info.Length;
                    EnsureHistoryService().SaveEntry(entry);
                    EnsureImageSearchIndexService().NotifyHistoryMetadataChanged();
                    _historyLibraryWindow?.RefreshEditedImage(entry.FilePath);
                    if (copyToClipboard)
                        ClipboardService.CopyToClipboard(flattened, entry.FilePath);
                    if (closeAfterSave)
                    {
                        overlay.CloseAfterEditorSave();
                        ToastWindow.Show("Screenshot saved", "Annotations remain editable from the Library.");
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogError("editable-project.save", ex);
                    ToastWindow.ShowError("Save failed", ex.Message);
                    return false;
                }
                finally
                {
                    saveInProgress = false;
                }
            }

            overlay.EditorSaveRequested += () => SaveEditorState(closeAfterSave: true, copyToClipboard: true);
            if (autoCopyOnChange)
                overlay.EditorContentChanged += () => SaveEditorState(closeAfterSave: false, copyToClipboard: true);
            overlay.FormClosed += (_, _) => project.Dispose();
            overlay.Show();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("editable-project.open", ex);
            ToastWindow.ShowError("Edit failed", ex.Message);
        }
    }

    private static void ConfigureScreenshotEditorToolbar(RegionOverlayForm overlay, AppSettings settings)
    {
        var annotationToolIds = ToolDef.AllTools.Where(tool => tool.Group == 1).Select(tool => tool.Id).ToList();
        overlay.SetEnabledTools(annotationToolIds);
        var editorOrder = settings.ToolbarToolOrderIds?.Where(annotationToolIds.Contains).ToList();
        var editorPinned = settings.ToolbarPinnedToolIds?.Where(annotationToolIds.Contains).ToList();
        overlay.SetToolbarLayout(editorOrder, editorPinned);
        overlay.SetShowToolNumberBadges(settings.ShowToolNumberBadges);
    }
}
