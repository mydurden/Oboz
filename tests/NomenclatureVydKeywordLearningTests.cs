using Xunit;

namespace Obozny.Tests;

public class NomenclatureVydKeywordLearningTests
{
    private static NomenclatureItem Nom(string name, string vyd = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Unit = "шт",
        Vyd = vyd,
    };

    [Fact]
    public void ExtractKeys_Doska_StemAndTrash()
    {
        var keys = NomenclatureVydNameMatch.ExtractKeys("ДОШКА ОБРІЗНА 50х150х6000 мм");
        Assert.Contains(keys, k => k.Equals("дошк", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, k => k is "мм" or "х" or "x" or "50" or "150" or "6000");
        // «обрізна» (7) → стем «обрізн»
        Assert.Contains(keys, k => k.StartsWith("обріз", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Learn_Then_MatchByLearnedKey()
    {
        var store = new List<VydKeywordEntry>();
        Assert.True(NomenclatureVydNameMatch.LearnFromName(
            "ДОШКА ОБРІЗНА 50х150", "Пиломатеріали", store));
        Assert.Contains(store, e =>
            e.Keyword.Equals("дошк", StringComparison.OrdinalIgnoreCase)
            && e.Vyd == "Пиломатеріали");

        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "Дошка стругана 25 мм", "Пиломатеріали", store));
        Assert.False(NomenclatureVydNameMatch.MatchesName(
            "Дошка стругана 25 мм", "Лісоматеріали", store));
        // Без навчання «дошк» не входить у назву Виду «Пиломатеріали»
        Assert.False(NomenclatureVydNameMatch.MatchesName(
            "Дошка стругана 25 мм", "Пиломатеріали", learned: null));
    }

    [Fact]
    public void Learn_Conflict_KeepsOldVyd()
    {
        var store = new List<VydKeywordEntry>();
        Assert.True(NomenclatureVydNameMatch.LearnFromName(
            "ДОШКА ОБРІЗНА", "Пиломатеріали", store));
        // Той самий ключ «дошк» → інший Вид: не перезаписуємо (інші нові ключі з назви — ок).
        NomenclatureVydNameMatch.LearnFromName("Дошка необрізна", "Лісоматеріали", store);
        var doshk = store.Single(e => e.Keyword.Equals("дошк", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Пиломатеріали", doshk.Vyd);
        Assert.DoesNotContain(store, e => e.Vyd == "Лісоматеріали"
            && e.Keyword.Equals("дошк", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Learn_SameVyd_Idempotent()
    {
        var store = new List<VydKeywordEntry>();
        Assert.True(NomenclatureVydNameMatch.LearnFromName(
            "ДОШКА", "Пиломатеріали", store));
        var count = store.Count;
        Assert.False(NomenclatureVydNameMatch.LearnFromName(
            "ДОШКА", "Пиломатеріали", store));
        Assert.Equal(count, store.Count);
    }

    [Fact]
    public void FilterByName_UsesLearnedKeys()
    {
        var store = new List<VydKeywordEntry>();
        NomenclatureVydNameMatch.LearnFromName("ДОШКА ОБРІЗНА", "Пиломатеріали", store);

        var list = new[]
        {
            Nom("Дошка стругана"),
            Nom("Цвях 120"),
            Nom("Брус 100х100"),
            Nom("Пісок"),
        };
        var matched = NomenclatureVydNameMatch.FilterByName(list, "Пиломатеріали", store);
        Assert.Single(matched);
        Assert.Equal("Дошка стругана", matched[0].Name);
    }

    [Fact]
    public void FixedSynonyms_StillWork_WithEmptyStore()
    {
        var store = new List<VydKeywordEntry>();
        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "Стрічка Егоза 450", "Загородження інженерні (Єгоза)", store));
        Assert.True(NomenclatureVydNameMatch.MatchesName(
            "Дріт колючий", "Загородження інженерні (Колючий дріт)", store));
    }

    [Fact]
    public void MultipleKeys_PerVyd_Ok()
    {
        var store = new List<VydKeywordEntry>();
        NomenclatureVydNameMatch.LearnFromName("ДОШКА ОБРІЗНА", "Пиломатеріали", store);
        NomenclatureVydNameMatch.LearnFromName("БРУСОК СОСНОВИЙ", "Пиломатеріали", store);
        Assert.True(store.Count(e => e.Vyd == "Пиломатеріали") >= 2);
        Assert.True(NomenclatureVydNameMatch.MatchesName("брусок 40х40", "Пиломатеріали", store));
        Assert.True(NomenclatureVydNameMatch.MatchesName("дошка 25", "Пиломатеріали", store));
    }
}
