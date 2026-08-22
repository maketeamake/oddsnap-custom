using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using ScreenshotCaptureMode = OddSnap.Models.CaptureMode;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfImage = System.Windows.Controls.Image;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OddSnap.UI;

public partial class HistoryLibraryWindow
{
    private static readonly DrawingColor DefaultInlineDrawingColor = DrawingColor.FromArgb(255, 255, 59, 48);
    private EditableScreenshotProject? _inlineProject;
    private string? _inlineProjectPath;
    private readonly List<Annotation> _inlineAnnotations = [];
    private readonly Stack<InlineEditorSnapshot> _inlineUndoStack = [];
    private readonly Stack<InlineEditorSnapshot> _inlineRedoStack = [];
    private readonly List<Annotation> _inlinePreviewAnnotations = [];
    private readonly Dictionary<WpfButton, (ScreenshotCaptureMode Mode, string IconId, int Shortcut)> _inlineToolButtons = [];
    private ScreenshotCaptureMode _inlineTool = ScreenshotCaptureMode.Select;
    private DrawingColor _inlineColor = DefaultInlineDrawingColor;
    private readonly Dictionary<ScreenshotCaptureMode, DrawingColor> _inlineToolColors = new()
    {
        [ScreenshotCaptureMode.Arrow] = DefaultInlineDrawingColor,
        [ScreenshotCaptureMode.Draw] = DefaultInlineDrawingColor,
        [ScreenshotCaptureMode.Highlight] = DefaultInlineDrawingColor,
        [ScreenshotCaptureMode.Text] = DefaultInlineDrawingColor
    };
    private InlineCropMode _inlineCropMode = InlineCropMode.KeepSelection;
    private DrawingColor? _inlineShapeBorderColor;
    private int _inlineHighlightOpacityPercent = 20;
    private DrawingPoint _inlineDragStart;
    private DrawingPoint _inlineDragCurrent;
    private readonly List<DrawingPoint> _inlinePoints = [];
    private bool _inlineDragging;
    private bool _inlineDraggingSelection;
    private bool _inlineAllowOutsideDrag;
    private int _inlineSelectedAnnotation = -1;
    private readonly HashSet<int> _inlineSelectedAnnotations = [];
    private readonly Dictionary<int, Annotation> _inlineSelectionOriginals = [];
    private Annotation? _inlineSelectionOriginal;
    private InlineResizeHandle _inlineResizeHandle;
    private bool _inlineTextContentHit;
    private int _inlineEditingTextIndex = -1;
    private DrawingPoint _inlineTextPosition;
    private int _inlineTextMaxWidth;
    private int _inlineTextEditorHeight;
    private float _inlineTextFontSize = 18f;
    private bool _syncingInlineFontSizeControl;
    private DrawingColor _inlineTextColor = DefaultInlineDrawingColor;
    private bool _inlineTextBold = true;
    private bool _inlineTextItalic;
    private bool _inlineTextStroke;
    private bool _inlineTextShadow;
    private bool _inlineTextBackground;
    private string _inlineTextFontFamily = "Segoe UI";
    private ImageSource? _inlineTextPreviousPreview;
    private List<Annotation>? _inlineTextPreviousPreviewAnnotations;
    private string _inlineSelectedEmoji = "✅";
    private DrawingColor _inlineEraserColor = DrawingColor.White;
    private InlineSaveJob? _inlineQueuedSaveJob;
    private bool _inlineSaveLoopRunning;
    private bool _suppressHistoryRefresh;

