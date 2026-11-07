using Xunit;

namespace Obozny.Tests;

public class StockSummaryByVydTests
{
    private static (Guid id, NomenclatureItem nom) Nom(string name, string vyd, string unit = "шт")
    {
        var id = Guid.NewGuid();
        return (id, new NomenclatureItem { Id = id, Name = name, Vyd = vyd, Unit = unit });
    }

    private static PropertyItem Prop(Guid nomId, decimal qty, string unit = "шт", string name = "x") =>
        new() { Name = name, Unit = unit, Quantity = qty, NomenclatureId = nomId };

    [Fact]
    public void Skips_Zero_And_EmptyVyd_And_MissingNom()
    {
        var (idOk, nomOk) = Nom("Цвях", "Цвяхи будівельні", "кг");
        var (idEmpty, nomEmpty) = Nom("Без виду", "", "шт");
        var orphan = Guid.NewGuid();
        var byId = new Dictionary<Guid, NomenclatureItem> { [idOk] = nomOk, [idEmpty] = nomEmpty };

        var rows = StockSummaryByVyd.Compute(
            new[]
            {
                Prop(idOk, 5, "кг"),
                Prop(idOk, 0, "кг"),
                Prop(idEmpty, 10),
                Prop(orphan, 3),
            },
            byId);

        Assert.Single(rows);
        Assert.Equal("Цвяхи будівельні", rows[0].Vyd);
        Assert.Equal("кг", rows[0].Unit);
        Assert.Equal(5m, rows[0].Quantity);
    }

    [Fact]
    public void SameVyd_DifferentUnits_AreSeparateRows()
    {
        var (idA, nomA) = Nom("Пісок", "Будівельні матеріали", "т");
        var (idB, nomB) = Nom("Цемент", "Будівельні матеріали", "кг");
        var byId = new Dictionary<Guid, NomenclatureItem> { [idA] = nomA, [idB] = nomB };

        var rows = StockSummaryByVyd.Compute(
            new[]
            {
                Prop(idA, 2, "т"),
                Prop(idB, 50, "кг"),
                Prop(idB, 10, "кг"),
            },
            byId);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Будівельні матеріали", r.Vyd));
        Assert.Contains(rows, r => r.Unit == "т" && r.Quantity == 2m);
        Assert.Contains(rows, r => r.Unit == "кг" && r.Quantity == 60m);
    }

    [Fact]
    public void Aggregates_SameVyd_SameUnit()
    {
        var (id1, n1) = Nom("Дошка 1", "Пиломатеріали", "м³");
        var (id2, n2) = Nom("Дошка 2", "Пиломатеріали", "м³");
        var byId = new Dictionary<Guid, NomenclatureItem> { [id1] = n1, [id2] = n2 };

        var rows = StockSummaryByVyd.Compute(
            new[] { Prop(id1, 1.5m, "м³"), Prop(id2, 2.5m, "м³") },
            byId);

        Assert.Single(rows);
        Assert.Equal("Пиломатеріали", rows[0].Vyd);
        Assert.Equal(4m, rows[0].Quantity);
    }

    [Fact]
    public void ZeroVydTotal_NotShown()
    {
        var (id, nom) = Nom("Скоба", "Скоби будівельні", "кг");
        var byId = new Dictionary<Guid, NomenclatureItem> { [id] = nom };

        var rows = StockSummaryByVyd.Compute(
            new[] { Prop(id, 0, "кг"), Prop(id, -1, "кг") },
            byId);

        Assert.Empty(rows);
    }

    [Fact]
    public void FormatQty_IsQtyOnly_UnitSeparate()
    {
        var qty = StockSummaryByVyd.FormatQty(12.5m);
        Assert.DoesNotContain("кг", qty);
        Assert.DoesNotContain("Пиломатеріали", qty);
        // Порядок колонок стікера: од. вим. потім кількість (окремо).
        var combined = StockSummaryByVyd.FormatQtyWithUnit(12.5m, "кг");
        Assert.EndsWith("кг", combined.Trim());
        Assert.StartsWith(qty, combined);
    }

    [Fact]
    public void NullEnabledVyds_MeansAllCatalogEnabled()
    {
        var set = StockSummaryByVyd.ResolveEnabledVyds(null);
        Assert.Equal(NomenclatureVydCatalog.Combined(null).Count, set.Count);
        Assert.Equal(NomenclatureVydCatalog.All.Length, set.Count);
        Assert.Contains(NomenclatureVydCatalog.All[0], set);
        Assert.False(StockSummaryByVyd.AllVydsDisabled(null));
    }

