using Xunit;

namespace Obozny.Tests;

public class NomenclatureWithoutVydTests
{
    private static NomenclatureItem Nom(string name, string vyd, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Unit = "шт",
        Vyd = vyd,
    };

    [Fact]
    public void Others_SkipsFilledVyd_AndExcludeId()
    {
        var current = Nom("Поточна", "", Guid.NewGuid());
        var emptyA = Nom("Альфа", "");
        var filled = Nom("Бета", "Цвяхи будівельні");
        var emptyB = Nom("Гама", "   ");

        var list = NomenclatureWithoutVyd.Others(
            new[] { current, emptyA, filled, emptyB },
            current.Id);

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "Альфа", "Гама" }, list.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void Others_WhenNoExclude_IncludesAllEmpty()
    {
        var a = Nom("Без виду 1", "");
        var b = Nom("З видом", "Макети");
        var list = NomenclatureWithoutVyd.Others(new[] { a, b }, excludeId: null);
        Assert.Single(list);
        Assert.Equal(a.Id, list[0].Id);
    }

    [Fact]
    public void Others_Empty_WhenEveryoneHasVyd()
    {
        var list = NomenclatureWithoutVyd.Others(
            new[] { Nom("А", "Інше"), Nom("Б", "Макети") },
            Guid.NewGuid());
        Assert.Empty(list);
    }

    [Fact]
    public void CopyVydChoices_NotReferenceEqual_AcrossRows()
    {
        var a = AssignVydBulkWindow.CopyVydChoices();
        var b = AssignVydBulkWindow.CopyVydChoices();
        Assert.False(ReferenceEquals(a, b));
        Assert.False(ReferenceEquals(a, NomenclatureVydCatalog.All));
        var expected = NomenclatureVydCatalog.Combined(AppStore.Instance.Data.CustomVyds);
        Assert.Equal(expected, a);
        Assert.Equal(expected, b);
    }
}