    private enum InlineResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        TextRight
    }

    private enum InlineCropMode
    {
        KeepSelection,
        CutOutBand
    }

    private void InitializeInlineToolbarVisuals()
    {
        RegisterInlineToolButton(SelectToolButton, ScreenshotCaptureMode.Select, "select", 1, "Select and move annotations");
        RegisterInlineToolButton(ArrowToolButton, ScreenshotCaptureMode.Arrow, "arrow", 2, "Arrow");
        RegisterInlineToolButton(DrawToolButton, ScreenshotCaptureMode.Draw, "pencil", 3, "Pencil");
        RegisterInlineToolButton(HighlightToolButton, ScreenshotCaptureMode.Highlight, "highlightBlock", 4, "Highlight");
        RegisterInlineToolButton(TextToolButton, ScreenshotCaptureMode.Text, "text", 5, "Text");
        TextToolButton.ToolTip = "Text (5 or T)";
        RegisterInlineToolButton(LineToolButton, ScreenshotCaptureMode.Line, "line", 6, "Line");
        RegisterInlineToolButton(CurvedArrowToolButton, ScreenshotCaptureMode.CurvedArrow, "curvedArrow", 7, "Curved arrow");
        RegisterInlineToolButton(BlurToolButton, ScreenshotCaptureMode.Blur, "blur", 0, "Blur");
        RegisterInlineToolButton(StepToolButton, ScreenshotCaptureMode.StepNumber, "step", 0, "Step number");
        RegisterInlineToolButton(RulerToolButton, ScreenshotCaptureMode.Ruler, "ruler", 0, "Ruler");
        RegisterInlineToolButton(RectangleToolButton, ScreenshotCaptureMode.RectShape, "rectShape", 0, "Rectangle");
        RegisterInlineToolButton(CircleToolButton, ScreenshotCaptureMode.CircleShape, "circleShape", 0, "Circle");
        RegisterInlineToolButton(EmojiToolButton, ScreenshotCaptureMode.Emoji, "emoji", 0, "Emoji");
        RegisterInlineToolButton(EraserToolButton, ScreenshotCaptureMode.Eraser, "eraser", 0, "Eraser");
        RegisterInlineToolButton(CropToolButton, ScreenshotCaptureMode.Crop, "rect", 0, "Crop image");
        InitializeColorMenuVisuals(ColorToolButton.ContextMenu);
        InitializeColorMenuVisuals(ShapeBorderToolButton.ContextMenu);
        InitializeColorMenuVisuals(ArrowPresetButton.ContextMenu);
        InitializeColorMenuVisuals(DrawPresetButton.ContextMenu);
        InitializeColorMenuVisuals(HighlightPresetButton.ContextMenu);
        SyncInlineFontSizeControl(_inlineTextFontSize);
        UpdateInlineToolButtons();
    }

    private void RegisterInlineToolButton(
        WpfButton button,
        ScreenshotCaptureMode mode,
        string iconId,
        int shortcut,
        string label)
    {
        _inlineToolButtons[button] = (mode, iconId, shortcut);
        button.ToolTip = shortcut > 0 ? $"{label} ({shortcut})" : label;
    }

    private static object CreateInlineToolIcon(
        string iconId,
        int shortcut,
        bool active,
        DrawingColor? configuredColor = null)
    {
        var grid = new Grid { Width = 24, Height = 24 };
        var color = configuredColor ?? (active
            ? DrawingColor.FromArgb(255, 0, 92, 184)
            : DrawingColor.FromArgb(255, 17, 24, 39));
        grid.Children.Add(new WpfImage
        {
            Source = FluentIcons.RenderWpf(iconId, color, 22, active),
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform
        });
        if (shortcut > 0)
        {
            grid.Children.Add(new TextBlock
            {
                Text = shortcut.ToString(),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = ToMediaBrush(configuredColor ?? (active
                    ? DrawingColor.FromArgb(255, 0, 92, 184)
                    : DrawingColor.FromArgb(255, 75, 85, 99))),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -2, -2)
            });
        }
        return grid;
    }

    private async Task LoadInlineEditorProjectAsync(LibraryImageItem item, int previewVersion)
    {
        EditableScreenshotProject? project = null;
        try
        {
            project = await Task.Run(() => EditableScreenshotService.Load(item.Entry.FilePath));
            if (_closed || previewVersion != _previewLoadVersion || !ReferenceEquals(FilmstripList.SelectedItem, item))
            {
                project.Dispose();
                return;
            }

            bool preserveActiveTool = string.Equals(
                _inlineProjectPath,
                item.Entry.FilePath,
                StringComparison.OrdinalIgnoreCase);
            var activeTool = _inlineTool;
            DisposeInlineEditorProject();
            _inlineProject = project;
            _inlineProjectPath = item.Entry.FilePath;
            project = null;
            _inlineAnnotations.Clear();
            _inlineAnnotations.AddRange(_inlineProject.Annotations);
            _inlinePreviewAnnotations.Clear();
            _inlinePreviewAnnotations.AddRange(_inlineProject.Annotations);
            ClearInlineHistoryStack(_inlineUndoStack);
            ClearInlineHistoryStack(_inlineRedoStack);
            ClearInlineSelectionState();
            _inlineResizeHandle = InlineResizeHandle.None;
            _inlineTool = preserveActiveTool ? activeTool : ScreenshotCaptureMode.Arrow;
            RestoreInlineToolColor();
            UpdateInlineToolButtons();
            ClearInlinePreview();
            RefreshPendingInlineAnnotations();
        }
        catch (Exception ex)
        {
            project?.Dispose();
            AppDiagnostics.LogError("library.inline-editor.load", ex);
            ToastWindow.ShowError("Edit failed", ex.Message);
        }
    }

    private void DisposeInlineEditorProject()
    {
        CommitInlineTextEditor(save: false);
        _inlineProject?.Dispose();
        _inlineProject = null;
        _inlineAnnotations.Clear();
        _inlinePreviewAnnotations.Clear();
        ClearInlineHistoryStack(_inlineUndoStack);
        ClearInlineHistoryStack(_inlineRedoStack);
        ClearInlineSelectionState();
        _inlineResizeHandle = InlineResizeHandle.None;
        ClearInlinePreview();
        PendingAnnotationCanvas.Children.Clear();
    }

    private void SetInlineEditorTool(ScreenshotCaptureMode mode)
    {
        CommitInlineTextEditor(save: true);
        _inlineTool = mode;
        RestoreInlineToolColor();
        ClearInlineSelectionState();
        _inlineResizeHandle = InlineResizeHandle.None;
        ClearInlinePreview();
        UpdateInlineToolButtons();
        EditorViewport.Cursor = mode == ScreenshotCaptureMode.Text ? WpfCursors.IBeam : WpfCursors.Cross;
    }

    private void ClearInlineSelectionState()
    {
        _inlineSelectedAnnotation = -1;
        _inlineSelectedAnnotations.Clear();
        _inlineSelectionOriginals.Clear();
        _inlineSelectionOriginal = null;
    }

    private void SelectOnlyInlineAnnotation(int index)
    {
        _inlineSelectedAnnotations.Clear();
        _inlineSelectionOriginals.Clear();
        if (index >= 0 && index < _inlineAnnotations.Count)
            _inlineSelectedAnnotations.Add(index);
        _inlineSelectedAnnotation = index;
        _inlineSelectionOriginal = index >= 0 && index < _inlineAnnotations.Count
            ? _inlineAnnotations[index]
            : null;
    }

    private void SnapshotInlineSelectionForDrag()
    {
        _inlineSelectionOriginals.Clear();
        foreach (int index in _inlineSelectedAnnotations)
        {
            if (index >= 0 && index < _inlineAnnotations.Count)
                _inlineSelectionOriginals[index] = _inlineAnnotations[index];
        }
        _inlineSelectionOriginal = _inlineSelectedAnnotation >= 0 &&
                                   _inlineSelectedAnnotation < _inlineAnnotations.Count
            ? _inlineAnnotations[_inlineSelectedAnnotation]
            : null;
    }

    private void UpdateInlineToolButtons()
    {
        foreach (var (button, definition) in _inlineToolButtons)
        {
            bool active = definition.Mode == _inlineTool;
            button.Content = CreateInlineToolIcon(
                definition.IconId,
                definition.Shortcut,
                active,
                _inlineToolColors.TryGetValue(definition.Mode, out var toolColor) ? toolColor : null);
            button.BorderThickness = active ? new Thickness(2) : new Thickness(1);
            button.Background = active
                ? new SolidColorBrush(MediaColor.FromRgb(191, 219, 254))
                : MediaBrushes.Transparent;
        }
        UndoToolButton.IsEnabled = _inlineUndoStack.Count > 0;
        RedoToolButton.IsEnabled = _inlineRedoStack.Count > 0;
        // Size presets live next to the Text tool, so the old contextual field no
        // longer shifts or clips the right side of the toolbar.
        TextSizePanel.Visibility = Visibility.Collapsed;
    }

    private void OpenInlinePresetMenu(WpfButton button)
    {
        if (button.ContextMenu is not { } menu)
            return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void ArrowPresetButton_Click(object sender, RoutedEventArgs e) => OpenInlinePresetMenu(ArrowPresetButton);

    private void DrawPresetButton_Click(object sender, RoutedEventArgs e) => OpenInlinePresetMenu(DrawPresetButton);

    private void HighlightPresetButton_Click(object sender, RoutedEventArgs e) => OpenInlinePresetMenu(HighlightPresetButton);

    private void TextPresetButton_Click(object sender, RoutedEventArgs e) => OpenInlinePresetMenu(TextPresetButton);

    private void CropPresetButton_Click(object sender, RoutedEventArgs e) => OpenInlinePresetMenu(CropPresetButton);

    private void CropModePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value })
            return;
        _inlineCropMode = string.Equals(value, "cutout", StringComparison.OrdinalIgnoreCase)
            ? InlineCropMode.CutOutBand
            : InlineCropMode.KeepSelection;
        CropToolButton.ToolTip = _inlineCropMode == InlineCropMode.CutOutBand
            ? "Cut out a horizontal or vertical band"
            : "Crop to selection";
        SetInlineEditorTool(ScreenshotCaptureMode.Crop);
    }

    private void ArrowColorPreset_Click(object sender, RoutedEventArgs e)
        => ApplyInlineToolColorPreset(sender, ScreenshotCaptureMode.Arrow);

    private void DrawColorPreset_Click(object sender, RoutedEventArgs e)
        => ApplyInlineToolColorPreset(sender, ScreenshotCaptureMode.Draw);

    private void HighlightColorPreset_Click(object sender, RoutedEventArgs e)
        => ApplyInlineToolColorPreset(sender, ScreenshotCaptureMode.Highlight);

    private void ApplyInlineToolColorPreset(object sender, ScreenshotCaptureMode mode)
    {
        if (sender is not MenuItem { Tag: string value })
            return;
        var media = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value);
        _inlineColor = DrawingColor.FromArgb(media.A, media.R, media.G, media.B);
        _inlineToolColors[mode] = _inlineColor;
        if (mode == ScreenshotCaptureMode.Text)
            _inlineTextColor = _inlineColor;
        ColorToolSwatch.Fill = new SolidColorBrush(media);
        ApplyColorToInlineSelection(_inlineColor);
        ActivateInlinePresetTool(mode);
    }

    private void TextSizePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } ||
            !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize))
        {
            return;
        }
        _inlineColor = DefaultInlineDrawingColor;
        _inlineTextColor = _inlineColor;
        _inlineToolColors[ScreenshotCaptureMode.Text] = _inlineColor;
        ColorToolSwatch.Fill = ToMediaBrush(_inlineColor);
        ApplyColorToInlineSelection(_inlineColor);
        ApplyInlineFontSize(fontSize);
        ActivateInlinePresetTool(ScreenshotCaptureMode.Text);
    }

    private void ActivateInlinePresetTool(ScreenshotCaptureMode mode)
    {
        CommitInlineTextEditor(save: true);
        _inlineTool = mode;
        UpdateInlineToolButtons();
        EditorViewport.Cursor = mode == ScreenshotCaptureMode.Text ? WpfCursors.IBeam : WpfCursors.Cross;
        ShowInlineSelection();
        EditorViewport.Focus();
    }

    private void ColorTool_Click(object sender, RoutedEventArgs e)
    {
        if (ColorToolButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ColorToolButton;
            menu.IsOpen = true;
        }
    }

    private static void InitializeColorMenuVisuals(ContextMenu? menu)
    {
        if (menu is null)
            return;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not string value || !value.StartsWith('#'))
                continue;
            var media = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value);
            string label = item.Header?.ToString()?.Replace("●", "").Trim() ?? "Color";
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(new Ellipse
            {
                Width = 13,
                Height = 13,
                Margin = new Thickness(0, 0, 8, 0),
                Fill = new SolidColorBrush(media),
                Stroke = new SolidColorBrush(MediaColor.FromRgb(107, 114, 128)),
                StrokeThickness = 1
            });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            item.Header = row;
        }
    }

    private void ColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value })
            return;

        var media = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value);
        _inlineColor = DrawingColor.FromArgb(media.A, media.R, media.G, media.B);
        if (_inlineToolColors.ContainsKey(_inlineTool))
            _inlineToolColors[_inlineTool] = _inlineColor;
        if (_inlineTool == ScreenshotCaptureMode.Text)
            _inlineTextColor = _inlineColor;
        ColorToolSwatch.Fill = new SolidColorBrush(media);
        ApplyColorToInlineSelection(_inlineColor);
        UpdateInlineToolButtons();
    }

    private void RestoreInlineToolColor()
    {
        if (!_inlineToolColors.TryGetValue(_inlineTool, out var color))
            return;
        _inlineColor = color;
        if (_inlineTool == ScreenshotCaptureMode.Text)
            _inlineTextColor = color;
        ColorToolSwatch.Fill = ToMediaBrush(color);
    }

    private void ApplyColorToInlineSelection(DrawingColor color)
    {
        var selected = _inlineSelectedAnnotations
            .Where(index => index >= 0 && index < _inlineAnnotations.Count)
            .OrderBy(index => index)
            .ToArray();
        if (selected.Length == 0)
            return;
        if (!selected.Any(index => !Equals(RecolorInlineAnnotation(_inlineAnnotations[index], color), _inlineAnnotations[index])))
            return;

        SnapshotInlineUndo();
        foreach (int index in selected)
            _inlineAnnotations[index] = RecolorInlineAnnotation(_inlineAnnotations[index], color);
        SnapshotInlineSelectionForDrag();
        RefreshPendingInlineAnnotations();
        ShowInlineSelection();
        SaveInlineEditor();
    }

    private static Annotation RecolorInlineAnnotation(Annotation annotation, DrawingColor color) => annotation switch
    {
        ArrowAnnotation value => value with { Color = color },
        CurvedArrowAnnotation value => value with { Color = color },
        DrawStroke value => value with { Color = color },
        HighlightAnnotation value => value with
        {
            Color = DrawingColor.FromArgb(value.Color.A, color.R, color.G, color.B)
        },
        StepNumberAnnotation value => value with { Color = color },
        TextAnnotation value => value with { Color = color },
        LineAnnotation value => value with { Color = color },
        RectShapeAnnotation value => value with
        {
            Color = color,
            FillColor = value.FillColor is { } fill
                ? DrawingColor.FromArgb(fill.A, color.R, color.G, color.B)
                : null
        },
        CircleShapeAnnotation value => value with
        {
            Color = color,
            FillColor = value.FillColor is { } fill
                ? DrawingColor.FromArgb(fill.A, color.R, color.G, color.B)
                : null
        },
        _ => annotation
    };

    private void SyncInlineColorFromSelection(Annotation annotation)
    {
        DrawingColor? color = annotation switch
        {
            ArrowAnnotation value => value.Color,
            CurvedArrowAnnotation value => value.Color,
            DrawStroke value => value.Color,
            HighlightAnnotation value => value.Color,
            StepNumberAnnotation value => value.Color,
            TextAnnotation value => value.Color,
            LineAnnotation value => value.Color,
            RectShapeAnnotation value => value.FillColor ?? value.Color,
            CircleShapeAnnotation value => value.FillColor ?? value.Color,
            _ => null
        };
        if (color is not { } selectedColor)
            return;

        _inlineColor = DrawingColor.FromArgb(255, selectedColor.R, selectedColor.G, selectedColor.B);
        ColorToolSwatch.Fill = ToMediaBrush(_inlineColor);
        if (annotation is HighlightAnnotation)
            _inlineHighlightOpacityPercent = (int)Math.Round(selectedColor.A / (double)byte.MaxValue * 100d);
        if (annotation is TextAnnotation text)
        {
            _inlineTextFontSize = text.FontSize;
            SyncInlineFontSizeControl(text.FontSize);
        }
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingInlineFontSizeControl || FontSizeBox.SelectedItem is not ComboBoxItem item)
            return;
        if (TryParseInlineFontSize(item.Content?.ToString(), out var fontSize))
            ApplyInlineFontSize(fontSize);
    }

    private void FontSizeBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        ApplyInlineFontSizeFromControl();
        EditorViewport.Focus();
    }

    private void FontSizeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => ApplyInlineFontSizeFromControl();

    private void ApplyInlineFontSizeFromControl()
    {
        if (_syncingInlineFontSizeControl || !TryParseInlineFontSize(FontSizeBox.Text, out var fontSize))
            return;
        ApplyInlineFontSize(fontSize);
    }

    private static bool TryParseInlineFontSize(string? value, out float fontSize)
    {
        bool parsed = float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out fontSize) ||
                      float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize);
        fontSize = Math.Clamp(fontSize, 6f, 200f);
        return parsed;
    }

    private void ApplyInlineFontSize(float fontSize)
    {
        fontSize = Math.Clamp(fontSize, 6f, 200f);
        _inlineTextFontSize = fontSize;
        SyncInlineFontSizeControl(fontSize);

        if (InlineTextEditor.Visibility == Visibility.Visible)
        {
            InlineTextEditor.FontSize = ToInlineWpfFontSize(fontSize);
            return;
        }

        if (_inlineSelectedAnnotation < 0 ||
            _inlineSelectedAnnotation >= _inlineAnnotations.Count ||
            _inlineAnnotations[_inlineSelectedAnnotation] is not TextAnnotation text)
        {
            return;
        }

        if (Math.Abs(text.FontSize - fontSize) < 0.01f)
            return;

        SnapshotInlineUndo();
        var resized = text with { FontSize = fontSize };
        _inlineAnnotations[_inlineSelectedAnnotation] = resized;
        _inlineSelectionOriginal = resized;
        RefreshPendingInlineAnnotations();
        ShowInlineSelection();
        SaveInlineEditor();
    }

    private void SyncInlineFontSizeControl(float fontSize)
    {
        _syncingInlineFontSizeControl = true;
        try
        {
            FontSizeBox.SelectedIndex = -1;
            FontSizeBox.Text = fontSize.ToString("0.#", CultureInfo.CurrentCulture);
        }
        finally
        {
            _syncingInlineFontSizeControl = false;
        }
    }

    private void HighlightOpacityPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } selected ||
            !int.TryParse(value, out var opacityPercent))
        {
            return;
        }

        _inlineHighlightOpacityPercent = Math.Clamp(opacityPercent, 0, 100);
        if (selected.Parent is MenuItem parent)
        {
            foreach (var sibling in parent.Items.OfType<MenuItem>())
                sibling.IsChecked = ReferenceEquals(sibling, selected);
        }
        ApplyOpacityToInlineHighlightSelection(_inlineHighlightOpacityPercent);
    }

    private void ApplyOpacityToInlineHighlightSelection(int opacityPercent)
    {
        if (_inlineSelectedAnnotation < 0 ||
            _inlineSelectedAnnotation >= _inlineAnnotations.Count ||
            _inlineAnnotations[_inlineSelectedAnnotation] is not HighlightAnnotation highlight)
        {
            return;
        }

        var updated = highlight with
        {
            Color = DrawingColor.FromArgb(
                (int)Math.Round(Math.Clamp(opacityPercent, 0, 100) / 100d * byte.MaxValue),
                highlight.Color.R,
                highlight.Color.G,
                highlight.Color.B)
        };
        if (Equals(updated, highlight))
            return;
        SnapshotInlineUndo();
        _inlineAnnotations[_inlineSelectedAnnotation] = updated;
        _inlineSelectionOriginal = updated;
        RefreshPendingInlineAnnotations();
        ShowInlineSelection();
        SaveInlineEditor();
    }

    private void ShapeBorderTool_Click(object sender, RoutedEventArgs e)
    {
        if (ShapeBorderToolButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ShapeBorderToolButton;
            menu.IsOpen = true;
        }
    }

    private void ShapeBorderPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } selected ||
            ShapeBorderToolButton.ContextMenu is not { } menu)
        {
            return;
        }

        foreach (var sibling in menu.Items.OfType<MenuItem>())
            sibling.IsChecked = ReferenceEquals(sibling, selected);

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            _inlineShapeBorderColor = null;
            ShapeBorderSwatch.Stroke = new SolidColorBrush(MediaColor.FromRgb(17, 24, 39));
            ShapeBorderSwatch.StrokeDashArray = [2, 2];
            return;
        }

        var media = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value);
        _inlineShapeBorderColor = DrawingColor.FromArgb(media.A, media.R, media.G, media.B);
        ShapeBorderSwatch.Stroke = new SolidColorBrush(media);
        ShapeBorderSwatch.StrokeDashArray = null;
    }

    private void EmojiTool_Click(object sender, RoutedEventArgs e)
    {
        SetInlineEditorTool(ScreenshotCaptureMode.Emoji);
        if (EmojiToolButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = EmojiToolButton;
            menu.IsOpen = true;
        }
    }

    private void EmojiPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string emoji })
            return;
        _inlineSelectedEmoji = emoji;
        SetInlineEditorTool(ScreenshotCaptureMode.Emoji);
    }

    private void UndoInlineEdit_Click(object sender, RoutedEventArgs e) => UndoInlineEdit();

    private void RedoInlineEdit_Click(object sender, RoutedEventArgs e) => RedoInlineEdit();

    private void DeleteInlineSelection_Click(object sender, RoutedEventArgs e) => DeleteInlineSelection();

    private void HistoryLibraryWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    RedoInlineEdit();
                else
                    UndoInlineEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Y)
            {
                RedoInlineEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                SaveInlineEditor();
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                e.Handled = true;
                if (CopyCurrentLibraryImage())
                    WindowState = WindowState.Minimized;
            }
            return;
        }

        var mode = e.Key switch
        {
            Key.D1 or Key.NumPad1 => ScreenshotCaptureMode.Select,
            Key.D2 or Key.NumPad2 => ScreenshotCaptureMode.Arrow,
            Key.D3 or Key.NumPad3 => ScreenshotCaptureMode.Draw,
            Key.D4 or Key.NumPad4 => ScreenshotCaptureMode.Highlight,
            Key.D5 or Key.NumPad5 or Key.T => ScreenshotCaptureMode.Text,
            Key.D6 or Key.NumPad6 => ScreenshotCaptureMode.Line,
            Key.D7 or Key.NumPad7 => ScreenshotCaptureMode.CurvedArrow,
            _ => (ScreenshotCaptureMode?)null
        };
        if (mode is not null)
        {
            SetInlineEditorTool(mode.Value);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteInlineSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (SaveInlineEditor())
            {
                e.Handled = true;
                WindowState = WindowState.Minimized;
            }
        }
    }

    private void EditorViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        bool allowOutside = SupportsInlineOutsideDrawing(_inlineTool);
        if (_inlineProject is null ||
            !TryGetInlineImagePoint(
                e.GetPosition(EditorViewport),
                out var imagePoint,
                allowOutside: allowOutside))
            return;

        _inlineAllowOutsideDrag = allowOutside;
        _inlineDraggingSelection = false;
        _inlineTextContentHit = false;

        if (_inlineTool == ScreenshotCaptureMode.Crop)
        {
            ClearInlineSelectionState();
            _inlineResizeHandle = InlineResizeHandle.None;
            _inlineDragStart = imagePoint;
            _inlineDragCurrent = imagePoint;
            _inlineDragging = true;
            EditorViewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (TryHitInlineResizeHandle(imagePoint, out var resizeHandle))
        {
            _inlineResizeHandle = resizeHandle;
            _inlineSelectionOriginal = _inlineAnnotations[_inlineSelectedAnnotation];
            _inlineDragStart = imagePoint;
            _inlineDragCurrent = imagePoint;
            _inlineDragging = true;
            _inlineDraggingSelection = true;
            EditorViewport.Cursor = GetInlineResizeCursor(resizeHandle);
            EditorViewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        int hitAnnotation = HitTestInlineAnnotation(imagePoint);
        // Existing objects can be moved without abandoning the active drawing
        // tool. A click selects; a drag moves; the next empty drag draws again.
        if (hitAnnotation >= 0)
        {
            _inlineTextContentHit = _inlineTool == ScreenshotCaptureMode.Text &&
                                    _inlineAnnotations[hitAnnotation] is TextAnnotation hitText &&
                                    IsInlineTextContentHit(hitText, imagePoint);
            bool extendSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (extendSelection && _inlineSelectedAnnotations.Contains(hitAnnotation))
            {
                _inlineSelectedAnnotations.Remove(hitAnnotation);
                _inlineSelectedAnnotation = _inlineSelectedAnnotations.LastOrDefault(-1);
                _inlineSelectionOriginal = _inlineSelectedAnnotation >= 0
                    ? _inlineAnnotations[_inlineSelectedAnnotation]
                    : null;
                _inlineResizeHandle = InlineResizeHandle.None;
                ShowInlineSelection();
                e.Handled = true;
                return;
            }

            if (extendSelection)
            {
                _inlineSelectedAnnotations.Add(hitAnnotation);
                _inlineSelectedAnnotation = hitAnnotation;
                _inlineSelectionOriginal = _inlineAnnotations[hitAnnotation];
            }
            else if (!_inlineSelectedAnnotations.Contains(hitAnnotation))
            {
                SelectOnlyInlineAnnotation(hitAnnotation);
            }
            else
            {
                _inlineSelectedAnnotation = hitAnnotation;
                _inlineSelectionOriginal = _inlineAnnotations[hitAnnotation];
            }
            SnapshotInlineSelectionForDrag();
            _inlineResizeHandle = InlineResizeHandle.None;
            _inlineDragStart = imagePoint;
            _inlineDragCurrent = imagePoint;
            _inlineDragging = true;
            _inlineDraggingSelection = true;
            UpdateInlineToolButtons();
            SyncInlineColorFromSelection(_inlineAnnotations[hitAnnotation]);
            EditorViewport.Cursor = WpfCursors.SizeAll;
            ShowInlineSelection();
            EditorViewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        RestoreInlineToolColor();

        if (_inlineTool == ScreenshotCaptureMode.Text)
        {
            _inlineDragStart = imagePoint;
            _inlineDragCurrent = imagePoint;
            _inlineDragging = true;
            EditorViewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_inlineTool == ScreenshotCaptureMode.StepNumber)
        {
            int number = _inlineAnnotations.OfType<StepNumberAnnotation>().Select(step => step.Number).DefaultIfEmpty(0).Max() + 1;
            SnapshotInlineUndo();
            _inlineAnnotations.Add(new StepNumberAnnotation(imagePoint, number, _inlineColor));
            SaveInlineEditor();
            e.Handled = true;
            return;
        }

        if (_inlineTool == ScreenshotCaptureMode.Magnifier)
        {
            int sourceSize = Math.Min(40, Math.Min(_inlineProject.BaseImage.Width, _inlineProject.BaseImage.Height));
            int sourceX = Math.Clamp(imagePoint.X - sourceSize / 2, 0, Math.Max(0, _inlineProject.BaseImage.Width - sourceSize));
            int sourceY = Math.Clamp(imagePoint.Y - sourceSize / 2, 0, Math.Max(0, _inlineProject.BaseImage.Height - sourceSize));
            SnapshotInlineUndo();
            _inlineAnnotations.Add(new MagnifierAnnotation(imagePoint, new DrawingRectangle(sourceX, sourceY, sourceSize, sourceSize)));
            SaveInlineEditor();
            e.Handled = true;
            return;
        }

        if (_inlineTool == ScreenshotCaptureMode.Emoji)
        {
            SnapshotInlineUndo();
            _inlineAnnotations.Add(new EmojiAnnotation(imagePoint, _inlineSelectedEmoji, 32f));
            SaveInlineEditor();
            e.Handled = true;
            return;
        }

        _inlineDragStart = imagePoint;
        _inlineDragCurrent = imagePoint;
        _inlinePoints.Clear();
        _inlinePoints.Add(imagePoint);
        _inlineDragging = true;
        _inlineDraggingSelection = false;

        if (_inlineTool == ScreenshotCaptureMode.Eraser)
            _inlineEraserColor = _inlineProject.BaseImage.GetPixel(imagePoint.X, imagePoint.Y);

        if (_inlineTool == ScreenshotCaptureMode.Select)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                ClearInlineSelectionState();
            _inlineResizeHandle = InlineResizeHandle.None;
            ShowInlineSelection();
        }

        EditorViewport.CaptureMouse();
        e.Handled = true;
    }

    private void EditorViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_inlineProject is null ||
            !TryGetInlineImagePoint(
                e.GetPosition(EditorViewport),
                out var imagePoint,
                clamp: _inlineDragging && !_inlineAllowOutsideDrag,
                allowOutside: _inlineAllowOutsideDrag))
            return;

        if (!_inlineDragging)
        {
            if (TryHitInlineResizeHandle(imagePoint, out var resizeHandle))
            {
                EditorViewport.Cursor = GetInlineResizeCursor(resizeHandle);
            }
            else
            {
                EditorViewport.Cursor = HitTestInlineAnnotation(imagePoint) >= 0
                    ? WpfCursors.SizeAll
                    : _inlineTool == ScreenshotCaptureMode.Text ? WpfCursors.IBeam : WpfCursors.Cross;
            }
            return;
        }

        _inlineDragCurrent = imagePoint;
        if (!_inlineDraggingSelection &&
            _inlineTool == ScreenshotCaptureMode.Arrow &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _inlineDragCurrent = ConstrainInlineArrowPoint(_inlineDragStart, _inlineDragCurrent);
        }
        if (_inlineTool is ScreenshotCaptureMode.Draw or ScreenshotCaptureMode.CurvedArrow)
        {
            var last = _inlinePoints[^1];
            int dx = imagePoint.X - last.X;
            int dy = imagePoint.Y - last.Y;
            if ((dx * dx) + (dy * dy) >= 4)
                _inlinePoints.Add(imagePoint);
        }

        ShowInlineDragPreview();
    }

    private void EditorViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_inlineDragging || _inlineProject is null)
            return;

        _inlineDragging = false;
        EditorViewport.ReleaseMouseCapture();
        if (TryGetInlineImagePoint(
                e.GetPosition(EditorViewport),
                out var imagePoint,
                clamp: !_inlineAllowOutsideDrag,
                allowOutside: _inlineAllowOutsideDrag))
            _inlineDragCurrent = imagePoint;
        if (!_inlineDraggingSelection &&
            _inlineTool == ScreenshotCaptureMode.Arrow &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _inlineDragCurrent = ConstrainInlineArrowPoint(_inlineDragStart, _inlineDragCurrent);
        }

        int selectionDx = _inlineDragCurrent.X - _inlineDragStart.X;
        int selectionDy = _inlineDragCurrent.Y - _inlineDragStart.Y;
        if (_inlineDraggingSelection &&
            _inlineTool == ScreenshotCaptureMode.Text &&
            _inlineSelectedAnnotations.Count == 1 &&
            _inlineSelectedAnnotation >= 0 &&
            _inlineSelectedAnnotation < _inlineAnnotations.Count &&
            _inlineAnnotations[_inlineSelectedAnnotation] is TextAnnotation &&
            _inlineTextContentHit &&
            Math.Abs(selectionDx) + Math.Abs(selectionDy) <= 2)
        {
            int textIndex = _inlineSelectedAnnotation;
            _inlineDraggingSelection = false;
            _inlineAllowOutsideDrag = false;
            ClearInlinePreview();
            BeginInlineTextEditing(_inlineDragStart, textIndex);
            e.Handled = true;
            return;
        }

        if (_inlineTool == ScreenshotCaptureMode.Crop && !_inlineDraggingSelection)
        {
            var cropRect = NormalizeInlineRect(_inlineDragStart, _inlineDragCurrent);
            _inlineAllowOutsideDrag = false;
            _inlineResizeHandle = InlineResizeHandle.None;
            ClearInlinePreview();
            ApplyInlineCrop(cropRect);
            e.Handled = true;
            return;
        }

        if (_inlineTool == ScreenshotCaptureMode.Text && !_inlineDraggingSelection)
        {
            var textRect = NormalizeInlineRect(_inlineDragStart, _inlineDragCurrent);
            if (textRect.Width < 12 || textRect.Height < 12)
            {
                int defaultWidth = Math.Max(120, (int)Math.Round(_inlineProject.BaseImage.Width * 0.30));
                textRect = new DrawingRectangle(
                    _inlineDragStart.X,
                    _inlineDragStart.Y,
                    Math.Min(defaultWidth, _inlineProject.BaseImage.Width - _inlineDragStart.X),
                    Math.Max(70, (int)Math.Round(_inlineProject.BaseImage.Height * 0.25)));
            }
            ClearInlinePreview();
            BeginInlineTextEditing(textRect.Location, -1, textRect.Width, textRect.Height);
            e.Handled = true;
            return;
        }

        bool changed = CompleteInlineDrag();
        _inlineDraggingSelection = false;
        _inlineAllowOutsideDrag = false;
        _inlineResizeHandle = InlineResizeHandle.None;
        ClearInlinePreview();
        if (changed)
        {
            SaveInlineEditor();
            ShowInlineSelection();
        }
        else
            ShowInlineSelection();
        e.Handled = true;
    }

    private bool CompleteInlineDrag()
    {
        int dx = _inlineDragCurrent.X - _inlineDragStart.X;
        int dy = _inlineDragCurrent.Y - _inlineDragStart.Y;
        var rect = NormalizeInlineRect(_inlineDragStart, _inlineDragCurrent);

        if (_inlineDraggingSelection && _inlineSelectedAnnotations.Count > 0)
        {
            if (dx == 0 && dy == 0)
                return false;
            if (_inlineResizeHandle != InlineResizeHandle.None &&
                _inlineSelectedAnnotations.Count == 1 &&
                _inlineSelectionOriginal is HighlightAnnotation highlight)
            {
                var resized = ResizeInlineHighlight(highlight.Rect, _inlineDragCurrent, _inlineResizeHandle);
                if (resized.Width < 3 || resized.Height < 3)
                    return false;
                SnapshotInlineUndo();
                var changedSelection = highlight with { Rect = resized };
                _inlineAnnotations[_inlineSelectedAnnotation] = changedSelection;
            }
            else if (_inlineResizeHandle == InlineResizeHandle.TextRight &&
                     _inlineSelectedAnnotations.Count == 1 &&
                     _inlineSelectionOriginal is TextAnnotation text)
            {
                var resized = ResizeInlineTextWidth(text, _inlineDragCurrent);
                if (resized.MaxWidth == text.MaxWidth)
                    return false;
                SnapshotInlineUndo();
                _inlineAnnotations[_inlineSelectedAnnotation] = resized;
            }
            else
            {
                if (_inlineSelectionOriginals.Count == 0)
                    SnapshotInlineSelectionForDrag();
                SnapshotInlineUndo();
                foreach (var (index, original) in _inlineSelectionOriginals)
                {
                    if (index >= 0 && index < _inlineAnnotations.Count)
                        _inlineAnnotations[index] = EditableScreenshotService.Translate(original, dx, dy);
                }
            }
            SnapshotInlineSelectionForDrag();
            return true;
        }

        switch (_inlineTool)
        {
            case ScreenshotCaptureMode.Arrow when Math.Abs(dx) + Math.Abs(dy) > 5:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new ArrowAnnotation(_inlineDragStart, _inlineDragCurrent, _inlineColor));
                return true;
            case ScreenshotCaptureMode.Line when Math.Abs(dx) + Math.Abs(dy) > 5:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new LineAnnotation(_inlineDragStart, _inlineDragCurrent, _inlineColor));
                return true;
            case ScreenshotCaptureMode.Ruler when Math.Abs(dx) + Math.Abs(dy) > 5:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new RulerAnnotation(_inlineDragStart, _inlineDragCurrent));
                return true;
            case ScreenshotCaptureMode.CurvedArrow when _inlinePoints.Count > 1:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new CurvedArrowAnnotation(_inlinePoints.ToList(), _inlineColor));
                return true;
            case ScreenshotCaptureMode.Draw when _inlinePoints.Count > 1:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new DrawStroke(_inlinePoints.ToList(), _inlineColor));
                return true;
            case ScreenshotCaptureMode.Highlight when rect.Width > 2 && rect.Height > 2:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new HighlightAnnotation(rect, DrawingColor.FromArgb(
                    (int)Math.Round(_inlineHighlightOpacityPercent / 100d * byte.MaxValue),
                    _inlineColor.R,
                    _inlineColor.G,
                    _inlineColor.B)));
                return true;
            case ScreenshotCaptureMode.Blur when rect.Width > 3 && rect.Height > 3:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new BlurRect(rect));
                return true;
            case ScreenshotCaptureMode.RectShape when rect.Width > 2 && rect.Height > 2:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new RectShapeAnnotation(
                    rect,
                    _inlineColor,
                    DrawingColor.FromArgb(51, _inlineColor.R, _inlineColor.G, _inlineColor.B),
                    _inlineShapeBorderColor));
                return true;
            case ScreenshotCaptureMode.CircleShape when rect.Width > 2 && rect.Height > 2:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new CircleShapeAnnotation(
                    rect,
                    _inlineColor,
                    DrawingColor.FromArgb(51, _inlineColor.R, _inlineColor.G, _inlineColor.B),
                    _inlineShapeBorderColor));
                return true;
            case ScreenshotCaptureMode.Eraser when rect.Width > 2 && rect.Height > 2:
                SnapshotInlineUndo();
                _inlineAnnotations.Add(new EraserFill(rect, _inlineEraserColor));
                return true;
            default:
                return false;
        }
    }

    private static DrawingPoint ConstrainInlineArrowPoint(DrawingPoint start, DrawingPoint current)
    {
        int dx = current.X - start.X;
        int dy = current.Y - start.Y;
        return Math.Abs(dx) >= Math.Abs(dy)
            ? new DrawingPoint(current.X, start.Y)
            : new DrawingPoint(start.X, current.Y);
    }

    private void ApplyInlineCrop(DrawingRectangle requestedCrop)
    {
        if (_inlineProject is null || string.IsNullOrWhiteSpace(_inlineProjectPath))
            return;

        var imageBounds = new DrawingRectangle(0, 0, _inlineProject.BaseImage.Width, _inlineProject.BaseImage.Height);
        var selection = DrawingRectangle.Intersect(requestedCrop, imageBounds);
        if (selection.Width < 2 || selection.Height < 2)
            return;

        if (_inlineCropMode == InlineCropMode.CutOutBand)
        {
            ApplyInlineCutOut(selection);
            return;
        }

        if (selection.Width < 10 || selection.Height < 10 || selection == imageBounds)
            return;

        Bitmap? croppedBase = null;
        try
        {
            croppedBase = new Bitmap(selection.Width, selection.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(croppedBase))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(
                    _inlineProject.BaseImage,
                    new DrawingRectangle(0, 0, selection.Width, selection.Height),
                    selection,
                    GraphicsUnit.Pixel);
            }

            var croppedBounds = new DrawingRectangle(0, 0, selection.Width, selection.Height);
            var translatedAnnotations = _inlineAnnotations
                .Select(annotation => EditableScreenshotService.Translate(annotation, -selection.Left, -selection.Top))
                .Where(annotation => GetInlineAnnotationBounds(annotation).IntersectsWith(croppedBounds))
                .ToList();
            CommitInlineBaseTransform(croppedBase, translatedAnnotations);
            croppedBase = null;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("library.inline-editor.crop", ex);
            ToastWindow.ShowError("Crop failed", ex.Message);
        }
        finally
        {
            croppedBase?.Dispose();
        }
    }

    private void ApplyInlineCutOut(DrawingRectangle selection)
    {
        if (_inlineProject is null)
            return;

        var band = GetInlineCutOutBand(selection);
        bool horizontal = band.Width >= band.Height;
        int newWidth = horizontal ? _inlineProject.BaseImage.Width : _inlineProject.BaseImage.Width - band.Width;
        int newHeight = horizontal ? _inlineProject.BaseImage.Height - band.Height : _inlineProject.BaseImage.Height;
        if (newWidth < 10 || newHeight < 10)
            return;

        Bitmap? result = null;
        try
        {
            result = new Bitmap(newWidth, newHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                if (horizontal)
                {
                    if (band.Top > 0)
                        graphics.DrawImage(_inlineProject.BaseImage, new DrawingRectangle(0, 0, newWidth, band.Top), new DrawingRectangle(0, 0, newWidth, band.Top), GraphicsUnit.Pixel);
                    int bottomHeight = _inlineProject.BaseImage.Height - band.Bottom;
                    if (bottomHeight > 0)
                        graphics.DrawImage(_inlineProject.BaseImage, new DrawingRectangle(0, band.Top, newWidth, bottomHeight), new DrawingRectangle(0, band.Bottom, newWidth, bottomHeight), GraphicsUnit.Pixel);
                }
                else
                {
                    if (band.Left > 0)
                        graphics.DrawImage(_inlineProject.BaseImage, new DrawingRectangle(0, 0, band.Left, newHeight), new DrawingRectangle(0, 0, band.Left, newHeight), GraphicsUnit.Pixel);
                    int rightWidth = _inlineProject.BaseImage.Width - band.Right;
                    if (rightWidth > 0)
                        graphics.DrawImage(_inlineProject.BaseImage, new DrawingRectangle(band.Left, 0, rightWidth, newHeight), new DrawingRectangle(band.Right, 0, rightWidth, newHeight), GraphicsUnit.Pixel);
                }
            }

            var annotations = new List<Annotation>();
            foreach (var annotation in _inlineAnnotations)
            {
                var bounds = GetInlineAnnotationBounds(annotation);
                if (bounds.IntersectsWith(band))
                    continue;
                if (horizontal && bounds.Top >= band.Bottom)
                    annotations.Add(EditableScreenshotService.Translate(annotation, 0, -band.Height));
                else if (!horizontal && bounds.Left >= band.Right)
                    annotations.Add(EditableScreenshotService.Translate(annotation, -band.Width, 0));
                else
                    annotations.Add(annotation);
            }

            CommitInlineBaseTransform(result, annotations);
            result = null;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("library.inline-editor.cut-out", ex);
            ToastWindow.ShowError("Cut out failed", ex.Message);
        }
        finally
        {
            result?.Dispose();
        }
    }

    private DrawingRectangle GetInlineCutOutBand(DrawingRectangle selection)
    {
        if (_inlineProject is null)
            return selection;
        return selection.Width >= selection.Height
            ? new DrawingRectangle(0, selection.Top, _inlineProject.BaseImage.Width, selection.Height)
            : new DrawingRectangle(selection.Left, 0, selection.Width, _inlineProject.BaseImage.Height);
    }

    private void CommitInlineBaseTransform(Bitmap transformedBase, List<Annotation> annotations)
    {
        if (_inlineProject is null || string.IsNullOrWhiteSpace(_inlineProjectPath))
            return;

        SnapshotInlineUndo(includeBaseImage: true);
        EditableScreenshotService.ReplaceProjectBase(_inlineProjectPath, transformedBase, annotations);
        _inlineQueuedSaveJob?.BaseImage.Dispose();
        _inlineQueuedSaveJob = null;
        _inlineProject.Dispose();
        _inlineProject = new EditableScreenshotProject(transformedBase, annotations, true);
        _inlineAnnotations.Clear();
        _inlineAnnotations.AddRange(annotations);
        _inlinePreviewAnnotations.Clear();
        ClearInlineSelectionState();
        UpdateInlineToolButtons();
        PreviewImage.Source = BitmapToBitmapSource(transformedBase);
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        RefreshPendingInlineAnnotations();
        SaveInlineEditor();
    }

    private void BeginInlineTextEditing(
        DrawingPoint point,
        int existingIndex = -1,
        int requestedWidth = 0,
        int requestedHeight = 0)
    {
        CommitInlineTextEditor(save: true);
        _inlineEditingTextIndex = existingIndex >= 0 ? existingIndex : HitTestInlineText(point);
        TextAnnotation? existing = _inlineEditingTextIndex >= 0
            ? (TextAnnotation)_inlineAnnotations[_inlineEditingTextIndex]
            : null;
        _inlineTextPosition = existing?.Pos ?? point;
        int defaultWidth = _inlineProject is null
            ? 240
            : Math.Max(120, (int)Math.Round(_inlineProject.BaseImage.Width * 0.30));
        _inlineTextMaxWidth = existing?.MaxWidth > 0
            ? existing.MaxWidth
            : requestedWidth > 0 ? requestedWidth : defaultWidth;
        if (_inlineProject is not null)
            _inlineTextMaxWidth = Math.Clamp(_inlineTextMaxWidth, 40, Math.Max(40, _inlineProject.BaseImage.Width - _inlineTextPosition.X));
        _inlineTextEditorHeight = requestedHeight > 0
            ? requestedHeight
            : _inlineProject is null ? 140 : Math.Max(70, (int)Math.Round(_inlineProject.BaseImage.Height * 0.25));
        _inlineTextFontSize = existing?.FontSize ?? _inlineTextFontSize;
        _inlineTextColor = existing?.Color ?? _inlineColor;
        _inlineTextBold = existing?.Bold ?? true;
        _inlineTextItalic = existing?.Italic ?? false;
        _inlineTextStroke = existing?.Stroke ?? false;
        _inlineTextShadow = existing?.Shadow ?? false;
        _inlineTextBackground = existing?.Background ?? false;
        _inlineTextFontFamily = existing?.FontFamily ?? "Segoe UI";
        InlineTextEditor.Text = existing?.Text ?? "";
        InlineTextEditor.FontSize = ToInlineWpfFontSize(_inlineTextFontSize);
        InlineTextEditor.FontFamily = new System.Windows.Media.FontFamily(_inlineTextFontFamily);
        InlineTextEditor.FontWeight = _inlineTextBold ? FontWeights.Bold : FontWeights.Normal;
        InlineTextEditor.FontStyle = _inlineTextItalic ? FontStyles.Italic : FontStyles.Normal;
        InlineTextEditor.Foreground = new SolidColorBrush(MediaColor.FromArgb(
            _inlineTextColor.A,
            _inlineTextColor.R,
            _inlineTextColor.G,
            _inlineTextColor.B));
        InlineTextEditor.TextAlignment = TextAlignment.Left;
        if (existing is not null)
        {
            SelectOnlyInlineAnnotation(_inlineEditingTextIndex);
        }
        else
        {
            ClearInlineSelectionState();
        }
        SyncInlineFontSizeControl(_inlineTextFontSize);
        if (existing is not null && _inlineProject is not null)
        {
            _inlineTextPreviousPreview = PreviewImage.Source;
            _inlineTextPreviousPreviewAnnotations = _inlinePreviewAnnotations.ToList();
            PreviewImage.Source = BitmapToBitmapSource(_inlineProject.BaseImage);
            _inlinePreviewAnnotations.Clear();
        }
        PositionInlineTextEditor(_inlineTextPosition, _inlineTextMaxWidth, _inlineTextEditorHeight);
        InlineTextEditor.Visibility = Visibility.Visible;
        UpdateInlineToolButtons();
        RefreshPendingInlineAnnotations();
        InlineTextEditor.Focus();
        InlineTextEditor.CaretIndex = InlineTextEditor.Text.Length;
        InlineTextEditor.Select(InlineTextEditor.CaretIndex, 0);
    }

    private void InlineTextEditor_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            CommitInlineTextEditor(save: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            InlineTextEditor.Visibility = Visibility.Collapsed;
            _inlineEditingTextIndex = -1;
            RestoreInlineTextPreview();
            EditorViewport.Focus();
        }
    }

    private void InlineTextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => CommitInlineTextEditor(save: true, focusViewport: false);

    private void CommitInlineTextEditor(bool save, bool focusViewport = true)
    {
        if (InlineTextEditor.Visibility != Visibility.Visible)
            return;

        var text = InlineTextEditor.Text;
        InlineTextEditor.Visibility = Visibility.Collapsed;
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var annotation = new TextAnnotation(
                _inlineTextPosition,
                text,
                _inlineTextFontSize,
                _inlineTextColor,
                _inlineTextBold,
                _inlineTextItalic,
                _inlineTextStroke,
                _inlineTextShadow,
                _inlineTextBackground,
                _inlineTextFontFamily,
                _inlineTextMaxWidth);
            if (_inlineEditingTextIndex >= 0 && _inlineEditingTextIndex < _inlineAnnotations.Count)
            {
                if (!_inlineAnnotations[_inlineEditingTextIndex].Equals(annotation))
                {
                    SnapshotInlineUndo();
                    _inlineAnnotations[_inlineEditingTextIndex] = annotation;
                    changed = true;
                }
            }
            else
            {
                SnapshotInlineUndo();
                _inlineAnnotations.Add(annotation);
                _inlineEditingTextIndex = _inlineAnnotations.Count - 1;
                changed = true;
            }
        }
        else if (_inlineEditingTextIndex >= 0 && _inlineEditingTextIndex < _inlineAnnotations.Count)
        {
            SnapshotInlineUndo();
            _inlineAnnotations.RemoveAt(_inlineEditingTextIndex);
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(text) &&
            _inlineEditingTextIndex >= 0 &&
            _inlineEditingTextIndex < _inlineAnnotations.Count)
        {
            SelectOnlyInlineAnnotation(_inlineEditingTextIndex);
        }
        else
        {
            ClearInlineSelectionState();
        }
        _inlineEditingTextIndex = -1;
        if (focusViewport)
            EditorViewport.Focus();
        if (changed && save)
        {
            _inlineTextPreviousPreview = null;
            _inlineTextPreviousPreviewAnnotations = null;
            SaveInlineEditor();
        }
        else
        {
            RestoreInlineTextPreview();
        }
    }

    private void RestoreInlineTextPreview()
    {
        if (_inlineTextPreviousPreview is null)
            return;
        PreviewImage.Source = _inlineTextPreviousPreview;
        _inlinePreviewAnnotations.Clear();
        if (_inlineTextPreviousPreviewAnnotations is not null)
            _inlinePreviewAnnotations.AddRange(_inlineTextPreviousPreviewAnnotations);
        _inlineTextPreviousPreview = null;
        _inlineTextPreviousPreviewAnnotations = null;
        RefreshPendingInlineAnnotations();
    }

    private void SnapshotInlineUndo(bool includeBaseImage = false)
    {
        _inlineUndoStack.Push(CaptureInlineSnapshot(includeBaseImage));
        ClearInlineHistoryStack(_inlineRedoStack);
        UpdateInlineToolButtons();
    }

    private InlineEditorSnapshot CaptureInlineSnapshot(bool includeBaseImage)
        => new(
            _inlineAnnotations.ToList(),
            includeBaseImage && _inlineProject is not null
                ? new Bitmap(_inlineProject.BaseImage)
                : null);

    private static void ClearInlineHistoryStack(Stack<InlineEditorSnapshot> stack)
    {
        while (stack.TryPop(out var snapshot))
            snapshot.Dispose();
    }

    private void UndoInlineEdit()
    {
        CommitInlineTextEditor(save: true);
        if (_inlineUndoStack.Count == 0 || _inlineProject is null)
            return;

        using var target = _inlineUndoStack.Pop();
        _inlineRedoStack.Push(CaptureInlineSnapshot(target.BaseImage is not null));
        ApplyInlineSnapshot(target);
    }

    private void RedoInlineEdit()
    {
        CommitInlineTextEditor(save: true);
        if (_inlineRedoStack.Count == 0 || _inlineProject is null)
            return;

        using var target = _inlineRedoStack.Pop();
        _inlineUndoStack.Push(CaptureInlineSnapshot(target.BaseImage is not null));
        ApplyInlineSnapshot(target);
    }

    private void ApplyInlineSnapshot(InlineEditorSnapshot snapshot)
    {
        if (_inlineProject is null)
            return;

        if (snapshot.BaseImage is not null && !string.IsNullOrWhiteSpace(_inlineProjectPath))
        {
            var restoredBase = new Bitmap(snapshot.BaseImage);
            EditableScreenshotService.ReplaceProjectBase(_inlineProjectPath, restoredBase, snapshot.Annotations);
            _inlineQueuedSaveJob?.BaseImage.Dispose();
            _inlineQueuedSaveJob = null;
            _inlineProject.Dispose();
            _inlineProject = new EditableScreenshotProject(restoredBase, snapshot.Annotations.ToList(), true);
            PreviewImage.Source = BitmapToBitmapSource(restoredBase);
            _inlinePreviewAnnotations.Clear();
        }

        _inlineAnnotations.Clear();
        _inlineAnnotations.AddRange(snapshot.Annotations);
        ClearInlineSelectionState();
        ClearInlinePreview();
        UpdateInlineToolButtons();
        RefreshPendingInlineAnnotations();
        SaveInlineEditor();
    }

    private void DeleteInlineSelection()
    {
        CommitInlineTextEditor(save: true);
        var selected = _inlineSelectedAnnotations
            .Where(index => index >= 0 && index < _inlineAnnotations.Count)
            .OrderByDescending(index => index)
            .ToArray();
        if (selected.Length == 0)
            return;

        SnapshotInlineUndo();
        foreach (int index in selected)
            _inlineAnnotations.RemoveAt(index);
        ClearInlineSelectionState();
        ClearInlinePreview();
        UpdateInlineToolButtons();
        SaveInlineEditor();
    }

    private bool SaveInlineEditor()
    {
        if (_inlineProject is null || FilmstripList.SelectedItem is not LibraryImageItem item)
            return false;

        Bitmap baseImage;
        try
        {
            baseImage = new Bitmap(_inlineProject.BaseImage);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("library.inline-editor.snapshot", ex);
            ToastWindow.ShowError("Save failed", ex.Message);
            return false;
        }

        _inlineQueuedSaveJob?.BaseImage.Dispose();
        _inlineQueuedSaveJob = new InlineSaveJob(
            baseImage,
            _inlineAnnotations.ToList(),
            item,
            item.Entry.FilePath);
        RefreshPendingInlineAnnotations();

        if (!_inlineSaveLoopRunning)
        {
            _inlineSaveLoopRunning = true;
            _ = ProcessInlineSaveQueueAsync();
        }
        return true;
    }

    private async Task ProcessInlineSaveQueueAsync()
    {
        try
        {
            while (_inlineQueuedSaveJob is { } job)
            {
                _inlineQueuedSaveJob = null;
                try
                {
                    var result = await RunInlineSaveOnStaThreadAsync(job);
                    ApplyInlineSaveResult(job, result);
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogError("library.inline-editor.save", ex);
                    if (!_closed)
                        ToastWindow.ShowError("Save failed", ex.Message);
                }
                finally
                {
                    job.BaseImage.Dispose();
                }
            }
        }
        finally
        {
            _inlineSaveLoopRunning = false;
            if (_inlineQueuedSaveJob is not null)
            {
                _inlineSaveLoopRunning = true;
                _ = ProcessInlineSaveQueueAsync();
            }
        }
    }

    private static Task<InlineSaveResult> RunInlineSaveOnStaThreadAsync(InlineSaveJob job)
    {
        var completion = new TaskCompletionSource<InlineSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var flattened = RegionOverlayForm.RenderEditorProject(job.BaseImage, job.Annotations, strokeShadow: false);
                EditableScreenshotService.SaveProject(job.FilePath, job.BaseImage, job.Annotations);
                EditableScreenshotService.SaveFlattenedImage(job.FilePath, flattened, 92);
                var clipboardPayload = ClipboardService.PrepareImageClipboardPayload(flattened, job.FilePath);
                ClipboardService.CopyPreparedImageToClipboard(flattened, clipboardPayload);
                completion.SetResult(new InlineSaveResult(
                    flattened.Width,
                    flattened.Height,
                    new FileInfo(job.FilePath).Length,
                    BitmapToBitmapSource(flattened, 220),
                    BitmapToBitmapSource(flattened)));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "OddSnap library save"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void ApplyInlineSaveResult(InlineSaveJob job, InlineSaveResult result)
    {
        job.Item.Entry.Width = result.Width;
        job.Item.Entry.Height = result.Height;
        job.Item.Entry.FileSizeBytes = result.FileSizeBytes;
        _suppressHistoryRefresh = true;
        try
        {
            _historyService.SaveEntry(job.Item.Entry);
        }
        finally
        {
            _suppressHistoryRefresh = false;
        }
        _imageSearchIndexService.NotifyHistoryMetadataChanged();
        job.Item.Thumbnail = result.Thumbnail;

        if (_closed ||
            FilmstripList.SelectedItem is not LibraryImageItem selected ||
            !string.Equals(selected.Entry.FilePath, job.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (InlineTextEditor.Visibility == Visibility.Visible)
        {
            _inlineTextPreviousPreview = result.Preview;
            _inlineTextPreviousPreviewAnnotations = job.Annotations.ToList();
            RefreshPendingInlineAnnotations();
            return;
        }

        PreviewImage.Source = result.Preview;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        _inlinePreviewAnnotations.Clear();
        _inlinePreviewAnnotations.AddRange(job.Annotations);
        RefreshPendingInlineAnnotations();
    }

    private sealed record InlineSaveJob(
        Bitmap BaseImage,
        List<Annotation> Annotations,
        LibraryImageItem Item,
        string FilePath);

    private sealed record InlineSaveResult(
        int Width,
        int Height,
        long FileSizeBytes,
        BitmapSource Thumbnail,
        BitmapSource Preview);

    private sealed record InlineEditorSnapshot(List<Annotation> Annotations, Bitmap? BaseImage) : IDisposable
    {
        public void Dispose() => BaseImage?.Dispose();
    }

    private static bool SupportsInlineOutsideDrawing(ScreenshotCaptureMode mode)
        => mode is ScreenshotCaptureMode.Arrow or
                   ScreenshotCaptureMode.Line or
                   ScreenshotCaptureMode.CurvedArrow or
                   ScreenshotCaptureMode.Draw or
                   ScreenshotCaptureMode.Crop;

    private bool TryGetInlineImagePoint(
        System.Windows.Point viewportPoint,
        out DrawingPoint imagePoint,
        bool clamp = false,
        bool allowOutside = false)
    {
        imagePoint = default;
        if (_inlineProject is null || EditorViewport.ActualWidth <= 0 || EditorViewport.ActualHeight <= 0)
            return false;

        var rect = GetInlineDisplayRect();
        double x = viewportPoint.X;
        double y = viewportPoint.Y;
        if (!rect.Contains(viewportPoint))
        {
            if (!clamp && !allowOutside)
                return false;
            if (clamp)
            {
                x = Math.Clamp(x, rect.Left, rect.Right - 0.01);
                y = Math.Clamp(y, rect.Top, rect.Bottom - 0.01);
            }
        }

        int px = (int)Math.Round((x - rect.Left) / rect.Width * _inlineProject.BaseImage.Width);
        int py = (int)Math.Round((y - rect.Top) / rect.Height * _inlineProject.BaseImage.Height);
        imagePoint = allowOutside && !clamp
            ? new DrawingPoint(px, py)
            : new DrawingPoint(
                Math.Clamp(px, 0, _inlineProject.BaseImage.Width - 1),
                Math.Clamp(py, 0, _inlineProject.BaseImage.Height - 1));
        return true;
    }

    private Rect GetInlineDisplayRect()
    {
        if (_inlineProject is null)
            return Rect.Empty;
        double scale = GetInlineDisplayScale();
        double width = _inlineProject.BaseImage.Width * scale;
        double height = _inlineProject.BaseImage.Height * scale;
        return new Rect(
            (EditorViewport.ActualWidth - width) / 2d,
            (EditorViewport.ActualHeight - height) / 2d,
            width,
            height);
    }

    private double GetInlineDisplayScale()
    {
        if (_inlineProject is null)
            return 1d;
        return Math.Min(
            EditorViewport.ActualWidth / _inlineProject.BaseImage.Width,
            EditorViewport.ActualHeight / _inlineProject.BaseImage.Height);
    }

    private double ToInlineWpfFontSize(float pointSize)
        => Math.Max(1d, pointSize * (96d / 72d) * GetInlineDisplayScale());

    private System.Windows.Point ToInlineViewportPoint(DrawingPoint point)
    {
        var rect = GetInlineDisplayRect();
        return new System.Windows.Point(
            rect.Left + (point.X / (double)_inlineProject!.BaseImage.Width * rect.Width),
            rect.Top + (point.Y / (double)_inlineProject.BaseImage.Height * rect.Height));
    }

    private void PositionInlineTextEditor(DrawingPoint point, int imageWidth, int imageHeight)
    {
        var screen = ToInlineViewportPoint(point);
        var bottomRight = ToInlineViewportPoint(new DrawingPoint(
            point.X + Math.Max(1, imageWidth),
            point.Y + Math.Max(1, imageHeight)));
        InlineTextEditor.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        InlineTextEditor.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        InlineTextEditor.Margin = new Thickness(screen.X, screen.Y, 0, 0);
        InlineTextEditor.Width = Math.Max(80, Math.Min(bottomRight.X - screen.X, EditorViewport.ActualWidth - screen.X - 8));
        InlineTextEditor.Height = Math.Max(42, Math.Min(bottomRight.Y - screen.Y, EditorViewport.ActualHeight - screen.Y - 8));
    }

    private void EditorViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshPendingInlineAnnotations();
        if (InlineTextEditor.Visibility == Visibility.Visible)
            PositionInlineTextEditor(_inlineTextPosition, _inlineTextMaxWidth, _inlineTextEditorHeight);
    }

    private void RefreshPendingInlineAnnotations()
    {
        PendingAnnotationCanvas.Children.Clear();
        if (_inlineProject is null)
            return;

        for (int i = 0; i < _inlineAnnotations.Count; i++)
        {
            if (InlineTextEditor.Visibility == Visibility.Visible && i == _inlineEditingTextIndex)
                continue;
            if (i < _inlinePreviewAnnotations.Count && Equals(_inlineAnnotations[i], _inlinePreviewAnnotations[i]))
                continue;
            AddPendingInlineAnnotation(_inlineAnnotations[i]);
        }
    }

    private void AddPendingInlineAnnotation(Annotation annotation)
    {
        var scale = GetInlineDisplayScale();
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                AddPendingLineWithArrow(arrow.From, arrow.To, arrow.Color, true);
                break;
            case LineAnnotation line:
                AddPendingLineWithArrow(line.From, line.To, line.Color, false);
                break;
            case RulerAnnotation ruler:
                AddPendingLineWithArrow(ruler.From, ruler.To, DrawingColor.FromArgb(255, 17, 24, 39), false);
                break;
            case CurvedArrowAnnotation curved when curved.Points.Count > 1:
                {
                    var polyline = new Polyline
                    {
                        Stroke = ToMediaBrush(curved.Color),
                        StrokeThickness = 4,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    foreach (var point in curved.Points)
                        polyline.Points.Add(ToInlineViewportPoint(point));
                    PendingAnnotationCanvas.Children.Add(polyline);
                    AddPendingArrowHead(
                        ToInlineViewportPoint(curved.Points[^2]),
                        ToInlineViewportPoint(curved.Points[^1]),
                        curved.Color);
                    break;
                }
            case DrawStroke draw when draw.Points.Count > 1:
                {
                    var polyline = new Polyline
                    {
                        Stroke = ToMediaBrush(draw.Color),
                        StrokeThickness = 4,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    foreach (var point in draw.Points)
                        polyline.Points.Add(ToInlineViewportPoint(point));
                    PendingAnnotationCanvas.Children.Add(polyline);
                    break;
                }
            case HighlightAnnotation highlight:
                AddPendingRect(highlight.Rect, ToMediaBrush(highlight.Color), null, 0, false);
                break;
            case BlurRect blur:
                AddPendingRect(blur.Rect, new SolidColorBrush(MediaColor.FromArgb(110, 156, 163, 175)), MediaBrushes.White, 1, false);
                break;
            case EraserFill eraser:
                AddPendingRect(eraser.Rect, ToMediaBrush(eraser.Color), null, 0, false);
                break;
            case RectShapeAnnotation rectangle:
                AddPendingRect(
                    rectangle.Rect,
                    rectangle.FillColor is { } rectFill ? ToMediaBrush(rectFill) : MediaBrushes.Transparent,
                    GetShapeStroke(rectangle.Color, rectangle.FillColor, rectangle.BorderColor),
                    3,
                    false);
                break;
            case CircleShapeAnnotation circle:
                AddPendingRect(
                    circle.Rect,
                    circle.FillColor is { } circleFill ? ToMediaBrush(circleFill) : MediaBrushes.Transparent,
                    GetShapeStroke(circle.Color, circle.FillColor, circle.BorderColor),
                    3,
                    true);
                break;
            case StepNumberAnnotation step:
                {
                    var center = ToInlineViewportPoint(step.Pos);
                    const double size = 30;
                    var badge = new Ellipse { Width = size, Height = size, Fill = ToMediaBrush(step.Color) };
                    Canvas.SetLeft(badge, center.X - size / 2);
                    Canvas.SetTop(badge, center.Y - size / 2);
                    PendingAnnotationCanvas.Children.Add(badge);
                    var label = new TextBlock
                    {
                        Text = step.Number.ToString(),
                        Foreground = MediaBrushes.White,
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Width = size,
                        TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(label, center.X - size / 2);
                    Canvas.SetTop(label, center.Y - 10);
                    PendingAnnotationCanvas.Children.Add(label);
                    break;
                }
            case TextAnnotation text:
                {
                    var pos = ToInlineViewportPoint(text.Pos);
                    var label = new TextBlock
                    {
                        Text = text.Text,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ToMediaBrush(text.Color),
                        FontFamily = new System.Windows.Media.FontFamily(text.FontFamily),
                        FontSize = Math.Max(1d, text.FontSize * (96d / 72d) * scale),
                        FontWeight = text.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = text.Italic ? FontStyles.Italic : FontStyles.Normal,
                        Background = text.Background ? ToMediaBrush(text.Color) : MediaBrushes.Transparent
                    };
                    if (text.MaxWidth > 0)
                        label.Width = Math.Max(40, text.MaxWidth * scale);
                    Canvas.SetLeft(label, pos.X);
                    Canvas.SetTop(label, pos.Y);
                    PendingAnnotationCanvas.Children.Add(label);
                    break;
                }
            case EmojiAnnotation emoji:
                {
                    var pos = ToInlineViewportPoint(emoji.Pos);
                    var label = new TextBlock { Text = emoji.Emoji, FontSize = Math.Max(16, emoji.Size * scale) };
                    Canvas.SetLeft(label, pos.X);
                    Canvas.SetTop(label, pos.Y);
                    PendingAnnotationCanvas.Children.Add(label);
                    break;
                }
        }
    }

    private void AddPendingLineWithArrow(DrawingPoint from, DrawingPoint to, DrawingColor color, bool arrowHead)
    {
        var start = ToInlineViewportPoint(from);
        var end = ToInlineViewportPoint(to);
        PendingAnnotationCanvas.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = ToMediaBrush(color),
            StrokeThickness = 2.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        if (arrowHead)
            AddPendingArrowHead(start, end, color);
    }

    private void AddPendingArrowHead(System.Windows.Point start, System.Windows.Point end, DrawingColor color)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return;
        double ux = dx / length;
        double uy = dy / length;
        const double headLength = 10;
        const double headWidth = 5;
        var basePoint = new System.Windows.Point(end.X - ux * headLength, end.Y - uy * headLength);
        var normal = new System.Windows.Vector(-uy * headWidth, ux * headWidth);
        PendingAnnotationCanvas.Children.Add(new Polygon
        {
            Fill = ToMediaBrush(color),
            Points = new PointCollection
            {
                end,
                basePoint + normal,
                basePoint - normal
            }
        });
    }

    private void AddPendingRect(DrawingRectangle rectangle, MediaBrush fill, MediaBrush? stroke, double strokeThickness, bool ellipse)
    {
        var topLeft = ToInlineViewportPoint(rectangle.Location);
        var bottomRight = ToInlineViewportPoint(new DrawingPoint(rectangle.Right, rectangle.Bottom));
        Shape shape = ellipse
            ? new Ellipse()
            : new System.Windows.Shapes.Rectangle { RadiusX = 3, RadiusY = 3 };
        shape.Width = Math.Max(1, bottomRight.X - topLeft.X);
        shape.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
        shape.Fill = fill;
        shape.Stroke = stroke;
        shape.StrokeThickness = stroke is null ? 0 : strokeThickness;
        Canvas.SetLeft(shape, topLeft.X);
        Canvas.SetTop(shape, topLeft.Y);
        PendingAnnotationCanvas.Children.Add(shape);
    }

    private static MediaBrush? GetShapeStroke(DrawingColor legacyColor, DrawingColor? fillColor, DrawingColor? borderColor)
        => fillColor.HasValue
            ? borderColor is { } border ? ToMediaBrush(border) : null
            : ToMediaBrush(legacyColor);

    private static SolidColorBrush ToMediaBrush(DrawingColor color)
        => new(MediaColor.FromArgb(color.A, color.R, color.G, color.B));

    private void ShowInlineDragPreview()
    {
        ClearInlinePreview();
        if (!_inlineDragging || _inlineProject is null)
            return;

        var previewStart = _inlineDragStart;
        var previewEnd = _inlineDragCurrent;
        if (_inlineTool == ScreenshotCaptureMode.Crop && _inlineCropMode == InlineCropMode.CutOutBand)
        {
            var imageBounds = new DrawingRectangle(0, 0, _inlineProject.BaseImage.Width, _inlineProject.BaseImage.Height);
            var selection = DrawingRectangle.Intersect(NormalizeInlineRect(previewStart, previewEnd), imageBounds);
            if (selection.Width > 0 && selection.Height > 0)
            {
                var band = GetInlineCutOutBand(selection);
                previewStart = band.Location;
                previewEnd = new DrawingPoint(band.Right, band.Bottom);
            }
        }
        var from = ToInlineViewportPoint(previewStart);
        var to = ToInlineViewportPoint(previewEnd);
        var brush = new SolidColorBrush(MediaColor.FromArgb(_inlineColor.A, _inlineColor.R, _inlineColor.G, _inlineColor.B));
        if (_inlineDraggingSelection)
        {
            if (_inlineResizeHandle == InlineResizeHandle.TextRight &&
                _inlineSelectionOriginal is TextAnnotation text)
            {
                ShowInlineSelection(GetInlineTextFrameBounds(ResizeInlineTextWidth(text, _inlineDragCurrent)));
            }
            else if (_inlineResizeHandle != InlineResizeHandle.None &&
                _inlineSelectedAnnotations.Count == 1 &&
                _inlineSelectionOriginal is HighlightAnnotation highlight)
            {
                ShowInlineSelection(ResizeInlineHighlight(highlight.Rect, _inlineDragCurrent, _inlineResizeHandle));
            }
            else
            {
                ShowInlineSelection(dx: _inlineDragCurrent.X - _inlineDragStart.X, dy: _inlineDragCurrent.Y - _inlineDragStart.Y);
            }
            return;
        }

        if (_inlineTool is ScreenshotCaptureMode.Draw or ScreenshotCaptureMode.CurvedArrow)
        {
            var polyline = new Polyline { Stroke = brush, StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round };
            foreach (var point in _inlinePoints)
                polyline.Points.Add(ToInlineViewportPoint(point));
            EditorCanvas.Children.Add(polyline);
            return;
        }

        double left = Math.Min(from.X, to.X);
        double top = Math.Min(from.Y, to.Y);
        double width = Math.Abs(to.X - from.X);
        double height = Math.Abs(to.Y - from.Y);
        var shapeFill = new SolidColorBrush(MediaColor.FromArgb(51, _inlineColor.R, _inlineColor.G, _inlineColor.B));
        var shapeBorder = _inlineShapeBorderColor is { } borderColor ? ToMediaBrush(borderColor) : null;
        Shape shape = _inlineTool switch
        {
            ScreenshotCaptureMode.Highlight => new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(MediaColor.FromArgb(
                    (byte)Math.Round(_inlineHighlightOpacityPercent / 100d * byte.MaxValue),
                    _inlineColor.R,
                    _inlineColor.G,
                    _inlineColor.B))
            },
            ScreenshotCaptureMode.Blur => new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(MediaColor.FromArgb(90, 180, 180, 180)), Stroke = MediaBrushes.White, StrokeDashArray = [3, 2] },
            ScreenshotCaptureMode.Crop => new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(MediaColor.FromArgb(30, 250, 204, 21)),
                Stroke = new SolidColorBrush(MediaColor.FromRgb(250, 204, 21)),
                StrokeThickness = 2,
                StrokeDashArray = [5, 3]
            },
            ScreenshotCaptureMode.Text => new System.Windows.Shapes.Rectangle { Stroke = brush, StrokeThickness = 2, StrokeDashArray = [4, 3], Fill = new SolidColorBrush(MediaColor.FromArgb(28, _inlineColor.R, _inlineColor.G, _inlineColor.B)) },
            ScreenshotCaptureMode.RectShape => new System.Windows.Shapes.Rectangle { Fill = shapeFill, Stroke = shapeBorder, StrokeThickness = shapeBorder is null ? 0 : 3 },
            ScreenshotCaptureMode.CircleShape => new Ellipse { Fill = shapeFill, Stroke = shapeBorder, StrokeThickness = shapeBorder is null ? 0 : 3 },
            ScreenshotCaptureMode.Eraser => new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(MediaColor.FromArgb(150, _inlineEraserColor.R, _inlineEraserColor.G, _inlineEraserColor.B)), Stroke = MediaBrushes.White, StrokeDashArray = [3, 2] },
            _ => new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = brush, StrokeThickness = 2.6 }
        };
        if (shape is not Line)
        {
            Canvas.SetLeft(shape, left);
            Canvas.SetTop(shape, top);
            shape.Width = Math.Max(1, width);
            shape.Height = Math.Max(1, height);
        }
        EditorCanvas.Children.Add(shape);
    }

    private void ShowInlineSelection(DrawingRectangle? overrideBounds = null, int dx = 0, int dy = 0)
    {
        ClearInlinePreview();
        if (_inlineSelectedAnnotations.Count == 0)
            return;

        foreach (int index in _inlineSelectedAnnotations.OrderBy(index => index))
        {
            if (index < 0 || index >= _inlineAnnotations.Count)
                continue;
            var selected = _inlineAnnotations[index];
            if (ShowInlinePathSelection(selected, dx, dy))
                continue;
            var bounds = index == _inlineSelectedAnnotation && overrideBounds is not null
                ? overrideBounds.Value
                : GetInlineAnnotationBounds(selected);
            if (!(index == _inlineSelectedAnnotation && overrideBounds is not null))
                bounds.Offset(dx, dy);
            var topLeft = ToInlineViewportPoint(new DrawingPoint(bounds.Left, bounds.Top));
            var bottomRight = ToInlineViewportPoint(new DrawingPoint(bounds.Right, bounds.Bottom));
            var selection = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(4, bottomRight.X - topLeft.X),
                Height = Math.Max(4, bottomRight.Y - topLeft.Y),
                Stroke = new SolidColorBrush(MediaColor.FromRgb(96, 165, 250)),
                StrokeThickness = 2,
                StrokeDashArray = [3, 2],
                Fill = MediaBrushes.Transparent
            };
            Canvas.SetLeft(selection, topLeft.X);
            Canvas.SetTop(selection, topLeft.Y);
            EditorCanvas.Children.Add(selection);

            if (_inlineSelectedAnnotations.Count == 1 && selected is HighlightAnnotation)
            {
                AddInlineResizeHandle(topLeft.X, topLeft.Y);
                AddInlineResizeHandle(bottomRight.X, topLeft.Y);
                AddInlineResizeHandle(topLeft.X, bottomRight.Y);
                AddInlineResizeHandle(bottomRight.X, bottomRight.Y);
            }
            else if (_inlineSelectedAnnotations.Count == 1 && selected is TextAnnotation)
            {
                AddInlineResizeHandle(bottomRight.X, (topLeft.Y + bottomRight.Y) / 2d);
            }
        }
    }

    private bool ShowInlinePathSelection(Annotation selected, int dx, int dy)
    {
        IReadOnlyList<DrawingPoint>? points = selected switch
        {
            ArrowAnnotation value => [value.From, value.To],
            LineAnnotation value => [value.From, value.To],
            RulerAnnotation value => [value.From, value.To],
            CurvedArrowAnnotation value => value.Points,
            DrawStroke value => value.Points,
            _ => null
        };
        if (points is null || points.Count < 2)
            return false;

        var outline = new Polyline
        {
            Stroke = new SolidColorBrush(MediaColor.FromArgb(210, 96, 165, 250)),
            StrokeThickness = 3,
            StrokeDashArray = [3, 2],
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        foreach (var point in points)
        {
            outline.Points.Add(ToInlineViewportPoint(new DrawingPoint(point.X + dx, point.Y + dy)));
        }
        EditorCanvas.Children.Add(outline);

        AddInlinePathEndpoint(outline.Points[0]);
        AddInlinePathEndpoint(outline.Points[^1]);
        return true;
    }

    private void AddInlinePathEndpoint(System.Windows.Point point)
    {
        const double size = 7;
        var handle = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = MediaBrushes.White,
            Stroke = new SolidColorBrush(MediaColor.FromRgb(37, 99, 235)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(handle, point.X - size / 2d);
        Canvas.SetTop(handle, point.Y - size / 2d);
        EditorCanvas.Children.Add(handle);
    }

    private void AddInlineResizeHandle(double x, double y)
    {
        const double size = 10;
        var handle = new System.Windows.Shapes.Rectangle
        {
            Width = size,
            Height = size,
            Fill = MediaBrushes.White,
            Stroke = new SolidColorBrush(MediaColor.FromRgb(37, 99, 235)),
            StrokeThickness = 2
        };
        Canvas.SetLeft(handle, x - size / 2d);
        Canvas.SetTop(handle, y - size / 2d);
        EditorCanvas.Children.Add(handle);
    }

    private bool TryHitInlineResizeHandle(DrawingPoint point, out InlineResizeHandle handle)
    {
        handle = InlineResizeHandle.None;
        if (_inlineSelectedAnnotations.Count != 1 ||
            _inlineSelectedAnnotation < 0 ||
            _inlineSelectedAnnotation >= _inlineAnnotations.Count)
        {
            return false;
        }

        int tolerance = Math.Max(6, (int)Math.Ceiling(8d / Math.Max(0.05, GetInlineDisplayScale())));
        if (_inlineAnnotations[_inlineSelectedAnnotation] is TextAnnotation text)
        {
            var bounds = GetInlineTextFrameBounds(text);
            var rightMiddle = new DrawingPoint(bounds.Right, bounds.Top + bounds.Height / 2);
            if (Math.Abs(point.X - rightMiddle.X) <= tolerance &&
                Math.Abs(point.Y - rightMiddle.Y) <= tolerance)
            {
                handle = InlineResizeHandle.TextRight;
                return true;
            }
            return false;
        }

        if (_inlineAnnotations[_inlineSelectedAnnotation] is not HighlightAnnotation highlight)
            return false;
        var corners = new (InlineResizeHandle Handle, DrawingPoint Point)[]
        {
            (InlineResizeHandle.TopLeft, new DrawingPoint(highlight.Rect.Left, highlight.Rect.Top)),
            (InlineResizeHandle.TopRight, new DrawingPoint(highlight.Rect.Right, highlight.Rect.Top)),
            (InlineResizeHandle.BottomLeft, new DrawingPoint(highlight.Rect.Left, highlight.Rect.Bottom)),
            (InlineResizeHandle.BottomRight, new DrawingPoint(highlight.Rect.Right, highlight.Rect.Bottom))
        };
        foreach (var candidate in corners)
        {
            if (Math.Abs(point.X - candidate.Point.X) <= tolerance &&
                Math.Abs(point.Y - candidate.Point.Y) <= tolerance)
            {
                handle = candidate.Handle;
                return true;
            }
        }
        return false;
    }

    private static System.Windows.Input.Cursor GetInlineResizeCursor(InlineResizeHandle handle) => handle switch
    {
        InlineResizeHandle.TopLeft or InlineResizeHandle.BottomRight => WpfCursors.SizeNWSE,
        InlineResizeHandle.TopRight or InlineResizeHandle.BottomLeft => WpfCursors.SizeNESW,
        InlineResizeHandle.TextRight => WpfCursors.SizeWE,
        _ => WpfCursors.SizeAll
    };

    private TextAnnotation ResizeInlineTextWidth(TextAnnotation text, DrawingPoint draggedPoint)
    {
        int maximumWidth = _inlineProject is null
            ? int.MaxValue
            : Math.Max(40, _inlineProject.BaseImage.Width - text.Pos.X);
        int width = Math.Clamp(draggedPoint.X - text.Pos.X, 40, maximumWidth);
        return text with { MaxWidth = width };
    }

    private DrawingRectangle ResizeInlineHighlight(
        DrawingRectangle original,
        DrawingPoint draggedPoint,
        InlineResizeHandle handle)
    {
        int left = handle is InlineResizeHandle.TopLeft or InlineResizeHandle.BottomLeft
            ? draggedPoint.X
            : original.Left;
        int right = handle is InlineResizeHandle.TopRight or InlineResizeHandle.BottomRight
            ? draggedPoint.X
            : original.Right;
        int top = handle is InlineResizeHandle.TopLeft or InlineResizeHandle.TopRight
            ? draggedPoint.Y
            : original.Top;
        int bottom = handle is InlineResizeHandle.BottomLeft or InlineResizeHandle.BottomRight
            ? draggedPoint.Y
            : original.Bottom;

        if (_inlineProject is not null)
        {
            left = Math.Clamp(left, 0, _inlineProject.BaseImage.Width);
            right = Math.Clamp(right, 0, _inlineProject.BaseImage.Width);
            top = Math.Clamp(top, 0, _inlineProject.BaseImage.Height);
            bottom = Math.Clamp(bottom, 0, _inlineProject.BaseImage.Height);
        }
        return DrawingRectangle.FromLTRB(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Max(left, right),
            Math.Max(top, bottom));
    }

    private void ClearInlinePreview() => EditorCanvas.Children.Clear();

    private int HitTestInlineAnnotation(DrawingPoint point)
    {
        int tolerance = Math.Max(3, (int)Math.Ceiling(5d / Math.Max(0.05, GetInlineDisplayScale())));
        for (int i = _inlineAnnotations.Count - 1; i >= 0; i--)
        {
            if (IsInlineAnnotationHit(_inlineAnnotations[i], point, tolerance))
                return i;
        }
        return -1;
    }

    private static bool IsInlineAnnotationHit(Annotation annotation, DrawingPoint point, int tolerance)
    {
        double toleranceSquared = tolerance * tolerance;
        return annotation switch
        {
            ArrowAnnotation value => DistanceToSegmentSquared(point, value.From, value.To) <= toleranceSquared,
            LineAnnotation value => DistanceToSegmentSquared(point, value.From, value.To) <= toleranceSquared,
            RulerAnnotation value => DistanceToSegmentSquared(point, value.From, value.To) <= toleranceSquared,
            CurvedArrowAnnotation value => IsPointNearPath(point, value.Points, toleranceSquared),
            DrawStroke value => IsPointNearPath(point, value.Points, toleranceSquared),
            TextAnnotation value => IsInlineTextContentHit(value, point) ||
                                    IsPointNearInlineRectangleBorder(point, GetInlineTextFrameBounds(value), tolerance),
            _ => Inflate(GetInlineAnnotationBounds(annotation), tolerance).Contains(point)
        };
    }

    private static bool IsPointNearInlineRectangleBorder(DrawingPoint point, DrawingRectangle bounds, int tolerance)
    {
        var outer = Inflate(bounds, tolerance);
        if (!outer.Contains(point))
            return false;
        var inner = Inflate(bounds, -tolerance);
        return inner.Width <= 0 || inner.Height <= 0 || !inner.Contains(point);
    }

    private static bool IsPointNearPath(DrawingPoint point, IReadOnlyList<DrawingPoint> path, double toleranceSquared)
    {
        for (int i = 1; i < path.Count; i++)
            if (DistanceToSegmentSquared(point, path[i - 1], path[i]) <= toleranceSquared)
                return true;
        return false;
    }

    private static double DistanceToSegmentSquared(DrawingPoint point, DrawingPoint start, DrawingPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
        {
            double px = point.X - start.X;
            double py = point.Y - start.Y;
            return px * px + py * py;
        }

        double t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy), 0d, 1d);
        double nearestX = start.X + t * dx;
        double nearestY = start.Y + t * dy;
        double offsetX = point.X - nearestX;
        double offsetY = point.Y - nearestY;
        return offsetX * offsetX + offsetY * offsetY;
    }

    private static DrawingRectangle Inflate(DrawingRectangle rectangle, int amount)
    {
        rectangle.Inflate(amount, amount);
        return rectangle;
    }

    private int HitTestInlineText(DrawingPoint point)
    {
        for (int i = _inlineAnnotations.Count - 1; i >= 0; i--)
            if (_inlineAnnotations[i] is TextAnnotation text && IsInlineTextContentHit(text, point))
                return i;
        return -1;
    }

    private static DrawingRectangle GetInlineAnnotationBounds(Annotation annotation) => annotation switch
    {
        ArrowAnnotation value => BoundsFromPoints(value.From, value.To, 10),
        LineAnnotation value => BoundsFromPoints(value.From, value.To, 8),
        CurvedArrowAnnotation value => BoundsFromPointList(value.Points, 10),
        DrawStroke value => BoundsFromPointList(value.Points, 8),
        HighlightAnnotation value => value.Rect,
        BlurRect value => value.Rect,
        EraserFill value => value.Rect,
        RectShapeAnnotation value => value.Rect,
        CircleShapeAnnotation value => value.Rect,
        RulerAnnotation value => BoundsFromPoints(value.From, value.To, 12),
        StepNumberAnnotation value => new DrawingRectangle(value.Pos.X - 18, value.Pos.Y - 18, 36, 36),
        TextAnnotation value => GetInlineTextFrameBounds(value),
        MagnifierAnnotation value => new DrawingRectangle(value.Pos.X - 64, value.Pos.Y - 64, 128, 128),
        EmojiAnnotation value => new DrawingRectangle(value.Pos.X, value.Pos.Y, (int)value.Size, (int)value.Size),
        _ => DrawingRectangle.Empty
    };

    private static bool IsInlineTextContentHit(TextAnnotation text, DrawingPoint point)
        => MeasureInlineTextContent(text).Contains(point);

    private static DrawingRectangle GetInlineTextFrameBounds(TextAnnotation text)
    {
        var content = MeasureInlineTextContent(text);
        int width = text.MaxWidth > 0 ? text.MaxWidth + 10 : content.Width;
        return new DrawingRectangle(text.Pos.X - 5, text.Pos.Y - 4, width, content.Height);
    }

    private static DrawingRectangle MeasureInlineTextContent(TextAnnotation text)
    {
        using var font = new Font(text.FontFamily, text.FontSize, text.Bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
        if (text.MaxWidth > 0)
        {
            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            var size = graphics.MeasureString(text.Text, font, text.MaxWidth);
            int contentWidth = text.Background
                ? text.MaxWidth
                : Math.Min(text.MaxWidth, Math.Max(1, (int)Math.Ceiling(size.Width)));
            return new DrawingRectangle(text.Pos.X - 5, text.Pos.Y - 4, contentWidth + 10, (int)Math.Ceiling(size.Height) + 8);
        }
        var singleLine = System.Windows.Forms.TextRenderer.MeasureText(text.Text, font);
        return new DrawingRectangle(text.Pos.X - 5, text.Pos.Y - 4, singleLine.Width + 10, singleLine.Height + 8);
    }

    private static DrawingRectangle BoundsFromPoints(DrawingPoint first, DrawingPoint second, int padding)
        => DrawingRectangle.FromLTRB(
            Math.Min(first.X, second.X) - padding,
            Math.Min(first.Y, second.Y) - padding,
            Math.Max(first.X, second.X) + padding,
            Math.Max(first.Y, second.Y) + padding);

    private static DrawingRectangle BoundsFromPointList(IReadOnlyList<DrawingPoint> points, int padding)
    {
        if (points.Count == 0) return DrawingRectangle.Empty;
        return DrawingRectangle.FromLTRB(
            points.Min(point => point.X) - padding,
            points.Min(point => point.Y) - padding,
            points.Max(point => point.X) + padding,
            points.Max(point => point.Y) + padding);
    }

    private static DrawingRectangle NormalizeInlineRect(DrawingPoint first, DrawingPoint second)
        => new(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));

    private static BitmapSource BitmapToBitmapSource(Bitmap bitmap, int decodePixelWidth = 0)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
            image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