    [Fact]
    public void EmptyEnabledVyds_MeansNothingShown()
    {
        Assert.True(StockSummaryByVyd.AllVydsDisabled(Array.Empty<string>()));
        Assert.Empty(StockSummaryByVyd.ResolveEnabledVyds(Array.Empty<string>()));
    }

    [Fact]
    public void Filter_SkipsDisabledVyd()
    {
        var (idA, nomA) = Nom("Цвях", "Цвяхи будівельні", "кг");
        var (idB, nomB) = Nom("Дошка", "Пиломатеріали", "м³");
        var byId = new Dictionary<Guid, NomenclatureItem> { [idA] = nomA, [idB] = nomB };

        var rows = StockSummaryByVyd.Compute(
            new[] { Prop(idA, 5, "кг"), Prop(idB, 2, "м³") },
            byId,
            enabledVyds: new[] { "Пиломатеріали" });

        Assert.Single(rows);
        Assert.Equal("Пиломатеріали", rows[0].Vyd);
        Assert.Equal(2m, rows[0].Quantity);
    }

    [Fact]
    public void Filter_EmptyList_YieldsNoRows()
    {
        var (id, nom) = Nom("Цвях", "Цвяхи будівельні", "кг");
        var byId = new Dictionary<Guid, NomenclatureItem> { [id] = nom };

        var rows = StockSummaryByVyd.Compute(
            new[] { Prop(id, 5, "кг") },
            byId,
            enabledVyds: Array.Empty<string>());

        Assert.Empty(rows);
    }

    [Fact]
    public void FilterWindow_IsCheckBoxHit_Null_False()
    {
        Assert.False(StockSummaryVydFilterWindow.IsCheckBoxHit(null));
        Assert.False(StockSummaryVydFilterWindow.ShouldToggleFromRowClick(null));
    }

    /// <summary>
    /// Регресія: ComboBox шаблон має ToggleButton — IsCheckBoxHit не повинен його їсти
    /// (інакше Preview Handled і список О.В. не відкривається).
    /// </summary>
    [Fact]
    public void FilterWindow_IsCheckBoxHit_ComboBoxToggle_NotCheckBox()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var combo = new System.Windows.Controls.ComboBox
                {
                    Width = 160,
                    Height = 40
                };
                combo.Items.Add("");
                combo.Items.Add(VydDisplayUnitHelper.AddUnitItem);

                // Без PresentationSource шаблон часто не розгортається — крутимо в прихованому вікні.
                var host = new System.Windows.Window
                {
                    Content = combo,
                    Width = 220,
                    Height = 100,
                    ShowInTaskbar = false,
                    WindowStyle = System.Windows.WindowStyle.None,
                    Opacity = 0,
                    AllowsTransparency = true,
                    Topmost = false
                };
                host.Show();
                combo.ApplyTemplate();
                host.UpdateLayout();

                Assert.False(StockSummaryVydFilterWindow.IsCheckBoxHit(combo));
                Assert.False(StockSummaryVydFilterWindow.ShouldToggleFromRowClick(combo));

                var toggle = FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(combo);
                Assert.NotNull(toggle);
                Assert.False(StockSummaryVydFilterWindow.IsCheckBoxHit(toggle));
                Assert.False(StockSummaryVydFilterWindow.ShouldToggleFromRowClick(toggle));

                var check = new System.Windows.Controls.CheckBox();
                Assert.True(StockSummaryVydFilterWindow.IsCheckBoxHit(check));

                host.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    private static T? FindDescendant<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    [Fact]
    public void FilterWindow_CollectSelected_AllUnchecked_YieldsEmpty()
    {
        var rows = NomenclatureVydCatalog.All.Select(v => (v, false));
        var selected = StockSummaryVydFilterWindow.CollectSelectedVyds(rows);
        Assert.Empty(selected);
        Assert.True(StockSummaryVydFilterWindow.IsSaveAllowed(selected.Count));
    }

    [Fact]
    public void FilterWindow_CollectSelected_Partial_KeepsCheckedOnly()
    {
        var a = NomenclatureVydCatalog.All[0];
        var b = NomenclatureVydCatalog.All[1];
        var selected = StockSummaryVydFilterWindow.CollectSelectedVyds(
            new[] { (a, true), (b, false) });
        Assert.Equal(new[] { a }, selected);
        Assert.True(StockSummaryVydFilterWindow.IsSaveAllowed(selected.Count));
    }
}
