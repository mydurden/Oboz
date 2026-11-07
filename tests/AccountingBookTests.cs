using Xunit;

namespace Obozny.Tests;

public class AccountingBookTests
{
    [Fact]
    public void ColumnKey_splits_same_name_by_category_and_price()
    {
        var a = Prop("Дошка", "І", 100m);
        var b = Prop("Дошка", "ІІ", 100m);
        var c = Prop("Дошка", "І", 200m);
        var d = Prop("Дошка", "І", 100m);

        Assert.NotEqual(AccountingBook.ColumnKeyOf(a), AccountingBook.ColumnKeyOf(b));
        Assert.NotEqual(AccountingBook.ColumnKeyOf(a), AccountingBook.ColumnKeyOf(c));
        Assert.Equal(AccountingBook.ColumnKeyOf(a), AccountingBook.ColumnKeyOf(d));
    }

    [Fact]
    public void ClassifyDelta_inbound_outbound_and_zero()
    {
        Assert.Equal((10m, (decimal?)null), AccountingBook.ClassifyDelta(10m));
        Assert.Equal(((decimal?)null, 7m), AccountingBook.ClassifyDelta(-7m));
        Assert.Equal(((decimal?)null, (decimal?)null), AccountingBook.ClassifyDelta(0m));
    }

    [Fact]
    public void QtyText_null_is_em_dash_not_empty()
    {
        Assert.Equal("—", AccountingBookLayout.QtyText(null));
        Assert.Equal(AccountingBookLayout.QtyDash, AccountingBookLayout.QtyText(null));
        Assert.NotEqual("", AccountingBookLayout.QtyText(null));
        Assert.Equal(Ua.FormatQty(12m), AccountingBookLayout.QtyText(12m));
        Assert.Equal(Ua.FormatQty(0m), AccountingBookLayout.QtyText(0m));
    }

    [Fact]
    public void BalanceAfter_and_warehouse_include_reserved()
    {
        Assert.Equal(90m, AccountingBook.BalanceAfter(100m, -10m));
        Assert.Equal(110m, AccountingBook.BalanceAfter(100m, 10m));

        var p = new PropertyItem { Quantity = 70m, ReservedQuantity = 30m };
        Assert.Equal(100m, AccountingBook.WarehousePresence(p));
    }

