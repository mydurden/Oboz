using Xunit;

namespace Obozny.Tests;

public class SummaryStatementTests
{
    [Fact]
    public void IsContiguous_accepts_single_and_neighbors_rejects_gap()
    {
        Assert.True(SummaryStatement.IsContiguous([3]));
        Assert.True(SummaryStatement.IsContiguous([1, 2, 3]));
        Assert.False(SummaryStatement.IsContiguous([1, 2, 4]));
        Assert.False(SummaryStatement.IsContiguous(Array.Empty<int>()));
    }

    [Fact]
    public void TryToggleMonth_extends_edge_blocks_gap()
    {
        Assert.True(SummaryStatement.TryToggleMonth([], 1, true, out var next, out _));
        Assert.Equal([1], next);

        Assert.True(SummaryStatement.TryToggleMonth(next, 2, true, out next, out _));
        Assert.Equal([1, 2], next);

        Assert.False(SummaryStatement.TryToggleMonth(next, 4, true, out _, out var err));
        Assert.Contains("суцільний", err ?? "", StringComparison.OrdinalIgnoreCase);

        Assert.True(SummaryStatement.TryToggleMonth(next, 3, true, out next, out _));
        Assert.Equal([1, 2, 3], next);

        Assert.False(SummaryStatement.TryToggleMonth(next, 2, false, out _, out err));
        Assert.Contains("крайній", err ?? "", StringComparison.OrdinalIgnoreCase);

        Assert.True(SummaryStatement.TryToggleMonth(next, 1, false, out next, out _));
        Assert.Equal([2, 3], next);
    }

    [Fact]
    public void TryPeriodFromMonths_builds_calendar_bounds()
    {
        Assert.True(SummaryStatement.TryPeriodFromMonths(
            2026, [1, 2, 3], out var from, out var to, out _));
        Assert.Equal(new DateTime(2026, 1, 1), from);
        Assert.Equal(new DateTime(2026, 3, 31), to);

        Assert.True(SummaryStatement.TryPeriodFromMonths(
            2026, [2], out from, out to, out _));
        Assert.Equal(new DateTime(2026, 2, 1), from);
        Assert.Equal(new DateTime(2026, 2, 28), to);
    }

