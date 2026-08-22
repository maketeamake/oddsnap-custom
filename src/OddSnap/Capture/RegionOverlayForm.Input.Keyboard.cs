using System.Drawing;
using System.Windows.Forms;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    // ProcessCmdKey always receives ESC (OnKeyDown sometimes doesn't)
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_isTyping && (keyData & Keys.KeyCode) == Keys.Enter)
        {
            if ((keyData & Keys.Control) != Keys.None)
            {
                CommitText();
                if (_editorMode)
                    RequestEditorSave();
            }
            else
            {
                InsertTextLineBreak();
            }
            return true;
        }
        if (_isTyping && HandleTextNavigationKey(keyData))
            return true;
        if (_editorMode && keyData == (Keys.Control | Keys.S))
        {
            RequestEditorSave();
            return true;
        }
        if (_editorMode && !_isTyping && keyData == Keys.Enter)
        {
            RequestEditorSave();
            return true;
        }
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            Cancel();
            return true;
        }
        if (_flyoutOpen && TryHandleAnnotationToolHotkey(keyData & Keys.KeyCode))
        {
            CloseMoreToolsDropdown();
            RefreshToolbar();
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            Cancel();
            return;
        }

        // Undo must work in all states (emoji placing, typing, etc.)
        if (e.KeyCode == Keys.Z && e.Control && _undoStack.Count > 0)
        {
            CancelActivePointerInteraction();
            var last = RemoveLastAnnotation();
            _redoStack.Add(last);
            // Update step counter when undoing a step number
            if (last is StepNumberAnnotation)
            {
                var remaining = _undoStack.OfType<StepNumberAnnotation>().LastOrDefault();
                _nextStepNumber = remaining != null ? remaining.Number + 1 : 1;
            }
            if (_selectedAnnotationIndex >= _undoStack.Count)
                _selectedAnnotationIndex = -1;
            Invalidate(InflateForRepaint(GetAnnotationBounds(last)));
            NotifyEditorContentChanged();
            return;
        }

        if ((e.KeyCode == Keys.Y && e.Control || e.KeyCode == Keys.Z && e.Control && e.Shift) && _redoStack.Count > 0)
        {
            CancelActivePointerInteraction();
            var annotation = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            RestoreAnnotation(annotation);
            if (annotation is StepNumberAnnotation step)
                _nextStepNumber = Math.Max(_nextStepNumber, step.Number + 1);
            Invalidate(InflateForRepaint(GetAnnotationBounds(annotation)));
            NotifyEditorContentChanged();
            return;
        }

        // All search/text input is handled by off-screen TextBoxes
        if (_emojiPickerOpen) return;

        // Emoji placing: Tab re-opens picker
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji)
        {
            if (e.KeyCode == Keys.Tab) { _emojiPickerOpen = true; _isPlacingEmoji = false; ShowEmojiSearchBox(); QueueEmojiWarmup(); RefreshToolbar(); }
            return;
        }

        if (_fontPickerOpen) return;
        if (_isTyping) return;
        if (TryHandleAnnotationToolHotkey(e.KeyCode))
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            RefreshToolbar();
            Invalidate();
            return;
        }

        // Delete selected annotation
        if (e.KeyCode == Keys.Delete && _mode == CaptureMode.Select && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            var bounds = InflateForRepaint(GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]));
            CancelActivePointerInteraction();
            _undoStack.RemoveAt(_selectedAnnotationIndex);
            _redoStack.Clear();
            MarkCommittedAnnotationsDirty();
            _selectedAnnotationIndex = -1;
            _selectPreviewAnnotation = null;
            _renderSkipIndex = -1;
            _isSelectDragging = false;
            _isSelectResizing = false;
            Invalidate(bounds);
            NotifyEditorContentChanged();
            return;
        }
    }

    private bool TryHandleAnnotationToolHotkey(Keys keyCode)
    {
        uint mod = 0;
        if ((ModifierKeys & Keys.Control) != 0) mod |= Native.User32.MOD_CONTROL;
        if ((ModifierKeys & Keys.Alt) != 0) mod |= Native.User32.MOD_ALT;
        if ((ModifierKeys & Keys.Shift) != 0) mod |= Native.User32.MOD_SHIFT;
        uint vk = unchecked((uint)(keyCode & Keys.KeyCode));

        if (!_annotationHotkeysByChord.TryGetValue((mod, vk), out var tool) || tool.Mode is null)
            return false;

        SetTool(tool);
        return true;
    }
}
