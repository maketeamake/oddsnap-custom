using System.Drawing;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class EditableTextAnnotationTests
{
    [Fact]
    public void Scale_PreservesRelativeTextBoxWidth()
    {
        var annotation = new TextAnnotation(
            new Point(100, 80),
            "A wrapped text annotation",
            24f,
            Color.Red,
            true,
            false,
            false,
            false,
            false,
            "Segoe UI",
            300);

        var scaled = Assert.IsType<TextAnnotation>(EditableScreenshotService.Scale(annotation, 0.5f, 0.75f));

        Assert.Equal(new Point(50, 60), scaled.Pos);
        Assert.Equal(150, scaled.MaxWidth);
        Assert.Equal(12f, scaled.FontSize);
    }

    [Fact]
    public void Translate_KeepsTextBoxWidth()
    {
        var annotation = new TextAnnotation(
            new Point(20, 30),
            "Text",
            24f,
            Color.Red,
            true,
            false,
            false,
            false,
            false,
            "Segoe UI",
            240);

        var translated = Assert.IsType<TextAnnotation>(EditableScreenshotService.Translate(annotation, 11, -7));

        Assert.Equal(new Point(31, 23), translated.Pos);
        Assert.Equal(240, translated.MaxWidth);
    }
}