    [Fact]
    public void Build_merges_by_name_unit_sums_period_and_end_balance()
    {
        var a = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m
        };
        var b = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "ІІ",
            UnitPrice = 200m
        };
        var cement = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Цемент",
            Unit = "т"
        };

        var janIn = Guid.NewGuid();
        var febOut = Guid.NewGuid();
        var earlier = Guid.NewGuid();
        var after = Guid.NewGuid();

        var docs = new[]
        {
            Doc(earlier, new DateTime(2025, 12, 15)),
            Doc(janIn, new DateTime(2026, 1, 10)),
            Doc(febOut, new DateTime(2026, 2, 5)),
            Doc(after, new DateTime(2026, 4, 1))
        };

        var moves = new[]
        {
            Move(earlier, a.Id, a.Name, 50m, new DateTime(2025, 12, 15)),
            Move(earlier, b.Id, b.Name, 30m, new DateTime(2025, 12, 15)),
            Move(janIn, a.Id, a.Name, 20m, new DateTime(2026, 1, 10)),
            Move(janIn, cement.Id, cement.Name, 5m, new DateTime(2026, 1, 10)),
            Move(febOut, a.Id, a.Name, -10m, new DateTime(2026, 2, 5)),
            Move(after, a.Id, a.Name, 100m, new DateTime(2026, 4, 1))
        };

        Assert.True(SummaryStatement.TryPeriodFromMonths(
            2026, [1, 2], out var from, out var to, out _));

        var report = SummaryStatement.Build(
            [a, b, cement],
            moves,
            docs,
            from,
            to,
            statementNumber: "42",
            unitName: "в/ч",
            compiledAt: new DateTime(2026, 3, 1));

        Assert.Equal("42", report.Number);
        Assert.Equal(2, report.Rows.Count);

        var board = report.Rows.Single(r => r.Name == "Дошка");
        Assert.Equal("м3", board.Unit);
        // Надійшло в січні: 20 (категорії злиті; грудневе не в періоді)
        Assert.Equal(20m, board.InQty);
        Assert.Equal(10m, board.OutQty);
        // Баланс на 28.02: 50+30+20-10 = 90 (квітневе +100 не входить)
        Assert.Equal(90m, board.Balance);

        var cem = report.Rows.Single(r => r.Name == "Цемент");
        Assert.Equal(5m, cem.InQty);
        Assert.Equal(0m, cem.OutQty);
        Assert.Equal(5m, cem.Balance);

        // Алфавіт: Дошка перед Цемент
        Assert.Equal("Дошка", report.Rows[0].Name);
        Assert.Equal("Цемент", report.Rows[1].Name);
    }

    [Fact]
    public void Build_includes_balance_only_without_period_move()
    {
        var prop = new PropertyItem { Id = Guid.NewGuid(), Name = "Лопата", Unit = "шт" };
        var docId = Guid.NewGuid();
        var docs = new[] { Doc(docId, new DateTime(2025, 6, 1)) };
        var moves = new[]
        {
            Move(docId, prop.Id, prop.Name, 12m, new DateTime(2025, 6, 1))
        };

        Assert.True(SummaryStatement.TryPeriodFromMonths(
            2026, [1], out var from, out var to, out _));

        var report = SummaryStatement.Build([prop], moves, docs, from, to);
        var row = Assert.Single(report.Rows);
        Assert.Equal(0m, row.InQty);
        Assert.Equal(0m, row.OutQty);
        Assert.Equal(12m, row.Balance);
    }

    [Fact]
    public void Build_skips_draft_and_zero_idle()
    {
        var prop = new PropertyItem { Id = Guid.NewGuid(), Name = "Цвях", Unit = "кг" };
        var draftId = Guid.NewGuid();
        var docs = new[]
        {
            new Document
            {
                Id = draftId,
                Type = DocType.Receipt,
                Date = new DateTime(2026, 1, 5),
                Status = DocStatus.Draft
            }
        };
        var moves = new[]
        {
            Move(draftId, prop.Id, prop.Name, 40m, new DateTime(2026, 1, 5))
        };

        Assert.True(SummaryStatement.TryPeriodFromMonths(
            2026, [1], out var from, out var to, out _));
        var report = SummaryStatement.Build([prop], moves, docs, from, to);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public void SuggestFileName_contains_period()
    {
        var report = new SummaryStatementReport
        {
            PeriodFrom = new DateTime(2026, 1, 1),
            PeriodTo = new DateTime(2026, 3, 31)
        };
        var name = SummaryStatementExport.SuggestFileName(report);
        Assert.Contains("Узагальнююча_відомість_Додаток_1", name, StringComparison.Ordinal);
        Assert.Contains("2026-01-2026-03", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_filters_by_supply_service()
    {
        var eng = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Мішок",
            Unit = "шт",
            SupplyService = SupplyServiceHelper.IsSsp
        };
        var szu = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Кабель",
            Unit = "м",
            SupplyService = SupplyServiceHelper.Szu
        };
        var docId = Guid.NewGuid();
        var docs = new[] { Doc(docId, new DateTime(2026, 1, 5)) };
        var moves = new[]
        {
            Move(docId, eng.Id, eng.Name, 3m, new DateTime(2026, 1, 5)),
            Move(docId, szu.Id, szu.Name, 7m, new DateTime(2026, 1, 5))
        };

        Assert.True(SummaryStatement.TryPeriodFromMonths(
            2026, [1], out var from, out var to, out _));

        var report = SummaryStatement.Build(
            [eng, szu], moves, docs, from, to,
            supplyService: SupplyServiceHelper.IsSsp);

        Assert.Equal(SupplyServiceHelper.IsSsp, report.SupplyService);
        var row = Assert.Single(report.Rows);
        Assert.Equal("Мішок", row.Name);
        Assert.Equal(3m, row.InQty);
    }

    private static Document Doc(Guid id, DateTime date) => new()
    {
        Id = id,
        Type = DocType.Receipt,
        Date = date,
        Status = DocStatus.Posted,
        Number = "1"
    };

    private static Movement Move(
        Guid docId, Guid propId, string name, decimal delta, DateTime date) => new()
    {
        DocumentId = docId,
        DocumentDate = date,
        DocumentType = delta >= 0 ? DocType.Receipt : DocType.Transfer,
        PropertyId = propId,
        PropertyName = name,
        QuantityDelta = delta,
        At = date
    };
}
