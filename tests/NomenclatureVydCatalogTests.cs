using Xunit;

namespace Obozny.Tests;

public class NomenclatureVydCatalogTests
{
    [Fact]
    public void All_IsAlphabetical_UkrainianAndContainsOvt()
    {
        var all = NomenclatureVydCatalog.All;
        Assert.Equal(25, all.Length);
        Assert.Contains("Інженерна техніка (ОВТ)", all);
        Assert.DoesNotContain(all, v => v.Contains("Інженера", StringComparison.Ordinal));
        Assert.DoesNotContain("Загородження інженерні", all);
        Assert.Contains("Загородження інженерні (Єгоза)", all);
        Assert.Contains("Загородження інженерні (Колючий дріт)", all);
        Assert.Contains("Загородження інженерні (Плутанка)", all);

        var sorted = all.OrderBy(x => x, StringComparer.Create(
            new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false)).ToArray();
        Assert.Equal(sorted, all);
    }

    [Theory]
    [InlineData("Загородження інженерні (Єгоза)", NomenclatureKind.Stocks)]
    [InlineData("Загородження інженерні (Колючий дріт)", NomenclatureKind.Stocks)]
    [InlineData("Загородження інженерні (Плутанка)", NomenclatureKind.Stocks)]
    [InlineData("Загородження інженерні", NomenclatureKind.Stocks)]
    public void DefaultKind_ZagorodzhenniaAreStocks(string vyd, NomenclatureKind expected) =>
        Assert.Equal(expected, NomenclatureVydCatalog.DefaultKind(vyd));

    [Theory]
    [InlineData("Шанцевий інструмент", NomenclatureKind.FixedAssets)]
    [InlineData("Електроінструмент", NomenclatureKind.FixedAssets)]
    [InlineData("Фортифікаційні споруди", NomenclatureKind.FixedAssets)]
    [InlineData("Макети", NomenclatureKind.FixedAssets)]
    [InlineData("Інженерна техніка (ОВТ)", NomenclatureKind.FixedAssets)]
    [InlineData("Будівельні матеріали", NomenclatureKind.Stocks)]
    [InlineData("Інше", NomenclatureKind.Stocks)]
    [InlineData("Саперне майно", NomenclatureKind.Stocks)]
    [InlineData("", NomenclatureKind.Stocks)]
    [InlineData(null, NomenclatureKind.Stocks)]
    public void DefaultKind_MatchesSpec(string? vyd, NomenclatureKind expected) =>
        Assert.Equal(expected, NomenclatureVydCatalog.DefaultKind(vyd));
}
