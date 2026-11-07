using Xunit;

namespace Obozny.Tests;

public class NomenclatureVydListAndRowClickTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("   ", "—")]
    [InlineData("Макети", "Макети")]
    [InlineData("  Цвяхи будівельні  ", "Цвяхи будівельні")]
    public void FormatVydColumn_EmptyOrValue(string? input, string expected)
    {
        Assert.Equal(expected, NomenclatureListWindow.FormatVydColumn(input));
    }

    [Fact]
    public void ShouldToggleFromRowClick_Null_False()
    {
        Assert.False(AssignVydBulkWindow.ShouldToggleFromRowClick(null));
    }

    [Fact]
    public void IsCheckBoxHit_Null_False()
    {
        Assert.False(AssignVydBulkWindow.IsCheckBoxHit(null));
    }

    [Fact]
    public void CopyVydChoices_StillIndependentLists()
    {
        var a = AssignVydBulkWindow.CopyVydChoices();
        var b = AssignVydBulkWindow.CopyVydChoices();
        Assert.False(ReferenceEquals(a, b));
        Assert.Equal(NomenclatureVydCatalog.All, a);
    }
}
