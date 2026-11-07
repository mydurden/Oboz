using Xunit;

namespace Obozny.Tests;

public class NomenclatureListHighlightTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("  ", 10.0, false)]
    [InlineData("пач.", null, false)]
    [InlineData("пач.", 0.0, false)]
    [InlineData("пач.", -1.0, false)]
    [InlineData("пач.", 50.0, true)]
    [InlineData(" м.п. ", 12.5, true)]
    public void HasOwnDisplayConversion_RequiresUnitAndPositiveFactor(
        string? unit, double? factor, bool expected)
    {
        decimal? d = factor is null ? null : (decimal)factor.Value;
        Assert.Equal(expected, NomenclatureListWindow.HasOwnDisplayConversion(unit, d));
    }

    [Fact]
    public void Priority_WhenStockAndNoDisplayPair()
    {
        Assert.True(NomenclatureListWindow.IsPriorityHighlight(true, 3m, hasDisplayPair: false));
        Assert.False(NomenclatureListWindow.IsDimmedHighlight(true, 3m, hasDisplayPair: false));
    }

    [Fact]
    public void Dimmed_WhenZeroStock_EvenWithoutDisplayPair()
    {
        Assert.False(NomenclatureListWindow.IsPriorityHighlight(true, 0m, hasDisplayPair: false));
        Assert.True(NomenclatureListWindow.IsDimmedHighlight(true, 0m, hasDisplayPair: false));
    }

    [Fact]
    public void Dimmed_WhenStockAndDisplayPairComplete()
    {
        Assert.False(NomenclatureListWindow.IsPriorityHighlight(true, 10m, hasDisplayPair: true));
        Assert.True(NomenclatureListWindow.IsDimmedHighlight(true, 10m, hasDisplayPair: true));
    }

    [Fact]
    public void IncompleteDisplayPair_DoesNotDimByItself_UsesStockRule()
    {
        // Коеф. порожній / О.В. не задана — лише правило по залишку.
        Assert.True(NomenclatureListWindow.IsPriorityHighlight(true, 5m, hasDisplayPair: false));
        Assert.False(NomenclatureListWindow.IsDimmedHighlight(true, 5m, hasDisplayPair: false));
        Assert.True(NomenclatureListWindow.IsDimmedHighlight(true, 0m, hasDisplayPair: false));
    }

    [Fact]
    public void StockUnknown_NeitherPriorityNorDimmed_NoFlash()
    {
        Assert.False(NomenclatureListWindow.IsPriorityHighlight(false, 0m, hasDisplayPair: false));
        Assert.False(NomenclatureListWindow.IsDimmedHighlight(false, 0m, hasDisplayPair: false));
        Assert.False(NomenclatureListWindow.IsPriorityHighlight(false, 9m, hasDisplayPair: true));
        Assert.False(NomenclatureListWindow.IsDimmedHighlight(false, 9m, hasDisplayPair: true));
    }
}
