using System.Drawing;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class EditableShapeAnnotationTests
{
    [Fact]
    public void Translate_PreservesFillAndOptionalBorder()
    {
        var fill = Color.FromArgb(51, 255, 59, 48);
        var annotation = new RectShapeAnnotation(
            new Rectangle(10, 20, 120, 80),
            Color.Red,
            fill,
            Color.Black);

        var translated = Assert.IsType<RectShapeAnnotation>(
            EditableScreenshotService.Translate(annotation, 7, -4));

        Assert.Equal(new Rectangle(17, 16, 120, 80), translated.Rect);
        Assert.Equal(fill, translated.FillColor);
        Assert.Equal(Color.Black, translated.BorderColor);
    }

    [Fact]
    public void Scale_PreservesNoBorderStyle()
    {
        var fill = Color.FromArgb(51, 0, 122, 255);
        var annotation = new CircleShapeAnnotation(
            new Rectangle(20, 30, 200, 100),
            Color.Blue,
            fill,
            null);

        var scaled = Assert.IsType<CircleShapeAnnotation>(
            EditableScreenshotService.Scale(annotation, 0.5f, 0.75f));

        Assert.Equal(new Rectangle(10, 22, 100, 75), scaled.Rect);
        Assert.Equal(fill, scaled.FillColor);
        Assert.Null(scaled.BorderColor);
    }
}
