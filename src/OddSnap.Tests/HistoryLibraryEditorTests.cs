using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class HistoryLibraryEditorTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("9999", 9999)]
    public void TryParseInlineStepNumber_AcceptsSupportedValues(string text, int expected)
    {
        bool parsed = HistoryLibraryWindow.TryParseInlineStepNumber(text, out int number);

        Assert.True(parsed);
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("10000")]
    [InlineData("3a")]
    public void TryParseInlineStepNumber_RejectsUnsupportedValues(string text)
    {
        Assert.False(HistoryLibraryWindow.TryParseInlineStepNumber(text, out _));
    }
}
