using Xunit;

namespace Obozny.Tests;

public class VydDisplayAndCustomTests
{
    [Fact]
    public void Combined_AddsCustom_Sorted_DoesNotAlterFixed()
    {
        var fixedCopy = NomenclatureVydCatalog.All.ToArray();
        var combined = NomenclatureVydCatalog.Combined(new[] { "Яяя-тест", "Ааа-тест" });
        Assert.Equal(fixedCopy, NomenclatureVydCatalog.All);
        Assert.Contains("Яяя-тест", combined);
        Assert.Contains("Ааа-тест", combined);
        Assert.Contains("Будівельні матеріали", combined);
        var sorted = combined.OrderBy(x => x, StringComparer.Create(
            new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false)).ToList();
        Assert.Equal(sorted, combined);
    }

    [Fact]
    public void ComboChoices_EndsWithNewItem()
    {
        var items = NomenclatureVydCatalog.ComboChoices(new[] { "Мій вид" });
        Assert.Equal(NomenclatureVydCatalog.NewVydItem, items[^1]);
        Assert.Contains("Мій вид", items);
        Assert.False(NomenclatureVydCatalog.IsNewItem("Мій вид"));
        Assert.True(NomenclatureVydCatalog.IsNewItem(NomenclatureVydCatalog.NewVydItem));
    }

    [Fact]
    public void NormalizeNewVydName_RejectsEmptyDuplicateAndSentinel()
    {
        Assert.Null(NomenclatureVydCatalog.NormalizeNewVydName("  ", null));
        Assert.Null(NomenclatureVydCatalog.NormalizeNewVydName(NomenclatureVydCatalog.NewVydItem, null));
        Assert.Null(NomenclatureVydCatalog.NormalizeNewVydName("Цвяхи будівельні", null));
        Assert.Null(NomenclatureVydCatalog.NormalizeNewVydName("Мій", new[] { "Мій" }));
        Assert.Equal("Мій вид", NomenclatureVydCatalog.NormalizeNewVydName("  Мій вид  ", null));
    }

    [Fact]
    public void CandidateUnitsFromBase_ExcludesAlreadyAndTyped()
    {
        var noms = new[]
        {
            new NomenclatureItem { Unit = "кг" },
            new NomenclatureItem { Unit = "пач." },
            new NomenclatureItem { Unit = "шт" },
            new NomenclatureItem { Unit = "кг" },
        };
        var list = VydDisplayUnitHelper.CandidateUnitsFromBase(noms, new[] { "шт" }, "пач.");
        Assert.DoesNotContain("шт", list);
        Assert.DoesNotContain("пач.", list);
        Assert.Contains("кг", list);
    }

    [Fact]
    public void Compute_DisplayUnit_UsesFactor_SkipsWithout()
    {
        var vyd = "Цвяхи будівельні";
        var idOk = Guid.NewGuid();
        var idNoFactor = Guid.NewGuid();
        var idWrongUnit = Guid.NewGuid();
        var byId = new Dictionary<Guid, NomenclatureItem>
        {
            [idOk] = new()
            {
                Id = idOk, Name = "Цвях 100", Vyd = vyd, Unit = "кг",
                DisplayUnit = "пач.", DisplayFactor = 50m
            },
            [idNoFactor] = new()
            {
                Id = idNoFactor, Name = "Цвях без к", Vyd = vyd, Unit = "кг",
                DisplayUnit = "пач.", DisplayFactor = null
            },
            [idWrongUnit] = new()
            {
                Id = idWrongUnit, Name = "Цвях інше", Vyd = vyd, Unit = "кг",
                DisplayUnit = "шт", DisplayFactor = 10m
            },
        };
        var props = new[]
        {
            new PropertyItem { NomenclatureId = idOk, Quantity = 2, Unit = "кг" },
            new PropertyItem { NomenclatureId = idNoFactor, Quantity = 5, Unit = "кг" },
            new PropertyItem { NomenclatureId = idWrongUnit, Quantity = 3, Unit = "кг" },
        };

        var rows = StockSummaryByVyd.Compute(
            props, byId,
            enabledVyds: new[] { vyd },
            displayUnitByVyd: new Dictionary<string, string> { [vyd] = "пач." });

        Assert.Single(rows);
        Assert.Equal(vyd, rows[0].Vyd);
        Assert.Equal("пач.", rows[0].Unit);
        Assert.Equal(100m, rows[0].Quantity); // 2 * 50
    }

    [Fact]
    public void Compute_WithoutDisplayMap_KeepsOfficialUnits()
    {
        var vyd = "Пиломатеріали";
        var id = Guid.NewGuid();
        var byId = new Dictionary<Guid, NomenclatureItem>
        {
            [id] = new()
            {
                Id = id, Name = "Дошка", Vyd = vyd, Unit = "м³",
                DisplayUnit = "м.п.", DisplayFactor = 12m
            }
        };
        var rows = StockSummaryByVyd.Compute(
            new[] { new PropertyItem { NomenclatureId = id, Quantity = 2, Unit = "м³" } },
            byId,
            enabledVyds: new[] { vyd });

        Assert.Single(rows);
        Assert.Equal("м³", rows[0].Unit);
        Assert.Equal(2m, rows[0].Quantity);
    }

    [Fact]
    public void ResolveEnabled_Null_IncludesCustom()
    {
        var set = StockSummaryByVyd.ResolveEnabledVyds(null, new[] { "Мій вид" });
        Assert.Contains("Мій вид", set);
        Assert.Contains(NomenclatureVydCatalog.All[0], set);
    }

    [Fact]
    public void FilterWindow_CollectDisplayUnits_OnlySelectedWithUnit()
    {
        var map = StockSummaryVydFilterWindow.CollectDisplayUnits(new[]
        {
            ("A", true, "пач."),
            ("B", true, ""),
            ("C", false, "кг"),
            ("D", true, VydDisplayUnitHelper.AddUnitItem),
        });
        Assert.Single(map);
        Assert.Equal("пач.", map["A"]);
    }

    [Fact]
    public void TryParseFactor_EmptyAndPositive()
    {
        Assert.True(NomenclatureCardWindow.TryParseFactor("", out var f));
        Assert.Null(f);
        Assert.True(NomenclatureCardWindow.TryParseFactor("12,5", out f));
        Assert.Equal(12.5m, f);
        Assert.False(NomenclatureCardWindow.TryParseFactor("0", out _));
        Assert.False(NomenclatureCardWindow.TryParseFactor("abc", out _));
    }

    [Fact]
    public void PickWindow_ResolveChoice_PrefersTyped()
    {
        Assert.Equal("пач.", VydDisplayUnitPickWindow.ResolveChoice("пач.", "кг"));
        Assert.Equal("кг", VydDisplayUnitPickWindow.ResolveChoice("  ", "кг"));
        Assert.Null(VydDisplayUnitPickWindow.ResolveChoice("", null));
    }
}
