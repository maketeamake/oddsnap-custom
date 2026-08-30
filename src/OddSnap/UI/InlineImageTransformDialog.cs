using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Grid = System.Windows.Controls.Grid;
using Panel = System.Windows.Controls.Panel;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace OddSnap.UI;

internal readonly record struct InlineImageResizeOptions(int Width, int Height, bool Smooth);

internal static class InlineImageTransformDialog
{
    public static bool TryGetResize(
        Window owner,
        int originalWidth,
        int originalHeight,
        out InlineImageResizeOptions options)
    {
        options = default;
        var window = CreateWindow(owner, "Resize Image", 410);
        var root = new StackPanel { Margin = new Thickness(18) };
        window.Content = root;

        root.Children.Add(Header("Scale"));
        var percentageMode = new RadioButton { Content = "Scale by percentage", IsChecked = true, Margin = new Thickness(0, 7, 0, 5) };
        var pixelMode = new RadioButton { Content = "Scale to specific size (pixels)", Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(percentageMode);
        root.Children.Add(pixelMode);

        var widthBox = NumberRow(root, "Width", "100");
        var heightBox = NumberRow(root, "Height", "100");
        var units = new TextBlock { Text = "%", Margin = new Thickness(0, -62, 14, 38), HorizontalAlignment = WpfHorizontalAlignment.Right };
        root.Children.Add(units);

        var smooth = new CheckBox { Content = "Use smooth scaling", IsChecked = true, Margin = new Thickness(0, 4, 0, 5) };
        var aspect = new CheckBox { Content = "Keep aspect ratio", IsChecked = true, Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(smooth);
        root.Children.Add(aspect);
        root.Children.Add(Header("Size setting summary"));
        var summary = new TextBlock { Margin = new Thickness(0, 8, 0, 14), LineHeight = 20 };
        root.Children.Add(summary);

        bool confirmed = false;
        bool syncing = false;
        (int Width, int Height) Calculate()
        {
            if (percentageMode.IsChecked == true)
            {
                double.TryParse(widthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double widthPercent);
                double.TryParse(heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double heightPercent);
                return (
                    Math.Clamp((int)Math.Round(originalWidth * Math.Clamp(widthPercent, 1, 1000) / 100d), 1, 30000),
                    Math.Clamp((int)Math.Round(originalHeight * Math.Clamp(heightPercent, 1, 1000) / 100d), 1, 30000));
            }
            int.TryParse(widthBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int width);
            int.TryParse(heightBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int height);
            return (Math.Clamp(width, 1, 30000), Math.Clamp(height, 1, 30000));
        }
        void UpdateSummary()
        {
            var final = Calculate();
            summary.Text = $"Original size:   {originalWidth} × {originalHeight}\nFinal size:        {final.Width} × {final.Height}";
        }
        void SyncAspect(TextBox changed, TextBox other)
        {
            if (syncing || aspect.IsChecked != true)
                return;
            syncing = true;
            try
            {
                if (percentageMode.IsChecked == true)
                    other.Text = changed.Text;
                else if (int.TryParse(changed.Text, out int value))
                    other.Text = ReferenceEquals(changed, widthBox)
                        ? Math.Max(1, (int)Math.Round(value * originalHeight / (double)originalWidth)).ToString(CultureInfo.CurrentCulture)
                        : Math.Max(1, (int)Math.Round(value * originalWidth / (double)originalHeight)).ToString(CultureInfo.CurrentCulture);
            }
            finally { syncing = false; }
            UpdateSummary();
        }
        widthBox.TextChanged += (_, _) => SyncAspect(widthBox, heightBox);
        heightBox.TextChanged += (_, _) => SyncAspect(heightBox, widthBox);
        percentageMode.Checked += (_, _) =>
        {
            if (widthBox is null) return;
            syncing = true;
            widthBox.Text = "100";
            heightBox.Text = "100";
            units.Text = "%";
            syncing = false;
            UpdateSummary();
        };
        pixelMode.Checked += (_, _) =>
        {
            syncing = true;
            widthBox.Text = originalWidth.ToString(CultureInfo.CurrentCulture);
            heightBox.Text = originalHeight.ToString(CultureInfo.CurrentCulture);
            units.Text = "px";
            syncing = false;
            UpdateSummary();
        };

        var buttons = ButtonRow("Resize", () => { confirmed = true; window.DialogResult = true; }, () => window.Close());
        root.Children.Add(buttons);
        UpdateSummary();
        window.ShowDialog();
        if (!confirmed)
            return false;
        var result = Calculate();
        options = new InlineImageResizeOptions(result.Width, result.Height, smooth.IsChecked == true);
        return true;
    }

    public static bool TryGetAngle(Window owner, out double angle)
    {
        angle = 0;
        double parsedAngle = 0;
        var window = CreateWindow(owner, "Rotate Image", 330);
        var root = new StackPanel { Margin = new Thickness(18) };
        window.Content = root;
        root.Children.Add(new TextBlock { Text = "Angle in degrees", FontWeight = FontWeights.SemiBold });
        var value = new TextBox { Text = "0", Margin = new Thickness(0, 8, 0, 16), Padding = new Thickness(8, 5, 8, 5) };
        root.Children.Add(value);
        bool confirmed = false;
        root.Children.Add(ButtonRow("Rotate", () =>
        {
            if (!double.TryParse(value.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed))
                return;
            parsedAngle = Math.Clamp(parsed, -360d, 360d);
            confirmed = true;
            window.DialogResult = true;
        }, () => window.Close()));
        value.SelectAll();
        value.Focus();
        window.ShowDialog();
        angle = parsedAngle;
        return confirmed;
    }

    private static Window CreateWindow(Window owner, string title, double width)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = WpfBrushes.White,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(17, 24, 39)),
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 13
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };
        return window;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.Bold,
        Padding = new Thickness(0, 0, 0, 5)
    };

    private static TextBox NumberRow(Panel root, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(26, 3, 34, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Text = value, Padding = new Thickness(7, 4, 7, 4), HorizontalContentAlignment = WpfHorizontalAlignment.Right };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        root.Children.Add(row);
        return box;
    }

    private static StackPanel ButtonRow(string primaryText, Action primary, Action cancel)
    {
        var row = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        cancelButton.Click += (_, _) => cancel();
        var primaryButton = new Button { Content = primaryText, MinWidth = 82, Padding = new Thickness(12, 6, 12, 6), FontWeight = FontWeights.SemiBold };
        primaryButton.Click += (_, _) => primary();
        row.Children.Add(cancelButton);
        row.Children.Add(primaryButton);
        return row;
    }
}
