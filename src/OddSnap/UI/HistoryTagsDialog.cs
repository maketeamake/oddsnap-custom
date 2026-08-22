using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OddSnap.Helpers;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfThickness = System.Windows.Thickness;

namespace OddSnap.UI;

internal sealed class HistoryTagsDialog : Window
{
    private readonly TextBox _input;

    private HistoryTagsDialog(string? currentTags)
    {
        Theme.Refresh();
        Title = "Edit capture tags";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new WpfFontFamily(UiChrome.PreferredFamilyName);
        Foreground = Theme.Brush(Theme.TextPrimary);

        _input = new TextBox
        {
            Text = currentTags ?? "",
            FontSize = 13,
            Padding = new WpfThickness(10, 7, 10, 7),
            Background = Theme.Brush(Theme.SettingsInputBg),
            Foreground = Theme.Brush(Theme.TextPrimary),
            BorderBrush = Theme.Brush(Theme.SettingsInputBorder),
            BorderThickness = new Thickness(1),
            CaretBrush = Theme.Brush(Theme.TextPrimary)
        };

        Content = BuildContent();
        OddSnapUiCaptureVisibility.Track(this);
        UiScale.ApplyToWindow(this, (FrameworkElement)Content, scaleWindowBounds: false);

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.CaretIndex = _input.Text.Length;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SaveAndClose();
            }
        };
    }

    public string Tags => _input.Text;

    public static bool TryEdit(Window? owner, string? currentTags, out string tags)
    {
        var dialog = new HistoryTagsDialog(currentTags);
        if (owner is { IsVisible: true })
            dialog.Owner = owner;

        var saved = dialog.ShowDialog() == true;
        tags = saved ? dialog.Tags : currentTags ?? "";
        return saved;
    }

    private FrameworkElement BuildContent()
    {
        var shell = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Theme.Brush(Theme.SurfaceWindowBackground),
            BorderBrush = Theme.Brush(Theme.SettingsWindowBorder),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = Theme.IsDark ? 0.42 : 0.18
            }
        };

        var root = new StackPanel { Margin = new WpfThickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Tags",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new WpfThickness(0, 0, 0, 5)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Separate tags with commas, for example: client, Excel, invoice.",
            FontSize = 11.5,
            Opacity = 0.55,
            Margin = new WpfThickness(0, 0, 0, 12)
        });
        root.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new WpfThickness(0, 16, 0, 0)
        };
        buttons.Children.Add(BuildButton("Cancel", false, Close));
        buttons.Children.Add(BuildButton("Save", true, SaveAndClose));
        root.Children.Add(buttons);

        shell.Child = root;
        return shell;
    }

    private static Button BuildButton(string text, bool primary, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 82,
            Padding = new WpfThickness(14, 7, 14, 7),
            Margin = new WpfThickness(8, 0, 0, 0),
            Cursor = WpfCursors.Hand,
            Background = primary ? Theme.Brush(Theme.Accent) : Theme.Brush(Theme.SettingsCardBg),
            Foreground = primary
                ? (Theme.IsDark ? WpfBrushes.Black : WpfBrushes.White)
                : Theme.Brush(Theme.TextPrimary),
            BorderBrush = Theme.Brush(Theme.SettingsInputBorder),
            BorderThickness = new WpfThickness(1)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void SaveAndClose()
    {
        DialogResult = true;
        Close();
    }
}
