using System.Windows.Input;
using Xunit;

namespace Obozny.Tests;

public class BulkDisplayUomTests
{
    [Theory]
    [InlineData(Key.Left, 3, 0, 0, true)]   // caret на початку → вихід
    [InlineData(Key.Left, 3, 1, 0, false)]  // caret всередині → лишити
    [InlineData(Key.Right, 3, 3, 0, true)]  // caret в кінці → вихід
    [InlineData(Key.Right, 3, 1, 0, false)] // caret всередині → лишити
    [InlineData(Key.Left, 3, 0, 2, false)]  // є виділення → спочатку згорнути
    [InlineData(Key.Right, 3, 1, 2, false)] // є виділення → спочатку згорнути
    [InlineData(Key.Up, 5, 2, 0, true)]
    [InlineData(Key.Down, 5, 2, 0, true)]
    [InlineData(Key.Tab, 5, 2, 0, true)]
    [InlineData(Key.Left, 0, 0, 0, true)]   // порожнє поле
    [InlineData(Key.Right, 0, 0, 0, true)]
    public void ShouldNavigateFromTextBox_EdgeRules(
        Key key, int textLen, int caret, int selLen, bool expected)
    {
        Assert.Equal(expected,
            BulkDisplayUomWindow.ShouldNavigateFromTextBox(key, textLen, caret, selLen));
    }

    [Fact]
    public void TryComputeNextCell_ArrowsStopAtEdges()
    {
        Assert.False(BulkDisplayUomWindow.TryComputeNextCell(
            0, 0, 0, -1, 3, 5, wrap: false, out _, out _));
        Assert.False(BulkDisplayUomWindow.TryComputeNextCell(
            2, 4, 0, 1, 3, 5, wrap: false, out _, out _));
        Assert.True(BulkDisplayUomWindow.TryComputeNextCell(
            1, 3, 0, 1, 3, 5, wrap: false, out var r, out var c));
        Assert.Equal(1, r);
        Assert.Equal(4, c);
        Assert.True(BulkDisplayUomWindow.TryComputeNextCell(
            1, 4, 1, 0, 3, 5, wrap: false, out r, out c));
        Assert.Equal(2, r);
        Assert.Equal(4, c);
    }

    [Fact]
    public void TryComputeNextCell_TabWrapsAcrossRows()
    {
        Assert.True(BulkDisplayUomWindow.TryComputeNextCell(
            0, 4, 0, 1, 2, 5, wrap: true, out var r, out var c));
        Assert.Equal(1, r);
        Assert.Equal(0, c);
        Assert.True(BulkDisplayUomWindow.TryComputeNextCell(
            1, 0, 0, -1, 2, 5, wrap: true, out r, out c));
        Assert.Equal(0, r);
        Assert.Equal(4, c);
        Assert.False(BulkDisplayUomWindow.TryComputeNextCell(
            1, 4, 0, 1, 2, 5, wrap: true, out _, out _));
    }

    [Theory]
    [InlineData(1000, new double[] { 300, 200, 90, 160, 120 }, 130)]
    [InlineData(870, new double[] { 300, 200, 90, 160, 120 }, 0)]
    [InlineData(0, new double[] { 300, 200 }, 0)]
    [InlineData(500, new double[] { 0, 0, 0 }, 500)]
    public void ComputeFilterGutter_MatchesGridMinusColumns(
        double gridWidth, double[] cols, double expected)
    {
        Assert.Equal(expected, BulkDisplayUomWindow.ComputeFilterGutter(gridWidth, cols));
    }

    [Fact]
    public void BuildDisplayChoices_StartsEmpty_EndsWithAdd_IncludesSelected()
    {
        var list = BulkDisplayUomWindow.BuildDisplayChoices(null, "пгм");
        Assert.Equal("", list[0]);
        Assert.Contains("пгм", list);
        Assert.Equal(VydDisplayUnitHelper.AddUnitItem, list[^1]);
        Assert.DoesNotContain(list.Take(list.Count - 1), VydDisplayUnitHelper.IsAddItem);
    }

    [Fact]
    public void BuildDisplayChoices_WithoutVyd_StillOffersAdd()
    {
        var list = BulkDisplayUomWindow.BuildDisplayChoices("", null);
        Assert.Equal("", list[0]);
        Assert.Equal(VydDisplayUnitHelper.AddUnitItem, list[^1]);
        Assert.Empty(VydDisplayUnitHelper.UnitsForVyd(null, ""));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("12,5", 12.5)]
    [InlineData("0", null)]
    [InlineData("abc", null)]
    public void ParseFactorSortKey_MatchesCardRules(string text, double? expected)
    {
        var key = BulkDisplayUomWindow.ParseFactorSortKey(text);
        if (expected == null)
            Assert.Null(key);
        else
            Assert.Equal((decimal)expected.Value, key);
    }

    [Theory]
    [InlineData("пгм", 11.0, "пгм", 11.0)]
    [InlineData("  ", 0.0, "", null)]
    [InlineData("+ Додати О.В.", 5.0, "", 5.0)]
    [InlineData("кг", null, "кг", null)]
    public void NormalizeDisplayPair_SameAsCard(
        string unitIn, double? factorIn, string unitOut, double? factorOut)
    {
        decimal? fin = factorIn.HasValue ? (decimal)factorIn.Value : null;
        var (u, f) = AppStore.NormalizeDisplayPair(unitIn, fin);
        Assert.Equal(unitOut, u);
        if (factorOut == null)
            Assert.Null(f);
        else
            Assert.Equal((decimal)factorOut.Value, f);
    }
}
