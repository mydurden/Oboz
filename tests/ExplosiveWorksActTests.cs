using Xunit;

namespace Obozny.Tests;

public class ExplosiveWorksActTests
{
    [Fact]
    public void Build_received_is_stock_return_is_received_minus_spent_for_draft()
    {
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Тротил",
            Unit = "кг",
            Quantity = 10m
        };
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Type = DocType.ExplosiveWorks,
            Status = DocStatus.Draft,
            Date = new DateTime(2026, 6, 1),
            Number = "7",
            Lines =
            {
                new DocumentLine
                {
                    PropertyId = prop.Id,
                    Name = prop.Name,
                    Unit = prop.Unit,
                    Quantity = 3m
                }
            }
        };

        // Підмінити склад через AppStore небезпечно в юніті без ізоляції —
        // перевіряємо формулу через прямий виклик з тимчасовим наповненням.
        var store = AppStore.Instance.Data;
        var saved = store.Properties.ToList();
        try
        {
            store.Properties.Clear();
            store.Properties.Add(prop);

            var rows = ExplosiveWorksAct.Build(doc);
            var row = Assert.Single(rows);
            Assert.Equal("Тротил", row.Name);
            Assert.Equal(10m, row.ReceivedQty);
            Assert.Equal(3m, row.SpentQty);
            Assert.Equal(7m, row.ReturnQty);
        }
        finally
        {
            store.Properties.Clear();
            foreach (var p in saved)
                store.Properties.Add(p);
        }
    }

    [Fact]
    public void Build_posted_adds_spent_back_into_received()
    {
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Детонатор",
            Unit = "шт",
            Quantity = 7m // уже після списання 3
        };
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Type = DocType.ExplosiveWorks,
            Status = DocStatus.Posted,
            Date = new DateTime(2026, 6, 1),
            Number = "8",
            Lines =
            {
                new DocumentLine
                {
                    PropertyId = prop.Id,
                    Name = prop.Name,
                    Unit = prop.Unit,
                    Quantity = 3m
                }
            }
        };

        var store = AppStore.Instance.Data;
        var saved = store.Properties.ToList();
        try
        {
            store.Properties.Clear();
            store.Properties.Add(prop);

            var row = Assert.Single(ExplosiveWorksAct.Build(doc));
            Assert.Equal(10m, row.ReceivedQty);
            Assert.Equal(3m, row.SpentQty);
            Assert.Equal(7m, row.ReturnQty);
        }
        finally
        {
            store.Properties.Clear();
            foreach (var p in saved)
                store.Properties.Add(p);
        }
    }
}
