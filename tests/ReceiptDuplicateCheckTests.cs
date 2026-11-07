using Xunit;

namespace Obozny.Tests;

public class ReceiptDuplicateCheckTests
{
    [Fact]
    public void Find_soft_when_number_and_name_match_but_qty_differs()
    {
        var docs = new[]
        {
            Receipt("2426/1", "Цвях", 10m, 5m)
        };
        var rows = new[]
        {
            Incoming("2426/1", "Цвях", 20m, 5m)
        };

        var hits = ReceiptDuplicateCheck.Find(rows, docs);

        Assert.Single(hits);
        Assert.Equal(ReceiptDuplicateCheck.MatchKind.Soft, hits[0].Kind);
        Assert.Contains("2426/1", hits[0].Number);
        Assert.Equal("Цвях", hits[0].Name);
    }

    [Fact]
    public void Find_exact_when_number_name_qty_price_match()
    {
        var docs = new[]
        {
            Receipt("2426/1", "Цвях", 10m, 5.12m)
        };
        var rows = new[]
        {
            Incoming("2426/1", "цвях", 10m, 5.12m)
        };

        var hits = ReceiptDuplicateCheck.Find(rows, docs);

        Assert.Single(hits);
        Assert.Equal(ReceiptDuplicateCheck.MatchKind.Exact, hits[0].Kind);
    }

    [Fact]
    public void Find_ignores_non_receipt_incoming_and_docs()
    {
        var docs = new[]
        {
            new Document
            {
                Type = DocType.WriteOff,
                Number = "1",
                Lines = { new DocumentLine { Name = "Цвях", Quantity = 10, UnitPrice = 5 } }
            }
        };
        var rows = new[]
        {
            new ImportRow
            {
                Number = "1", Name = "Цвях", Quantity = 10, Price = 5,
                Kind = ImportMoveKind.WriteOff, Date = DateTime.Today
            }
        };

        Assert.Empty(ReceiptDuplicateCheck.Find(rows, docs));
    }

    [Fact]
    public void Find_skips_blank_document_number()
    {
        var docs = new[]
        {
            Receipt("б/н", "Цвях", 10m, 5m)
        };
        var rows = new[]
        {
            Incoming("", "Цвях", 10m, 5m)
        };

        Assert.Empty(ReceiptDuplicateCheck.Find(rows, docs));
    }

    [Fact]
    public void BuildConfirmText_mentions_both_cases()
    {
        var text = ReceiptDuplicateCheck.BuildConfirmText(new[]
        {
            new ReceiptDuplicateCheck.Hit(ReceiptDuplicateCheck.MatchKind.Soft, "A-1", "Дошка"),
            new ReceiptDuplicateCheck.Hit(ReceiptDuplicateCheck.MatchKind.Exact, "B-2", "Цвях")
        });

        Assert.Contains("уже були в прийманні", text);
        Assert.Contains("уже було раніше прийнято", text);
        Assert.Contains("Зберегти все одно?", text);
    }

    private static Document Receipt(string number, string name, decimal qty, decimal price) =>
        new()
        {
            Type = DocType.Receipt,
            Number = number,
            Status = DocStatus.Posted,
            Lines =
            {
                new DocumentLine { Name = name, Quantity = qty, UnitPrice = price, Unit = "шт" }
            }
        };

    private static ImportRow Incoming(string number, string name, decimal qty, decimal price) =>
        new()
        {
            Number = number,
            Name = name,
            Quantity = qty,
            Price = price,
            Kind = ImportMoveKind.Receipt,
            Date = DateTime.Today,
            Unit = "шт"
        };
}
