using Xunit;

namespace Obozny.Tests;

public class NomenclatureVydNameMatchTests
{
    private static NomenclatureItem Nom(string name, string vyd = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Unit = "шт",
        Vyd = vyd,
    };

    [Fact]
    public void KolyuchyiDrit_MatchesNameTokens()
    {
        const string vyd = "Загородження інженерні (Колючий дріт)";
        Assert.True(NomenclatureVydNameMatch.MatchesName("Дріт колючий оцинкований", vyd));
        Assert.True(NomenclatureVydNameMatch.MatchesName("КОЛЮЧИЙ ДРІТ 2.5", vyd));
        Assert.False(NomenclatureVydNameMatch.MatchesName("Цвяхи будівельні 100 мм", vyd));
        Assert.False(NomenclatureVydNameMatch.MatchesName("Пісок кар'єрний", vyd));
    }

    [Fact]
    public void Egoza_And_Plutanka_Synonyms()
    {
        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "Стрічка Егоза 450", "Загородження інженерні (Єгоза)"));
        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "єгоза армована", "Загородження інженерні (Єгоза)"));
        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "Плутанка МЗП-1", "Загородження інженерні (Плутанка)"));
        Assert.False(NomenclatureVydNameMatch.MatchesName(
            "Плутанка МЗП-1", "Загородження інженерні (Єгоза)"));
    }

    [Fact]
    public void StopWords_DoNotDriveMatchAlone()
    {
        // «для» / «і» не є голками; без значущих слів збігу немає.
        var needles = NomenclatureVydNameMatch.NeedlesFor("Сітка для АДЗ/ІКАЗ");
        Assert.DoesNotContain(needles, n => n is "для" or "і");
        Assert.Contains(needles, n => n.Contains("сітк", StringComparison.OrdinalIgnoreCase)
                                      || n.Contains("адз", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FilterByName_OnlySimilar_WithoutVyd()
    {
        var list = new[]
        {
            Nom("Дріт колючий 3 мм"),
            Nom("Цвях 120"),
            Nom("Загородження з колючого дроту"),
            Nom("Пісок"),
        };
        var matched = NomenclatureVydNameMatch.FilterByName(
            list, "Загородження інженерні (Колючий дріт)");
        Assert.Equal(2, matched.Count);
        Assert.All(matched, m => Assert.True(
            m.Name.Contains("колюч", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("дріт", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("дрот", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "МЕТАЛОПРОКАТ ЛИСТ", "Металопрокат"));
    }
}
