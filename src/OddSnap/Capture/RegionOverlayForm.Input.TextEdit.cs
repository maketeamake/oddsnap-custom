using System.Drawing;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    // All text input is handled by off-screen TextBox controls

    private readonly record struct TextLineLayout(string Text, int Start, int BreakLength);

    private void CommitText()
    {
        // Sync from TextBox before committing
        if (_textBox != null && _textBox.Visible)
            _textBuffer = _textBox.Text;
        if (_isTyping && _textBuffer.Length > 0)
            AddAnnotation(new TextAnnotation(_textPos, _textBuffer, _textFontSize, _toolColor, _textBold, _textItalic, _textStroke, _textShadow, _textBackground, _textFontFamily));
        _isTyping = false;
        SetSnapGuides(false, false);
        _textBuffer = "";
        InvalidateActiveTextLayout();
        _fontPickerOpen = false;
        HideTextBox();
        RefreshOverlayUiChrome();
        Invalidate();
    }

    private static RectangleF MeasureTextRect(Point pos, string text, float fontSize, string fontFamily, bool bold, bool italic, bool background = false)
    {
        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(fontFamily, fontSize, style);
        string display = text.Length > 0 ? text : "Type here...";
        var lines = GetTextLines(display);
        float width = lines.Max(line => MeasureTextLineWidth(line.Text, line.Text.Length, font));
        float height = Math.Max(1f, lines.Count * GetTextLineHeight(font));

        int padX = background ? 16 : 8;
        int padY = background ? 12 : 8;
        return new RectangleF(
            pos.X - (padX / 2f),
            pos.Y - (padY / 2f),
            Math.Max(1f, width) + padX,
            height + padY);
    }

    private RectangleF GetActiveTextRect()
    {
        if (!_isTyping) return RectangleF.Empty;
        if (_activeTextLayoutDirty)
        {
            var style = FontStyle.Regular;
            if (_textBold) style |= FontStyle.Bold;
            if (_textItalic) style |= FontStyle.Italic;
            var font = GetAnnotationFont(_textFontFamily, _textFontSize, style);
            _activeTextRectCache = MeasureTextRect(_textPos, _textBuffer, _textFontSize, _textFontFamily, _textBold, _textItalic, _textBackground);
            _activeTextHandleCache[0] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.X, _activeTextRectCache.Y));
            _activeTextHandleCache[1] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.Right, _activeTextRectCache.Y));
            _activeTextHandleCache[2] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.X, _activeTextRectCache.Bottom));
            _activeTextHandleCache[3] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.Right, _activeTextRectCache.Bottom));
            _activeTextLayoutDirty = false;
        }
        return _activeTextRectCache;
    }

    private int GetTextHandle(Point p)
    {
        if (!_isTyping) return -1;
        _ = GetActiveTextRect();
        for (int i = 0; i < _activeTextHandleCache.Length; i++)
        {
            var h = Rectangle.Round(_activeTextHandleCache[i]);
            h.Inflate((WindowsHandleRenderer.HitSize - h.Width) / 2, (WindowsHandleRenderer.HitSize - h.Height) / 2);
            if (h.Contains(p)) return i;
        }
        return -1;
    }

    private static bool IsTextMoveGrip(Point point, RectangleF bounds)
    {
        var outer = bounds;
        outer.Inflate(5f, 5f);
        if (!outer.Contains(point))
            return false;

        var inner = bounds;
        inner.Inflate(-6f, -6f);
        return inner.Width <= 0 || inner.Height <= 0 || !inner.Contains(point);
    }

    private List<TextAnnotation> GetTextAnnotations() =>
        _undoStack.OfType<TextAnnotation>().ToList();

    private void BeginEditingTextAnnotation(TextAnnotation annotation, Point? caretPoint = null)
    {
        RemoveAnnotation(annotation);
        _isTyping = true;
        _textPos = annotation.Pos;
        _textBuffer = annotation.Text;
        _textFontSize = annotation.FontSize;
        _toolColor = annotation.Color;
        _textBold = annotation.Bold;
        _textItalic = annotation.Italic;
        _textStroke = annotation.Stroke;
        _textShadow = annotation.Shadow;
        _textBackground = annotation.Background;
        _textFontFamily = annotation.FontFamily;
        InvalidateActiveTextLayout();
        ShowTextBox();
        if (caretPoint.HasValue)
            SetTextCaretFromPoint(caretPoint.Value);
    }

    private int HitTestText(Point p)
    {
        var texts = GetTextAnnotations();
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var ta = texts[i];
            var rect = MeasureTextRect(ta.Pos, ta.Text, ta.FontSize, ta.FontFamily, ta.Bold, ta.Italic, ta.Background);
            if (rect.Contains(p)) return i;
        }
        return -1;
    }

    private static List<TextLineLayout> GetTextLines(string text)
    {
        var lines = new List<TextLineLayout>();
        int lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
                continue;

            int breakLength = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            lines.Add(new TextLineLayout(text[lineStart..i], lineStart, breakLength));
            i += breakLength - 1;
            lineStart = i + 1;
        }

        lines.Add(new TextLineLayout(text[lineStart..], lineStart, 0));
        return lines;
    }

    private static float GetTextLineHeight(Font font)
    {
        var size = TextRenderer.MeasureText("Ag", font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return Math.Max(1f, size.Height);
    }

    private static float MeasureTextLineWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0f;

        length = Math.Min(length, text.Length);
        var size = TextRenderer.MeasureText(text[..length], font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return size.Width;
    }

    private int GetTextCharIndexAt(Point p)
    {
        if (!_isTyping)
            return 0;

        var style = FontStyle.Regular;
        if (_textBold) style |= FontStyle.Bold;
        if (_textItalic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(_textFontFamily, _textFontSize, style);
        string text = _textBuffer;
        var lines = GetTextLines(text);
        float lineHeight = GetTextLineHeight(font);
        int lineIndex = Math.Clamp((int)Math.Floor((p.Y - _textPos.Y) / lineHeight), 0, lines.Count - 1);
        var line = lines[lineIndex];
        float x = p.X - _textPos.X;
        if (x <= 0 || line.Text.Length == 0)
            return line.Start;

        for (int i = 1; i <= line.Text.Length; i++)
        {
            float width = MeasureTextLineWidth(line.Text, i, font);
            float prevWidth = MeasureTextLineWidth(line.Text, i - 1, font);
            if (x <= ((prevWidth + width) / 2f))
                return line.Start + i - 1;
        }

        return line.Start + line.Text.Length;
    }

    private PointF GetTextCaretPoint(int index, Font font)
    {
        var lines = GetTextLines(_textBuffer);
        index = Math.Clamp(index, 0, _textBuffer.Length);
        float lineHeight = GetTextLineHeight(font);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int lineEnd = line.Start + line.Text.Length;
            if (index <= lineEnd || i == lines.Count - 1)
            {
                int offset = Math.Clamp(index - line.Start, 0, line.Text.Length);
                return new PointF(
                    _textPos.X + MeasureTextLineWidth(line.Text, offset, font),
                    _textPos.Y + (i * lineHeight));
            }
        }

        return _textPos;
    }

    private void SetTextCaretFromPoint(Point point)
    {
        if (_textBox is null)
            return;

        int index = GetTextCharIndexAt(point);
        _textBox.SelectionStart = index;
        _textBox.SelectionLength = 0;
        _textSelectionAnchor = index;
        _textCaretIndex = index;
        _textBox.Focus();
        Invalidate(InflateForRepaint(Rectangle.Round(GetActiveTextRect()), 16));
    }

    private void StartTextSelection(Point point)
    {
        SetTextCaretFromPoint(point);
        _textSelecting = true;
    }

    private void SelectTextWordAt(Point point)
    {
        if (_textBox is null || _textBuffer.Length == 0)
            return;

        int index = Math.Clamp(GetTextCharIndexAt(point), 0, _textBuffer.Length);
        if (index == _textBuffer.Length && index > 0)
            index--;

        bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_';
        if (!IsWordChar(_textBuffer[index]))
        {
            _textBox.SelectionStart = index;
            _textBox.SelectionLength = 1;
        }
        else
        {
            int start = index;
            int end = index + 1;
            while (start > 0 && IsWordChar(_textBuffer[start - 1])) start--;
            while (end < _textBuffer.Length && IsWordChar(_textBuffer[end])) end++;
        _textBox.SelectionStart = start;
        _textBox.SelectionLength = end - start;
        _textSelectionAnchor = start;
        _textCaretIndex = end;
        }

        _textBox.Focus();
        Invalidate(InflateForRepaint(Rectangle.Round(GetActiveTextRect()), 16));
    }

    private void InsertTextLineBreak()
    {
        if (!_isTyping || _textBox is null)
            return;

        _textBox.SelectedText = "\n";
        _textSelectionAnchor = _textBox.SelectionStart;
        _textCaretIndex = _textSelectionAnchor;
        _textBox.Focus();
    }

    private bool HandleTextNavigationKey(Keys keyData)
    {
        if (_textBox is null)
            return false;

        var key = keyData & Keys.KeyCode;
        bool control = (keyData & Keys.Control) != 0;
        bool shift = (keyData & Keys.Shift) != 0;
        if (control && key == Keys.A)
        {
            _textSelectionAnchor = 0;
            _textCaretIndex = _textBuffer.Length;
            ApplyTrackedTextSelection();
            return true;
        }

        if (key is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End))
            return false;

        int caret = Math.Clamp(_textCaretIndex, 0, _textBuffer.Length);
        if (!shift && _textBox.SelectionLength > 0 && !control && key is Keys.Left or Keys.Right)
        {
            caret = key == Keys.Left
                ? _textBox.SelectionStart
                : _textBox.SelectionStart + _textBox.SelectionLength;
        }
        else
        {
            caret = key switch
            {
                Keys.Left when control => FindPreviousWordBoundary(caret),
                Keys.Right when control => FindNextWordBoundary(caret),
                Keys.Left => Math.Max(0, caret - 1),
                Keys.Right => Math.Min(_textBuffer.Length, caret + 1),
                Keys.Up when control => 0,
                Keys.Down when control => _textBuffer.Length,
                Keys.Up => FindVerticalCaret(caret, -1),
                Keys.Down => FindVerticalCaret(caret, 1),
                Keys.Home when control => 0,
                Keys.End when control => _textBuffer.Length,
                Keys.Home => FindCurrentLineStart(caret),
                Keys.End => FindCurrentLineEnd(caret),
                _ => caret
            };
        }

        if (!shift)
            _textSelectionAnchor = caret;
        _textCaretIndex = caret;
        ApplyTrackedTextSelection();
        return true;
    }

    private int FindVerticalCaret(int index, int direction)
    {
        var style = FontStyle.Regular;
        if (_textBold) style |= FontStyle.Bold;
        if (_textItalic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(_textFontFamily, _textFontSize, style);
        var lines = GetTextLines(_textBuffer);
        float lineHeight = GetTextLineHeight(font);
        var caretPoint = GetTextCaretPoint(index, font);
        int currentLine = Math.Clamp((int)Math.Floor((caretPoint.Y - _textPos.Y) / lineHeight), 0, lines.Count - 1);
        int targetLine = Math.Clamp(currentLine + direction, 0, lines.Count - 1);
        if (targetLine == currentLine)
            return direction < 0 ? 0 : _textBuffer.Length;

        return GetTextCharIndexAt(new Point(
            (int)Math.Round(caretPoint.X),
            (int)Math.Round(_textPos.Y + (targetLine * lineHeight) + (lineHeight / 2f))));
    }

    private void ApplyTrackedTextSelection()
    {
        if (_textBox is null)
            return;

        int start = Math.Min(_textSelectionAnchor, _textCaretIndex);
        int end = Math.Max(_textSelectionAnchor, _textCaretIndex);
        _textBox.SelectionStart = start;
        _textBox.SelectionLength = end - start;
        _textBox.Focus();
        Invalidate(InflateForRepaint(Rectangle.Round(GetActiveTextRect()), 16));
    }

    private int FindPreviousWordBoundary(int index)
    {
        index = Math.Clamp(index, 0, _textBuffer.Length);
        while (index > 0 && char.IsWhiteSpace(_textBuffer[index - 1])) index--;
        while (index > 0 && !char.IsWhiteSpace(_textBuffer[index - 1])) index--;
        return index;
    }

    private int FindNextWordBoundary(int index)
    {
        index = Math.Clamp(index, 0, _textBuffer.Length);
        while (index < _textBuffer.Length && !char.IsWhiteSpace(_textBuffer[index])) index++;
        while (index < _textBuffer.Length && char.IsWhiteSpace(_textBuffer[index])) index++;
        return index;
    }

    private int FindCurrentLineStart(int index)
    {
        index = Math.Clamp(index, 0, _textBuffer.Length);
        int previous = index > 0 ? _textBuffer.LastIndexOf('\n', index - 1) : -1;
        return previous + 1;
    }

    private int FindCurrentLineEnd(int index)
    {
        index = Math.Clamp(index, 0, _textBuffer.Length);
        int next = _textBuffer.IndexOf('\n', index);
        return next < 0 ? _textBuffer.Length : next;
    }

    private void ToggleColorPicker()
    {
        _emojiPickerOpen = false;
        _fontPickerOpen = false;
        HideEmojiSearchBox();
        HideFontSearchBox();
        _isPlacingEmoji = false;
        _colorPickerOpen = !_colorPickerOpen;
        Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
        RefreshToolbar();
    }

    private bool HandleColorPickerClick(Point p)
    {
        if (!_colorPickerRect.Contains(p)) return false;

        for (int i = 0; i < ToolColors.Length; i++)
        {
            if (!GetColorPickerSwatchRect(i).Contains(p))
                continue;

            SetToolColor(ToolColors[i]);
            _activeToolId = _visibleTools.FirstOrDefault(t => t.Mode == _mode)?.Id ?? _activeToolId;
            if (_mode != CaptureMode.Highlight)
                _colorPickerOpen = false;
            Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
            RefreshToolbar();
            return true;
        }

        if (_mode == CaptureMode.Highlight)
        {
            for (int i = 0; i < HighlightOpacityPresets.Length; i++)
            {
                if (!GetHighlightOpacityPresetRect(i).Contains(p))
                    continue;

                SetHighlightOpacity(HighlightOpacityPresets[i]);
                Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
                RefreshToolbar();
                return true;
            }
        }

        return true; // absorb clicks inside the popup even between swatches
    }

    private bool HandleFontPickerClick(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;

        int itemH = 30, pad = 8, searchBarH = 32;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int relY = p.Y - listY;
        var fonts = GetFilteredFonts();
        int visibleCount = 8;
        int maxScroll = Math.Max(0, fonts.Length - visibleCount);
        int trackH = visibleCount * itemH - 8;
        int trackX = _fontPickerRect.Right - pad - 4;
        int trackY = listY + 4;
        var trackRect = new Rectangle(trackX - 4, trackY, 12, trackH);
        if (trackRect.Contains(p) && fonts.Length > visibleCount)
        {
            int thumbH = Math.Max(12, trackH * visibleCount / fonts.Length);
            int thumbTravel = Math.Max(1, trackH - thumbH);
            int target = p.Y - trackY - (thumbH / 2);
            target = Math.Clamp(target, 0, thumbTravel);
            _fontPickerScroll = (int)Math.Round((double)target / thumbTravel * maxScroll);
            RefreshToolbar();
            return true;
        }

        int idx = _fontPickerScroll + relY / itemH;

        if (relY >= 0 && idx >= 0 && idx < fonts.Length)
        {
            var oldTextRect = Rectangle.Round(GetActiveTextRect());
            var oldToolbarRect = Rectangle.Round(GetTextToolbarBounds());
            var oldPickerRect = InflateForRepaint(GetFontPickerBounds(), 12);
            _textFontFamily = fonts[idx];
            _fontPickerOpen = false;
            _fontSearch = ""; _filteredFonts = null;
            InvalidateActiveTextLayout();
            UpdateTextBoxStyle(); SyncTextBoxSize();
            var newTextRect = Rectangle.Round(GetActiveTextRect());
            var newToolbarRect = Rectangle.Round(GetTextToolbarBounds());
            RefreshOverlayUiChrome();
            Invalidate(Rectangle.Union(
                Rectangle.Union(InflateForRepaint(oldTextRect, 16), InflateForRepaint(newTextRect, 16)),
                Rectangle.Union(Rectangle.Union(InflateForRepaint(oldToolbarRect, 16), InflateForRepaint(newToolbarRect, 16)), oldPickerRect)));
            RefreshToolbar();
            return true;
        }
        return true; // absorb click inside picker
    }

    private bool IsPointInFontPickerSearch(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;
        int searchBarH = 32, pad = 8;
        int searchBottom = _fontPickerRect.Y + pad + searchBarH;
        return p.Y < searchBottom;
    }

    private bool IsPointInFontPickerScrollbar(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;
        var fonts = GetFilteredFonts();
        int visibleCount = 8;
        if (fonts.Length <= visibleCount) return false;

        int itemH = 30, pad = 8, searchBarH = 32;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int trackH = visibleCount * itemH - 8;
        int trackX = _fontPickerRect.Right - pad - 4;
        int trackY = listY + 4;
        var trackRect = new Rectangle(trackX - 4, trackY, 12, trackH);
        return trackRect.Contains(p);
    }

    private bool IsPointInFontPickerList(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;
        int itemH = 30, pad = 8, searchBarH = 32;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int relY = p.Y - listY;
        int idx = _fontPickerScroll + relY / itemH;
        return relY >= 0 && idx >= 0 && idx < GetFilteredFonts().Length;
    }

    private bool IsPointInEmojiPickerSearch(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;
        int pad = EmojiPickerPadding, searchBarH = EmojiPickerSearchBarHeight;
        int searchBottom = _emojiPickerRect.Y + pad + searchBarH + pad;
        return p.Y < searchBottom;
    }

    private bool IsPointInEmojiPickerItem(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;

        var filtered = GetFilteredEmojiPalette();
        int cols = EmojiPickerColumns, emojiSize = EmojiPickerIconSize, pad = EmojiPickerPadding;
        int searchBarH = EmojiPickerSearchBarHeight;
        int gridY = _emojiPickerRect.Y + pad + searchBarH + pad;
        int relX = p.X - _emojiPickerRect.X - pad;
        int relY = p.Y - gridY;
        int col = relX / (emojiSize + pad);
        int row = relY / (emojiSize + pad);
        int idx = (_emojiScrollOffset + row) * cols + col;
        return col >= 0 && col < cols && row >= 0 && idx >= 0 && idx < filtered.Length;
    }

    private bool IsPointInColorPickerOption(Point p)
    {
        if (!_colorPickerRect.Contains(p)) return false;
        for (int i = 0; i < ToolColors.Length; i++)
            if (GetColorPickerSwatchRect(i).Contains(p))
                return true;
        if (_mode == CaptureMode.Highlight)
            for (int i = 0; i < HighlightOpacityPresets.Length; i++)
                if (GetHighlightOpacityPresetRect(i).Contains(p))
                    return true;
        return false;
    }

    private bool HandleEmojiPickerClick(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;

        var filtered = GetFilteredEmojiPalette();

        int cols = EmojiPickerColumns, emojiSize = EmojiPickerIconSize, pad = EmojiPickerPadding;
        int searchBarH = EmojiPickerSearchBarHeight;
        int gridY = _emojiPickerRect.Y + pad + searchBarH + pad;

        // Check if clicking in search bar area (just keep focus, absorb click)
        if (p.Y < gridY) return true;

        int relX = p.X - _emojiPickerRect.X - pad;
        int relY = p.Y - gridY;
        int col = relX / (emojiSize + pad);
        int row = relY / (emojiSize + pad);
        int idx = (_emojiScrollOffset + row) * cols + col;

        if (col >= 0 && col < cols && row >= 0 && idx < filtered.Length)
        {
            _selectedEmoji = filtered[idx].emoji;
            _isPlacingEmoji = true;
            _emojiPickerOpen = false;
            _fontPickerOpen = false;
            HideEmojiSearchBox();
            Invalidate(InflateForRepaint(GetEmojiPickerBounds(), 12));
            RefreshToolbar();
            return true;
        }
        return true; // absorb click inside picker
    }
}