    [Fact]
    public void BuildSection_posted_receipt_then_transfer_updates_stanovyt()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 3900m,
            Quantity = 80m,
            NomenclatureId = nom.Id
        };

        var receiptId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var receipt = new Document
        {
            Id = receiptId,
            Type = DocType.Receipt,
            Number = "0001",
            Date = new DateTime(2025, 1, 8),
            Status = DocStatus.Posted
        };
        var transfer = new Document
        {
            Id = transferId,
            Type = DocType.Transfer,
            Number = "0010",
            Date = new DateTime(2025, 3, 4),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptId,
                DocumentDate = receipt.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "0001",
                PropertyId = prop.Id,
                PropertyName = prop.Name,
                QuantityDelta = 100m,
                At = receipt.Date
            },
            new Movement
            {
                DocumentId = transferId,
                DocumentDate = transfer.Date,
                DocumentType = DocType.Transfer,
                DocumentNumber = "0010",
                PropertyId = prop.Id,
                PropertyName = prop.Name,
                QuantityDelta = -20m,
                At = transfer.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { receipt, transfer },
            "Склад інженерної служби");

        Assert.Single(section.Columns);
        Assert.Equal(2, section.Rows.Count);

        var key = section.Columns[0].Key;
        Assert.Equal(100m, section.Rows[0].Cells[key].InQty);
        Assert.Null(section.Rows[0].Cells[key].OutQty);
        Assert.Equal(100m, section.Rows[0].Cells[key].Balance);

        Assert.Null(section.Rows[1].Cells[key].InQty);
        Assert.Equal(20m, section.Rows[1].Cells[key].OutQty);
        Assert.Equal(80m, section.Rows[1].Cells[key].Balance);
        Assert.Equal("Накладна", section.Rows[1].DocumentTitle);
    }

    [Fact]
    public void BuildSection_ignores_draft_document_movements()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Цвях", Vyd = "Цвяхи будівельні", Unit = "кг" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Цвях",
            Unit = "кг",
            UnitPrice = 50m,
            Quantity = 10m,
            ReservedQuantity = 5m,
            NomenclatureId = nom.Id
        };
        var draftId = Guid.NewGuid();
        var draft = new Document
        {
            Id = draftId,
            Type = DocType.Issue,
            Number = "D1",
            Date = DateTime.Today,
            Status = DocStatus.Draft
        };
        // Навіть якщо помилково є Movement на чернетку — не беремо в книгу.
        var movements = new[]
        {
            new Movement
            {
                DocumentId = draftId,
                DocumentDate = draft.Date,
                DocumentType = DocType.Issue,
                PropertyId = prop.Id,
                QuantityDelta = -5m,
                At = DateTime.Now
            }
        };

        var section = AccountingBook.BuildSection(
            "Цвяхи будівельні",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { draft });

        Assert.Single(section.Columns);
        Assert.Empty(section.Rows);
        Assert.Equal(15m, AccountingBook.WarehousePresence(prop));
    }

    [Fact]
    public void BuildSection_uses_accounting_unit_not_display_unit()
    {
        var nom = new NomenclatureItem
        {
            Id = Guid.NewGuid(),
            Name = "Дріт",
            Vyd = "Дріт металевий",
            Unit = "кг",
            DisplayUnit = "мотки",
            DisplayFactor = 10m
        };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дріт",
            Unit = "кг",
            UnitPrice = 1m,
            Quantity = 5m,
            NomenclatureId = nom.Id
        };

        var section = AccountingBook.BuildSection(
            "Дріт металевий",
            new[] { prop },
            new[] { nom },
            Array.Empty<Movement>(),
            Array.Empty<Document>());

        Assert.Single(section.Columns);
        Assert.Equal("кг", section.Columns[0].Unit);
    }

    [Fact]
    public void DefaultPeriod_from_jan1_to_today()
    {
        var today = new DateTime(2026, 10, 25);
        Assert.Equal(new DateTime(2026, 1, 1), AccountingBook.DefaultPeriodFrom(today));
        Assert.Equal(today, AccountingBook.DefaultPeriodTo(today));
        Assert.True(AccountingBook.InPeriod(new DateTime(2026, 5, 1), new DateTime(2026, 1, 1), today));
        Assert.False(AccountingBook.InPeriod(new DateTime(2025, 12, 31), new DateTime(2026, 1, 1), today));
    }

    [Fact]
    public void BuildToc_only_kinds_with_posted_moves_in_period()
    {
        var nomIn = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали" };
        var nomOut = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Цвях", Vyd = "Цвяхи будівельні" };
        var nomOld = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дріт", Vyd = "Дріт металевий" };
        var propIn = new PropertyItem { Id = Guid.NewGuid(), Name = "Дошка", NomenclatureId = nomIn.Id };
        var propOut = new PropertyItem { Id = Guid.NewGuid(), Name = "Цвях", NomenclatureId = nomOut.Id };
        var propOld = new PropertyItem { Id = Guid.NewGuid(), Name = "Дріт", NomenclatureId = nomOld.Id };

        var docIn = new Document
        {
            Id = Guid.NewGuid(),
            Type = DocType.Receipt,
            Date = new DateTime(2026, 3, 10),
            Status = DocStatus.Posted
        };
        var docDraft = new Document
        {
            Id = Guid.NewGuid(),
            Type = DocType.Receipt,
            Date = new DateTime(2026, 4, 1),
            Status = DocStatus.Draft
        };
        var docOld = new Document
        {
            Id = Guid.NewGuid(),
            Type = DocType.Receipt,
            Date = new DateTime(2025, 6, 1),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = docIn.Id,
                DocumentDate = docIn.Date,
                PropertyId = propIn.Id,
                QuantityDelta = 10m,
                At = docIn.Date
            },
            new Movement
            {
                DocumentId = docDraft.Id,
                DocumentDate = docDraft.Date,
                PropertyId = propOut.Id,
                QuantityDelta = 5m,
                At = docDraft.Date
            },
            new Movement
            {
                DocumentId = docOld.Id,
                DocumentDate = docOld.Date,
                PropertyId = propOld.Id,
                QuantityDelta = 3m,
                At = docOld.Date
            }
        };

        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 10, 25);
        var toc = AccountingBook.BuildToc(
            new[] { nomIn, nomOut, nomOld },
            new[] { propIn, propOut, propOld },
            movements,
            new[] { docIn, docDraft, docOld },
            from,
            to);

        Assert.Contains("Пиломатеріали", toc);
        Assert.DoesNotContain("Цвяхи будівельні", toc); // чернетка
        Assert.DoesNotContain("Дріт металевий", toc); // поза періодом
        Assert.DoesNotContain("Будівельні матеріали", toc);
    }

    [Fact]
    public void BuildSection_empty_vyd_has_no_columns_or_rows()
    {
        var section = AccountingBook.BuildSection(
            "Макети",
            Array.Empty<PropertyItem>(),
            Array.Empty<NomenclatureItem>(),
            Array.Empty<Movement>(),
            Array.Empty<Document>());

        Assert.True(section.IsEmpty);
    }

    [Fact]
    public void FormatOpWriteOff_two_lines_with_op_name()
    {
        Assert.Equal("списання\n(ОП Честерфілд)", AccountingBook.FormatOpWriteOff("Честерфілд"));
        Assert.Equal("списання\n(ОП Північ)", AccountingBook.FormatOpWriteOff("  Північ  "));
        Assert.Equal("", AccountingBook.FormatOpWriteOff(""));
        Assert.Equal("", AccountingBook.FormatOpWriteOff(null));
        var text = AccountingBook.FormatOpWriteOff("Честерфілд");
        Assert.Contains("\n", text);
        Assert.StartsWith("списання", text);
        Assert.EndsWith("(ОП Честерфілд)", text);
    }

    [Fact]
    public void FormatNumberAndDate_two_lines()
    {
        var text = AccountingBook.FormatNumberAndDate("2426/1331", new DateTime(2026, 7, 30));
        Assert.Equal("2426/1331\n30.07.2026", text);
        Assert.Equal("242\n18.03.2023", AccountingBook.FormatNumberAndDate("242", new DateTime(2023, 3, 18)));
        Assert.Equal("б/н\n01.01.2024", AccountingBook.FormatNumberAndDate(null, new DateTime(2024, 1, 1)));
        Assert.DoesNotContain("№", text);
        Assert.DoesNotContain("від", text);
    }

    [Fact]
    public void DocumentShortTitle_uses_DocTypeNames()
    {
        Assert.Equal("Акт якісного стану", AccountingBook.DocumentShortTitle(DocType.QualityState));
        Assert.Equal("Накладна (вимога)", AccountingBook.DocumentShortTitle(DocType.Receipt));
        Assert.Equal("Видача майна", AccountingBook.DocumentShortTitle(DocType.Issue));
        Assert.Equal("Акт списання", AccountingBook.DocumentShortTitle(DocType.WriteOff));
        Assert.Equal(DocTypeNames.Title(DocType.QualityChange), AccountingBook.DocumentShortTitle(DocType.QualityChange));
    }

    [Fact]
    public void ResolveCounterparty_op_never_bare_strongpoint_name()
    {
        var doc = new Document
        {
            Type = DocType.QualityState,
            StrongpointName = "Честерфілд",
            Status = DocStatus.Posted
        };
        var text = AccountingBook.ResolveCounterparty(doc, "Підрозділ", "СЗУ");
        Assert.Equal("списання\n(ОП Честерфілд)", text);
        Assert.DoesNotContain("Честерфілд", text.Replace("списання\n(ОП Честерфілд)", ""));
        Assert.StartsWith("списання", text);
    }

    [Fact]
    public void ResolveCounterparty_receipt_uses_supply_service_not_unit()
    {
        var doc = new Document { Type = DocType.Receipt, Status = DocStatus.Posted };
        Assert.Equal("СОВТ та МСВ",
            AccountingBook.ResolveCounterparty(doc, "Склад інженерної служби", "СОВТ та МСВ"));
        Assert.Equal("ІС ССП",
            AccountingBook.ResolveCounterparty(doc, "Склад", "ІС ССП"));
        Assert.Equal("—",
            AccountingBook.ResolveCounterparty(doc, "Склад", null));
        Assert.Equal("—",
            AccountingBook.ResolveCounterparty(doc, "Склад", ""));
    }

    [Fact]
    public void ResolveCounterparty_barrier_and_inventory_unchanged()
    {
        var barrier = new Document
        {
            Type = DocType.QualityChange,
            BarrierName = "ЗЯС «Альфа»",
            Status = DocStatus.Posted
        };
        Assert.Equal("ЗЯС «Альфа»", AccountingBook.ResolveCounterparty(barrier, "Підрозділ", "СЗУ"));

        var inv = new Document { Type = DocType.Inventory, Status = DocStatus.Posted };
        Assert.Equal("Мій підрозділ",
            AccountingBook.ResolveCounterparty(inv, "Мій підрозділ", "СЗУ"));
    }

    [Fact]
    public void BuildSection_receipt_counterparty_is_property_supply_service()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m,
            Quantity = 10m,
            NomenclatureId = nom.Id,
            SupplyService = "ІС ССП"
        };
        var receiptId = Guid.NewGuid();
        var receipt = new Document
        {
            Id = receiptId,
            Type = DocType.Receipt,
            Number = "0007",
            Date = new DateTime(2025, 2, 1),
            Status = DocStatus.Posted
        };
        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptId,
                DocumentDate = receipt.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "0007",
                PropertyId = prop.Id,
                PropertyName = prop.Name,
                QuantityDelta = 10m,
                At = receipt.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { receipt },
            unitName: "Склад інженерної служби");

        Assert.Single(section.Rows);
        Assert.Equal("ІС ССП", section.Rows[0].Counterparty);
    }

    [Fact]
    public void BuildSection_receipt_without_property_supply_is_dash()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m,
            Quantity = 10m,
            NomenclatureId = nom.Id,
            SupplyService = ""
        };
        var receiptId = Guid.NewGuid();
        var receipt = new Document
        {
            Id = receiptId,
            Type = DocType.Receipt,
            Number = "0008",
            Date = new DateTime(2025, 2, 2),
            Status = DocStatus.Posted
        };
        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptId,
                DocumentDate = receipt.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "0008",
                PropertyId = prop.Id,
                QuantityDelta = 3m,
                At = receipt.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { receipt },
            unitName: "Склад");

        Assert.Single(section.Rows);
        Assert.Equal("—", section.Rows[0].Counterparty);
    }

    [Fact]
    public void BuildSection_quality_state_counterparty_is_op_two_line_format()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Брус", Vyd = "Пиломатеріали", Unit = "м3" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Брус",
            Unit = "м3",
            UnitPrice = 50m,
            Quantity = 0m,
            NomenclatureId = nom.Id
        };
        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            Type = DocType.QualityState,
            Number = "ЯС-1",
            Date = new DateTime(2025, 5, 1),
            Status = DocStatus.Posted,
            StrongpointName = "Північ"
        };
        var movements = new[]
        {
            new Movement
            {
                DocumentId = docId,
                DocumentDate = doc.Date,
                DocumentType = DocType.QualityState,
                DocumentNumber = "ЯС-1",
                PropertyId = prop.Id,
                QuantityDelta = -5m,
                At = doc.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { doc },
            unitName: "Підрозділ");

        Assert.Single(section.Rows);
        Assert.Equal("списання\n(ОП Північ)", section.Rows[0].Counterparty);
    }

    [Fact]
    public void BuildSection_columns_ordered_by_first_inbound_not_alphabet()
    {
        // Алфавіт: «Брус» перед «Дошка»; перше надійшло — навпаки (Дошка раніше).
        var nomA = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var nomB = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Брус", Vyd = "Пиломатеріали", Unit = "м3" };
        var propA = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m,
            Quantity = 10m,
            NomenclatureId = nomA.Id
        };
        var propB = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Брус",
            Unit = "м3",
            Category = "І",
            UnitPrice = 50m,
            Quantity = 5m,
            NomenclatureId = nomB.Id
        };

        var receiptAId = Guid.NewGuid();
        var receiptBId = Guid.NewGuid();
        var receiptA = new Document
        {
            Id = receiptAId,
            Type = DocType.Receipt,
            Number = "A1",
            Date = new DateTime(2025, 1, 10),
            Status = DocStatus.Posted
        };
        var receiptB = new Document
        {
            Id = receiptBId,
            Type = DocType.Receipt,
            Number = "B1",
            Date = new DateTime(2025, 1, 20),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptAId,
                DocumentDate = receiptA.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "A1",
                PropertyId = propA.Id,
                QuantityDelta = 10m,
                At = receiptA.Date
            },
            new Movement
            {
                DocumentId = receiptBId,
                DocumentDate = receiptB.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "B1",
                PropertyId = propB.Id,
                QuantityDelta = 5m,
                At = receiptB.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { propB, propA },
            new[] { nomB, nomA },
            movements,
            new[] { receiptB, receiptA });

        Assert.Equal(2, section.Columns.Count);
        Assert.Equal("Дошка", section.Columns[0].Key.Name);
        Assert.Equal("Брус", section.Columns[1].Key.Name);
        // Рядки хронології не зламані: спочатку A (10.01), потім B (20.01).
        Assert.Equal(receiptAId, section.Rows[0].DocumentId);
        Assert.Equal(receiptBId, section.Rows[1].DocumentId);
    }

    [Fact]
    public void ApplyPresenceFilter_keeps_only_columns_with_stanovyt_gt_zero()
    {
        var nomKeep = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var nomGone = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Брус", Vyd = "Пиломатеріали", Unit = "м3" };
        var propKeep = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m,
            Quantity = 30m,
            ReservedQuantity = 10m,
            NomenclatureId = nomKeep.Id
        };
        var propGone = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Брус",
            Unit = "м3",
            Category = "І",
            UnitPrice = 50m,
            Quantity = 0m,
            NomenclatureId = nomGone.Id
        };

        var inKeepId = Guid.NewGuid();
        var inGoneId = Guid.NewGuid();
        var outGoneId = Guid.NewGuid();
        var inKeep = new Document
        {
            Id = inKeepId,
            Type = DocType.Receipt,
            Number = "K1",
            Date = new DateTime(2025, 1, 1),
            Status = DocStatus.Posted
        };
        var inGone = new Document
        {
            Id = inGoneId,
            Type = DocType.Receipt,
            Number = "G1",
            Date = new DateTime(2025, 1, 2),
            Status = DocStatus.Posted
        };
        var outGone = new Document
        {
            Id = outGoneId,
            Type = DocType.Transfer,
            Number = "G2",
            Date = new DateTime(2025, 2, 1),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = inKeepId,
                DocumentDate = inKeep.Date,
                DocumentType = DocType.Receipt,
                PropertyId = propKeep.Id,
                QuantityDelta = 40m,
                At = inKeep.Date
            },
            new Movement
            {
                DocumentId = inGoneId,
                DocumentDate = inGone.Date,
                DocumentType = DocType.Receipt,
                PropertyId = propGone.Id,
                QuantityDelta = 20m,
                At = inGone.Date
            },
            new Movement
            {
                DocumentId = outGoneId,
                DocumentDate = outGone.Date,
                DocumentType = DocType.Transfer,
                PropertyId = propGone.Id,
                QuantityDelta = -20m,
                At = outGone.Date
            }
        };

        var full = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { propKeep, propGone },
            new[] { nomKeep, nomGone },
            movements,
            new[] { inKeep, inGone, outGone });

        Assert.Equal(2, full.Columns.Count);
        Assert.Equal(40m, AccountingBook.CurrentBalance(full, AccountingBook.ColumnKeyOf(propKeep)));
        Assert.Equal(0m, AccountingBook.CurrentBalance(full, AccountingBook.ColumnKeyOf(propGone)));

        var filtered = AccountingBook.ApplyPresenceFilter(full, onlyPositiveBalance: true);
        Assert.Single(filtered.Columns);
        Assert.Equal("Дошка", filtered.Columns[0].Key.Name);
        Assert.Equal(3, filtered.Rows.Count); // проводки ті самі

        var allAgain = AccountingBook.ApplyPresenceFilter(full, onlyPositiveBalance: false);
        Assert.Equal(2, allAgain.Columns.Count);
        Assert.Same(full, allAgain);
    }

    [Fact]
    public void ApplyPresenceFilter_hides_column_when_stanovyt_rounds_to_zero_on_screen()
    {
        // Пилинка менша за крок формату кількості (3 знаки) — на екрані «0,000», ховати.
        var dust = 0.0004m;
        Assert.True(dust > 0m);
        Assert.Equal(0m, decimal.Round(dust, 3, MidpointRounding.AwayFromZero));

        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Плівка", Vyd = "Плівка", Unit = "пгм" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Плівка 1500",
            Unit = "пгм",
            Category = "І",
            UnitPrice = 37.4m,
            Quantity = 0m,
            NomenclatureId = nom.Id
        };
        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            Type = DocType.Receipt,
            Number = "P1",
            Date = new DateTime(2026, 3, 1),
            Status = DocStatus.Posted
        };
        var section = AccountingBook.BuildSection(
            "Плівка",
            new[] { prop },
            new[] { nom },
            new[]
            {
                new Movement
                {
                    DocumentId = docId,
                    DocumentDate = doc.Date,
                    DocumentType = DocType.Receipt,
                    PropertyId = prop.Id,
                    QuantityDelta = dust,
                    At = doc.Date
                }
            },
            new[] { doc });

        Assert.Equal(dust, AccountingBook.CurrentBalance(section, AccountingBook.ColumnKeyOf(prop)));
        Assert.False(AccountingBook.HasPositiveStanovyt(section, AccountingBook.ColumnKeyOf(prop)));
        var filtered = AccountingBook.ApplyPresenceFilter(section, onlyPositiveBalance: true);
        Assert.Empty(filtered.Columns);
    }

    [Fact]
    public void ApplyPresenceFilter_after_period_inbound_hides_zero_final_stanovyt()
    {
        // Як Плівка 1500ММ: у періоді було надійшло, потім вибуло в нуль — колонка зникає з фільтром.
        var nomKeep = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Плівка 1000", Vyd = "Плівка", Unit = "пгм" };
        var nomZero = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Плівка 1500", Vyd = "Плівка", Unit = "пгм" };
        var propKeep = new PropertyItem
        {
            Id = Guid.NewGuid(), Name = "Плівка 1000", Unit = "пгм", Category = "І",
            UnitPrice = 51.24m, Quantity = 1750m, NomenclatureId = nomKeep.Id
        };
        var propZero = new PropertyItem
        {
            Id = Guid.NewGuid(), Name = "Плівка 1500", Unit = "пгм", Category = "І",
            UnitPrice = 37.4m, Quantity = 0m, NomenclatureId = nomZero.Id
        };

        var inKeepId = Guid.NewGuid();
        var inZeroId = Guid.NewGuid();
        var outZeroId = Guid.NewGuid();
        var docs = new[]
        {
            new Document { Id = inKeepId, Type = DocType.Receipt, Number = "1", Date = new DateTime(2026, 2, 1), Status = DocStatus.Posted },
            new Document { Id = inZeroId, Type = DocType.Receipt, Number = "2", Date = new DateTime(2026, 2, 2), Status = DocStatus.Posted },
            new Document { Id = outZeroId, Type = DocType.Transfer, Number = "3", Date = new DateTime(2026, 3, 1), Status = DocStatus.Posted }
        };
        var moves = new[]
        {
            new Movement { DocumentId = inKeepId, DocumentDate = docs[0].Date, DocumentType = DocType.Receipt, PropertyId = propKeep.Id, QuantityDelta = 1750m, At = docs[0].Date },
            new Movement { DocumentId = inZeroId, DocumentDate = docs[1].Date, DocumentType = DocType.Receipt, PropertyId = propZero.Id, QuantityDelta = 100m, At = docs[1].Date },
            new Movement { DocumentId = outZeroId, DocumentDate = docs[2].Date, DocumentType = DocType.Transfer, PropertyId = propZero.Id, QuantityDelta = -100m, At = docs[2].Date }
        };

        var full = AccountingBook.BuildSection(
            "Плівка",
            new[] { propKeep, propZero },
            new[] { nomKeep, nomZero },
            moves,
            docs,
            periodFrom: new DateTime(2026, 1, 1),
            periodTo: new DateTime(2026, 10, 26));

        Assert.Equal(2, full.Columns.Count);
        Assert.Equal(1750m, AccountingBook.CurrentBalance(full, AccountingBook.ColumnKeyOf(propKeep)));
        Assert.Equal(0m, AccountingBook.CurrentBalance(full, AccountingBook.ColumnKeyOf(propZero)));
        Assert.True(AccountingBook.HasPositiveStanovyt(full, AccountingBook.ColumnKeyOf(propKeep)));
        Assert.False(AccountingBook.HasPositiveStanovyt(full, AccountingBook.ColumnKeyOf(propZero)));

        var filtered = AccountingBook.ApplyPresenceFilter(full, onlyPositiveBalance: true);
        Assert.Single(filtered.Columns);
        Assert.Equal("Плівка 1000", filtered.Columns[0].Key.Name);
    }

    [Fact]
    public void Layout_property_group_left_matches_triplet_and_body_col_left()
    {
        // Межі назва ↔ mid ↔ трійка ↔ тіло — ті самі X на будь-якій ширині в’юпорта.
        foreach (var viewport in new[] { 320.0, 900.0, 1600.0, 2560.0 })
        {
            foreach (var cols in new[] { 1, 2, 8, 20 })
            {
                AccountingBookLayout.UpdateQtyColWidth(cols, viewport);
                Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);
                for (var i = 0; i < cols; i++)
                {
                    var baseCol = AccountingBookLayout.MetaCols + i * 3;
                    Assert.Equal(
                        AccountingBookLayout.ColLeft(baseCol),
                        AccountingBookLayout.PropertyGroupLeft(i));
                    Assert.Equal(
                        AccountingBookLayout.ColLeft(baseCol) + AccountingBookLayout.PropertyGroupWidth,
                        AccountingBookLayout.ColLeft(baseCol + 3));
                    Assert.True(AccountingBookLayout.IsPropertyGroupBoundary(baseCol));
                    Assert.False(AccountingBookLayout.IsPropertyGroupBoundary(baseCol + 1));
                    Assert.False(AccountingBookLayout.IsPropertyGroupBoundary(baseCol + 2));
                }
            }
        }
    }

    [Fact]
    public void Layout_pad_must_use_viewport_not_actual_with_scrollbar_gutter()
    {
        // Регресія: pad від ActualWidth (viewport+бігунок) зсуває sticky на ~sb/2
        // відносно тіла саме коли фільтр лишає мало колонок на широкому екрані.
        var cols = 3;
        AccountingBookLayout.UpdateQtyColWidth(cols, 0);
        var content = AccountingBookLayout.ContentWidth(cols);
        var viewport = content + 400;
        var scrollbar = 17.0;
        var actual = viewport + scrollbar;

        var padVp = AccountingBookLayout.ContentLeftPad(cols, viewport);
        var padActual = AccountingBookLayout.ContentLeftPad(cols, actual);
        Assert.True(padVp > 0);
        Assert.Equal(Math.Floor(scrollbar / 2), padActual - padVp, 0);
        Assert.Equal(scrollbar, AccountingBookLayout.ScrollBarGutter(actual, viewport));
        Assert.Equal(0, AccountingBookLayout.ScrollBarGutter(viewport, viewport));

        // Sticky Canvas.Left = pad − H; при H=0 збігається з Margin тіла.
        Assert.Equal(padVp, AccountingBookLayout.StickyCanvasLeft(padVp, 0));
        Assert.Equal(padVp - 40, AccountingBookLayout.StickyCanvasLeft(padVp, 40));

        // Спільні X назва/mid/трійка/тіло при цьому pad (без stretch qty).
        Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);
        for (var i = 0; i < cols; i++)
        {
            var baseCol = AccountingBookLayout.MetaCols + i * 3;
            var groupLeft = padVp + AccountingBookLayout.PropertyGroupLeft(i);
            Assert.Equal(groupLeft, padVp + AccountingBookLayout.ColLeft(baseCol));
            Assert.Equal(groupLeft + AccountingBookLayout.QtyColW,
                padVp + AccountingBookLayout.ColLeft(baseCol + 1));
            Assert.Equal(groupLeft + 2 * AccountingBookLayout.QtyColW,
                padVp + AccountingBookLayout.ColLeft(baseCol + 2));
            Assert.Equal(groupLeft + AccountingBookLayout.PropertyGroupWidth,
                padVp + AccountingBookLayout.ColLeft(baseCol + 3));
        }
    }

    [Fact]
    public void BuildSection_every_row_has_all_columns_with_dash_and_carried_balance()
    {
        var nomA = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var nomB = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Брус", Vyd = "Пиломатеріали", Unit = "м3" };
        var propA = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m,
            Quantity = 80m,
            NomenclatureId = nomA.Id
        };
        var propB = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Брус",
            Unit = "м3",
            Category = "І",
            UnitPrice = 50m,
            Quantity = 50m,
            NomenclatureId = nomB.Id
        };

        var receiptAId = Guid.NewGuid();
        var receiptBId = Guid.NewGuid();
        var transferAId = Guid.NewGuid();
        var receiptA = new Document
        {
            Id = receiptAId,
            Type = DocType.Receipt,
            Number = "A1",
            Date = new DateTime(2025, 1, 1),
            Status = DocStatus.Posted
        };
        var receiptB = new Document
        {
            Id = receiptBId,
            Type = DocType.Receipt,
            Number = "B1",
            Date = new DateTime(2025, 1, 2),
            Status = DocStatus.Posted
        };
        var transferA = new Document
        {
            Id = transferAId,
            Type = DocType.Transfer,
            Number = "T1",
            Date = new DateTime(2025, 2, 1),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptAId,
                DocumentDate = receiptA.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "A1",
                PropertyId = propA.Id,
                PropertyName = propA.Name,
                QuantityDelta = 100m,
                At = receiptA.Date
            },
            new Movement
            {
                DocumentId = receiptBId,
                DocumentDate = receiptB.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "B1",
                PropertyId = propB.Id,
                PropertyName = propB.Name,
                QuantityDelta = 50m,
                At = receiptB.Date
            },
            new Movement
            {
                DocumentId = transferAId,
                DocumentDate = transferA.Date,
                DocumentType = DocType.Transfer,
                DocumentNumber = "T1",
                PropertyId = propA.Id,
                PropertyName = propA.Name,
                QuantityDelta = -20m,
                At = transferA.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { propA, propB },
            new[] { nomA, nomB },
            movements,
            new[] { receiptA, receiptB, transferA });

        Assert.Equal(2, section.Columns.Count);
        Assert.Equal(3, section.Rows.Count);

        var keyA = AccountingBook.ColumnKeyOf(propA);
        var keyB = AccountingBook.ColumnKeyOf(propB);

        // Рядок 0: лише A надійшло; B — прочерк, становить 0 (ще не було руху).
        Assert.Equal(100m, section.Rows[0].Cells[keyA].InQty);
        Assert.Null(section.Rows[0].Cells[keyA].OutQty);
        Assert.Equal(100m, section.Rows[0].Cells[keyA].Balance);
        Assert.Null(section.Rows[0].Cells[keyB].InQty);
        Assert.Null(section.Rows[0].Cells[keyB].OutQty);
        Assert.Equal(0m, section.Rows[0].Cells[keyB].Balance);
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[0].Cells[keyB].InQty));
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[0].Cells[keyB].OutQty));
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[0].Cells[keyA].OutQty));

        // Рядок 1: лише B надійшло; A — прочерк, становить 100 (протяжка).
        Assert.Null(section.Rows[1].Cells[keyA].InQty);
        Assert.Null(section.Rows[1].Cells[keyA].OutQty);
        Assert.Equal(100m, section.Rows[1].Cells[keyA].Balance);
        Assert.Equal(50m, section.Rows[1].Cells[keyB].InQty);
        Assert.Null(section.Rows[1].Cells[keyB].OutQty);
        Assert.Equal(50m, section.Rows[1].Cells[keyB].Balance);
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[1].Cells[keyA].InQty));

        // Рядок 2: лише A вибуло; B — прочерк, становить 50 (протяжка).
        Assert.Null(section.Rows[2].Cells[keyA].InQty);
        Assert.Equal(20m, section.Rows[2].Cells[keyA].OutQty);
        Assert.Equal(80m, section.Rows[2].Cells[keyA].Balance);
        Assert.Null(section.Rows[2].Cells[keyB].InQty);
        Assert.Null(section.Rows[2].Cells[keyB].OutQty);
        Assert.Equal(50m, section.Rows[2].Cells[keyB].Balance);
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[2].Cells[keyB].InQty));
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[2].Cells[keyB].OutQty));
        Assert.Equal("—", AccountingBookLayout.QtyText(section.Rows[2].Cells[keyA].InQty));

        // У кожному рядку — усі колонки розділу.
        foreach (var row in section.Rows)
        {
            Assert.Equal(section.Columns.Count, row.Cells.Count);
            foreach (var col in section.Columns)
                Assert.True(row.Cells.ContainsKey(col.Key));
        }
    }

    [Fact]
    public void SupplyServiceHelper_known_and_map_from_peredaye()
    {
        Assert.True(SupplyServiceHelper.IsKnown("СОВТ та МСВ"));
        Assert.True(SupplyServiceHelper.IsKnown("ІС ССП"));
        Assert.True(SupplyServiceHelper.IsKnown("СЗУ"));
        Assert.False(SupplyServiceHelper.IsKnown("Інше"));
        Assert.Null(SupplyServiceHelper.Normalize(""));
        Assert.Equal("СЗУ", SupplyServiceHelper.Normalize("  СЗУ  "));

        Assert.Equal("СОВТ та МСВ", SupplyServiceHelper.MapFromPeredaye("СОВТ та МСВ"));
        Assert.Equal("ІС ССП", SupplyServiceHelper.MapFromPeredaye("ІС ССП"));
        Assert.Equal("СЗУ", SupplyServiceHelper.MapFromPeredaye("СЗУ"));
        Assert.Equal("ІС ССП", SupplyServiceHelper.MapFromPeredaye("Інженерний склад"));
        Assert.Equal("ІС ССП", SupplyServiceHelper.MapFromPeredaye("інженерна служба"));
        Assert.Equal("ІС ССП", SupplyServiceHelper.MapFromPeredaye("Інженерний склад в/ч А4638"));
        Assert.Null(SupplyServiceHelper.MapFromPeredaye("Склад №3"));
        Assert.Null(SupplyServiceHelper.MapFromPeredaye(""));
        Assert.Null(SupplyServiceHelper.MapFromPeredaye(null));
    }

    [Fact]
    public void Layout_property_group_matches_three_qty_columns()
    {
        AccountingBookLayout.UpdateQtyColWidth(3, 0); // мінімальні ширини
        Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);

        for (var i = 0; i < 5; i++)
        {
            var baseCol = AccountingBookLayout.MetaCols + i * 3;
            Assert.Equal(AccountingBookLayout.ColLeft(baseCol), AccountingBookLayout.PropertyGroupLeft(i));
            Assert.Equal(
                AccountingBookLayout.ColWidth(baseCol)
                + AccountingBookLayout.ColWidth(baseCol + 1)
                + AccountingBookLayout.ColWidth(baseCol + 2),
                AccountingBookLayout.PropertyGroupWidth);
            Assert.Equal(
                AccountingBookLayout.ColLeft(baseCol) + AccountingBookLayout.PropertyGroupWidth,
                AccountingBookLayout.ColLeft(baseCol + 3));
        }
    }

    [Fact]
    public void Layout_wide_viewport_keeps_min_qty_and_centers_with_pad()
    {
        const int cols = 4;
        var min = AccountingBookLayout.MinContentWidth(cols);
        var wide = min + 400;
        AccountingBookLayout.UpdateQtyColWidth(cols, wide);

        Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);
        Assert.Equal(min, AccountingBookLayout.ContentWidth(cols), 3);
        Assert.Equal(Math.Floor((wide - min) / 2), AccountingBookLayout.ContentLeftPad(cols, wide));

        // Назва (група) = рівно 3 qty-колонки тіла.
        Assert.Equal(3 * AccountingBookLayout.QtyColW, AccountingBookLayout.PropertyGroupWidth);
        Assert.Equal(
            AccountingBookLayout.MetaBlockWidth + 2 * AccountingBookLayout.PropertyGroupWidth,
            AccountingBookLayout.PropertyGroupLeft(2));

        AccountingBookLayout.UpdateQtyColWidth(cols, min - 10);
        Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);
        Assert.Equal(0, AccountingBookLayout.ContentLeftPad(cols, min - 10));
    }

    [Fact]
    public void Layout_frozen_meta_content_x_compensates_h_offset()
    {
        AccountingBookLayout.UpdateQtyColWidth(2, 0);
        const double hOff = 120;
        for (var col = 0; col < AccountingBookLayout.MetaCols; col++)
        {
            Assert.Equal(
                AccountingBookLayout.ColLeft(col) + hOff,
                AccountingBookLayout.FrozenMetaContentX(col, hOff));
        }
        // Після зсуву −hOff візуальний X = ColLeft (H-freeze).
        Assert.Equal(
            AccountingBookLayout.ColLeft(0),
            AccountingBookLayout.FrozenMetaContentX(0, hOff) - hOff);
        Assert.Equal(
            AccountingBookLayout.MetaBlockWidth,
            AccountingBookLayout.FrozenMetaContentX(AccountingBookLayout.MetaCols - 1, hOff)
                + AccountingBookLayout.ColWidth(AccountingBookLayout.MetaCols - 1)
                - hOff);
    }

    [Fact]
    public void Layout_full_header_is_name_then_mid_then_triplet()
    {
        Assert.Equal(
            AccountingBookLayout.MidHeaderRows * AccountingBookLayout.HeaderMidRowH,
            AccountingBookLayout.MidBlockH);
        // Повна шапка: mid у тілі одразу; трійка під mid, не під назвою.
        Assert.Equal(0, AccountingBookLayout.BodyTripletTop - AccountingBookLayout.MidBlockH);
        Assert.Equal(
            AccountingBookLayout.MidBlockH + AccountingBookLayout.MoveRowH,
            AccountingBookLayout.BodyDataTop);
        Assert.True(AccountingBookLayout.BodyTripletTop > 0);
        Assert.True(AccountingBookLayout.BodyDataTop > AccountingBookLayout.BodyTripletTop);

        Assert.Equal(AccountingBookLayout.NameRowH, AccountingBookLayout.StickySurfaceHeight(false));
        Assert.Equal(
            AccountingBookLayout.NameRowH + AccountingBookLayout.MoveRowH,
            AccountingBookLayout.StickySurfaceHeight(true));
        Assert.False(AccountingBookLayout.StickyMetaDrawsBottomBorder(false));
        Assert.True(AccountingBookLayout.StickyMetaDrawsBottomBorder(true));

        Assert.Equal(
            AccountingBookLayout.BodyDataTop + 3 * AccountingBookLayout.DataRowH,
            AccountingBookLayout.BodyHeight(3));
    }

    [Fact]
    public void Layout_pin_triplet_after_mid_leaves_and_compensates()
    {
        var mid = AccountingBookLayout.MidBlockH;
        Assert.False(AccountingBookLayout.ShouldPinTriplet(0, currentlyPinned: false));
        Assert.False(AccountingBookLayout.ShouldPinTriplet(mid - 1, currentlyPinned: false));
        Assert.True(AccountingBookLayout.ShouldPinTriplet(mid, currentlyPinned: false));
        Assert.True(AccountingBookLayout.ShouldPinTriplet(mid + AccountingBookLayout.MoveRowH, currentlyPinned: true));
        Assert.False(AccountingBookLayout.ShouldPinTriplet(
            mid + AccountingBookLayout.MoveRowH - 1, currentlyPinned: true));
    }

    [Fact]
    public void Layout_narrow_section_group_matches_qty_like_wide()
    {
        const int cols = 1;
        AccountingBookLayout.UpdateQtyColWidth(cols, 320); // вужче за min
        Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);
        Assert.Equal(AccountingBookLayout.ColLeft(AccountingBookLayout.MetaCols),
            AccountingBookLayout.PropertyGroupLeft(0));
        Assert.Equal(3 * AccountingBookLayout.QtyColW, AccountingBookLayout.PropertyGroupWidth);
        Assert.Equal(
            AccountingBookLayout.PropertyGroupLeft(0) + AccountingBookLayout.PropertyGroupWidth,
            AccountingBookLayout.ColLeft(AccountingBookLayout.MetaCols + 3));

        var wideEnough = AccountingBookLayout.MinContentWidth(cols) + 120;
        AccountingBookLayout.UpdateQtyColWidth(cols, wideEnough);
        Assert.Equal(AccountingBookLayout.MinQtyColW, AccountingBookLayout.QtyColW);
        Assert.Equal(AccountingBookLayout.MinContentWidth(cols), AccountingBookLayout.ContentWidth(cols), 3);
        Assert.Equal(3 * AccountingBookLayout.QtyColW, AccountingBookLayout.PropertyGroupWidth);
        Assert.Equal(
            AccountingBookLayout.ColLeft(AccountingBookLayout.MetaCols),
            AccountingBookLayout.PropertyGroupLeft(0));
        Assert.True(AccountingBookLayout.ContentLeftPad(cols, wideEnough) > 0);
    }

    [Fact]
    public void Layout_mid_spans_property_group_boundaries_only()
    {
        AccountingBookLayout.UpdateQtyColWidth(2, 0);
        Assert.True(AccountingBookLayout.IsPropertyGroupBoundary(AccountingBookLayout.MetaCols));
        Assert.False(AccountingBookLayout.IsPropertyGroupBoundary(AccountingBookLayout.MetaCols + 1));
        Assert.False(AccountingBookLayout.IsPropertyGroupBoundary(AccountingBookLayout.MetaCols + 2));
        Assert.True(AccountingBookLayout.IsPropertyGroupBoundary(AccountingBookLayout.MetaCols + 3));
        // Mid малюється на PropertyGroupWidth (= 3 qty), як назва.
        Assert.Equal(3 * AccountingBookLayout.QtyColW, AccountingBookLayout.PropertyGroupWidth);
    }

    [Fact]
    public void BuildSection_period_filters_rows_keeps_opening_balance()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 3900m,
            Quantity = 90m,
            NomenclatureId = nom.Id
        };

        var receiptOldId = Guid.NewGuid();
        var receiptInId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var receiptOld = new Document
        {
            Id = receiptOldId,
            Type = DocType.Receipt,
            Number = "0001",
            Date = new DateTime(2025, 1, 8),
            Status = DocStatus.Posted
        };
        // «Надійшло» в періоді потрібне, щоб колонка лишилась видимою.
        var receiptIn = new Document
        {
            Id = receiptInId,
            Type = DocType.Receipt,
            Number = "0002",
            Date = new DateTime(2026, 2, 1),
            Status = DocStatus.Posted
        };
        var transfer = new Document
        {
            Id = transferId,
            Type = DocType.Transfer,
            Number = "0010",
            Date = new DateTime(2026, 3, 4),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptOldId,
                DocumentDate = receiptOld.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "0001",
                PropertyId = prop.Id,
                PropertyName = prop.Name,
                QuantityDelta = 100m,
                At = receiptOld.Date
            },
            new Movement
            {
                DocumentId = receiptInId,
                DocumentDate = receiptIn.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "0002",
                PropertyId = prop.Id,
                PropertyName = prop.Name,
                QuantityDelta = 10m,
                At = receiptIn.Date
            },
            new Movement
            {
                DocumentId = transferId,
                DocumentDate = transfer.Date,
                DocumentType = DocType.Transfer,
                DocumentNumber = "0010",
                PropertyId = prop.Id,
                PropertyName = prop.Name,
                QuantityDelta = -20m,
                At = transfer.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { receiptOld, receiptIn, transfer },
            "Склад",
            periodFrom: new DateTime(2026, 1, 1),
            periodTo: new DateTime(2026, 10, 25));

        Assert.Equal(2, section.Rows.Count);
        var key = section.Columns[0].Key;
        Assert.Equal(10m, section.Rows[0].Cells[key].InQty);
        // Баланс після приходу в періоді враховує 100 поза «Від».
        Assert.Equal(110m, section.Rows[0].Cells[key].Balance);
        Assert.Equal(20m, section.Rows[1].Cells[key].OutQty);
        Assert.Equal(90m, section.Rows[1].Cells[key].Balance);
    }

    [Fact]
    public void BuildSection_period_keeps_only_columns_with_inbound_in_period()
    {
        var nomKeep = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var nomHide = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Брус", Vyd = "Пиломатеріали", Unit = "м3" };
        var propKeep = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 100m,
            Quantity = 10m,
            NomenclatureId = nomKeep.Id
        };
        var propHide = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Брус",
            Unit = "м3",
            Category = "І",
            UnitPrice = 50m,
            Quantity = 3m,
            NomenclatureId = nomHide.Id
        };

        var keepInId = Guid.NewGuid();
        var hideOldInId = Guid.NewGuid();
        var hideOutId = Guid.NewGuid();
        var keepIn = new Document
        {
            Id = keepInId,
            Type = DocType.Receipt,
            Number = "K1",
            Date = new DateTime(2026, 5, 10),
            Status = DocStatus.Posted
        };
        var hideOldIn = new Document
        {
            Id = hideOldInId,
            Type = DocType.Receipt,
            Number = "H0",
            Date = new DateTime(2025, 6, 1),
            Status = DocStatus.Posted
        };
        var hideOut = new Document
        {
            Id = hideOutId,
            Type = DocType.Transfer,
            Number = "H1",
            Date = new DateTime(2026, 5, 15),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = hideOldInId,
                DocumentDate = hideOldIn.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "H0",
                PropertyId = propHide.Id,
                QuantityDelta = 5m,
                At = hideOldIn.Date
            },
            new Movement
            {
                DocumentId = keepInId,
                DocumentDate = keepIn.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "K1",
                PropertyId = propKeep.Id,
                QuantityDelta = 10m,
                At = keepIn.Date
            },
            new Movement
            {
                DocumentId = hideOutId,
                DocumentDate = hideOut.Date,
                DocumentType = DocType.Transfer,
                DocumentNumber = "H1",
                PropertyId = propHide.Id,
                QuantityDelta = -2m,
                At = hideOut.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { propKeep, propHide },
            new[] { nomKeep, nomHide },
            movements,
            new[] { keepIn, hideOldIn, hideOut },
            periodFrom: new DateTime(2026, 1, 1),
            periodTo: new DateTime(2026, 10, 25));

        Assert.Single(section.Columns);
        Assert.Equal("Дошка", section.Columns[0].Key.Name);
        // Рядок лише вибуло по «Брус» (без In у періоді) — не показуємо.
        Assert.Single(section.Rows);
        Assert.Equal(keepInId, section.Rows[0].DocumentId);
        Assert.Equal(10m, section.Rows[0].Cells[section.Columns[0].Key].InQty);

        // Без періоду — обидві колонки (як раніше).
        var all = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { propKeep, propHide },
            new[] { nomKeep, nomHide },
            movements,
            new[] { keepIn, hideOldIn, hideOut });
        Assert.Equal(2, all.Columns.Count);
    }

    [Fact]
    public void BuildSection_period_without_inbound_hides_column_despite_outbound()
    {
        var nom = new NomenclatureItem { Id = Guid.NewGuid(), Name = "Дошка", Vyd = "Пиломатеріали", Unit = "м3" };
        var prop = new PropertyItem
        {
            Id = Guid.NewGuid(),
            Name = "Дошка",
            Unit = "м3",
            Category = "І",
            UnitPrice = 3900m,
            Quantity = 80m,
            NomenclatureId = nom.Id
        };

        var receiptId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var receipt = new Document
        {
            Id = receiptId,
            Type = DocType.Receipt,
            Number = "0001",
            Date = new DateTime(2025, 1, 8),
            Status = DocStatus.Posted
        };
        var transfer = new Document
        {
            Id = transferId,
            Type = DocType.Transfer,
            Number = "0010",
            Date = new DateTime(2026, 3, 4),
            Status = DocStatus.Posted
        };

        var movements = new[]
        {
            new Movement
            {
                DocumentId = receiptId,
                DocumentDate = receipt.Date,
                DocumentType = DocType.Receipt,
                DocumentNumber = "0001",
                PropertyId = prop.Id,
                QuantityDelta = 100m,
                At = receipt.Date
            },
            new Movement
            {
                DocumentId = transferId,
                DocumentDate = transfer.Date,
                DocumentType = DocType.Transfer,
                DocumentNumber = "0010",
                PropertyId = prop.Id,
                QuantityDelta = -20m,
                At = transfer.Date
            }
        };

        var section = AccountingBook.BuildSection(
            "Пиломатеріали",
            new[] { prop },
            new[] { nom },
            movements,
            new[] { receipt, transfer },
            periodFrom: new DateTime(2026, 1, 1),
            periodTo: new DateTime(2026, 10, 25));

        Assert.Empty(section.Columns);
        Assert.Empty(section.Rows);
    }

    private static PropertyItem Prop(string name, string category, decimal price) =>
        new()
        {
            Name = name,
            Category = category,
            UnitPrice = price,
            Unit = "шт"
        };
}
