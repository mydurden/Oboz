using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Obozny;

public partial class DocumentViewWindow : Window
{
    private static readonly SolidColorBrush GroupStripe = Freeze(0xE3, 0xF2, 0xE8);
    private static readonly SolidColorBrush DarkOnLight = Freeze(0x1A, 0x1A, 0x1A);

    private readonly Document _doc;
    private readonly Guid? _highlightPropertyId;
    public bool OpenAsDraft { get; private set; }
    public bool Deleted { get; private set; }
    /// <summary>Якщо з картки ОП відкрили інший акт як чернетку.</summary>
    public Document? DraftToOpen { get; private set; }

    public DocumentViewWindow(Document doc, Guid? highlightPropertyId = null)
    {
        InitializeComponent();
        _doc = doc;
        _highlightPropertyId = highlightPropertyId;

        var docTitle = DocTypeNames.Title(doc.Type);
        var num = DocTypeNames.FormatNumber(doc.Number);
        Title = $"{docTitle} № {num}";
        TitleText.Text = string.IsNullOrWhiteSpace(doc.WorkTypeName)
            ? $"{docTitle} № {num}"
            : $"{docTitle} № {num}\n{doc.WorkTypeName}";
        InfoText.Text = BuildInfo(doc);
        ApplyNoteButton(doc.Note);

        // Чернетка — «Відкрити чернетку»; проведений ЯС/ЗЯС — «Редагувати» (дата/№ ВР лише в чернетці).
        if (doc.Status == DocStatus.Draft && DocTypeNames.DecreasesStock(doc.Type))
            EditDraftBtn.Visibility = Visibility.Visible;
        else if (VrActBuilder.IsPairedSource(doc) && doc.Status == DocStatus.Posted)
            EditPostedBtn.Visibility = Visibility.Visible;

        if (doc.Type == DocType.QualityState)
        {
            BindQualityStateTable();
            var hasOp = !string.IsNullOrWhiteSpace(doc.StrongpointName)
                        || !string.IsNullOrWhiteSpace(doc.StrongpointRegNumber);
            OpCardBtn.Visibility = hasOp ? Visibility.Visible : Visibility.Collapsed;
            if (hasOp)
            {
                var name = string.IsNullOrWhiteSpace(doc.StrongpointName)
                    ? ""
                    : doc.StrongpointName.Trim();
                OpCardBtn.Content = string.IsNullOrEmpty(name)
                    ? "Картка ОП"
                    : $"Картка ОП {name}";
            }
        }
        else if (doc.Type == DocType.ExplosiveWorks)
            BindExplosiveWorksTable();
        else
            BindFlatTable(withSum: doc.Type == DocType.Receipt);

        if (doc.Type == DocType.QualityChange)
            VrActBtn.Visibility = Visibility.Visible;
    }

    private void ApplyNoteButton(string? note)
    {
        var has = !string.IsNullOrWhiteSpace(note);
        NoteBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (has)
            NoteEditWindow.ApplyButton(NoteBtn, note);
    }

    private void OpenActions(object sender, RoutedEventArgs e)
    {
        if (ActionsMenu == null || ActionsBtn == null) return;
        ActionsMenu.PlacementTarget = ActionsBtn;
        ActionsMenu.Placement = PlacementMode.Bottom;
        ActionsMenu.IsOpen = true;
    }

    private void OpenOpCardClick(object sender, RoutedEventArgs e)
    {
        var row = AppStore.Instance.StrongpointRowFor(_doc);
        if (row == null)
        {
            MessageBox.Show("Опорний пункт ще не в переліку. Збережіть чернетку ЯС.",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var card = new StrongpointCardWindow(row) { Owner = this };
        if (card.ShowDialog() != true) return;
        if (card.DraftToOpen != null)
        {
            DraftToOpen = card.DraftToOpen;
            OpenAsDraft = true;
            DialogResult = true;
        }
    }

    private void OpenVrActClick(object sender, RoutedEventArgs e)
    {
        if (_doc.Type != DocType.QualityChange) return;
        new VrActViewWindow(_doc) { Owner = this }.ShowDialog();
    }

    private void BindQualityStateTable()
    {
        BuildQualityStateColumns();
        LinesGrid.ItemsSource = QualityStateActTable.Build(_doc)
            .Select(r => new LineRow(
                r.PropertyId ?? Guid.Empty,
                r.Name,
                r.Unit,
                r.QuantityText,
                "",
                r.PriceText,
                "",
                r.Category,
                r.IsStructure))
            .ToList();
    }

    private void BindFlatTable(bool withSum)
    {
        // Як у ЯС: Найменування | О.В. | Категорія | Кількість | Ціна; для надходження — ще Сума.
        BuildColumns(withStructure: false, withSum);
        var props = AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
        decimal total = 0;
        LinesGrid.ItemsSource = _doc.Lines
            .Select(l =>
            {
                props.TryGetValue(l.PropertyId, out var p);
                var price = ResolvePrice(l, props);
                var sum = price * l.Quantity;
                total += sum;
                return new LineRow(
                    l.PropertyId,
                    l.Name,
                    l.Unit,
                    Ua.Num(l.Quantity, "#,##0.###"),
                    "",
                    price > 0 ? Ua.FormatPrice(price) : "",
                    withSum ? Ua.Num(sum, "#,##0.00") : "",
                    p?.Category ?? "",
                    IsStructure: false);
            })
            .ToList();

        if (withSum)
        {
            TotalText.Text = $"Загальна вартість за накладною: {Ua.Num(total, "#,##0.00")}";
            TotalText.Visibility = Visibility.Visible;
        }
    }

    internal static decimal ResolvePrice(DocumentLine line, IReadOnlyDictionary<Guid, PropertyItem>? props = null)
    {
        if (line.UnitPrice > 0) return line.UnitPrice;
        props ??= AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
        return props.TryGetValue(line.PropertyId, out var p) ? p.UnitPrice : 0;
    }

    private static string BuildInfo(Document doc)
    {
        var status = doc.Status == DocStatus.Posted ? "Проведено" : "Чернетка";
        var lines = new List<string>
        {
            $"Дата: {doc.Date:dd.MM.yyyy}",
            $"Документ: {DocTypeNames.Title(doc.Type)}",
            $"Тип руху: {MoveKindNames.Of(doc.Type)}",
            $"Статус: {status}"
        };
        if (!string.IsNullOrWhiteSpace(doc.WorkTypeName))
            lines.Insert(1, $"Вид робіт: {doc.WorkTypeName}");

        if (doc.Type == DocType.QualityState)
        {
            lines.Add($"Назва опорного пункту: {Empty(doc.StrongpointName)}");
            lines.Add($"Дата введення в експлуатацію: {(doc.CommissioningDate ?? doc.Date):dd.MM.yyyy}");
            lines.Add($"Реєстраційний номер опорного пункту: {Empty(doc.StrongpointRegNumber)}");
            lines.Add($"№ Акта ВР: {VrActBuilder.DisplayVrNumber(doc)}");
        }
        else if (doc.Type == DocType.QualityChange)
        {
            lines.Add($"Серія формуляру: {Empty(doc.FormSeries)}");
            lines.Add($"Номер формуляру: {Empty(doc.FormNumber)}");
            lines.Add($"Назва інженерного загородження: {Empty(doc.BarrierName)}");
            lines.Add($"№ Акта ВР: {VrActBuilder.DisplayVrNumber(doc)}");
        }
        else if (doc.Type == DocType.ExplosiveWorks)
        {
            lines.Add("Додаток 16 до Керівництва з підривної (вибухової) справи (п. 12.1.4)");
            lines.Add("Накладні (FIFO): №№ " + ExplosiveWorksAct.InvoiceNumbers(doc));
        }

        return string.Join("\n", lines);
    }

    private void BindExplosiveWorksTable()
    {
        LinesGrid.Columns.Clear();
        var left = (Style)FindResource("CellLeft");
        var center = (Style)FindResource("CellCenter");
        var right = (Style)FindResource("CellRight");
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "№ з/п",
            Binding = new Binding(nameof(ExplosiveViewRow.Number)),
            Width = new DataGridLength(64),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Назви (шифр) боєприпасів",
            Binding = new Binding(nameof(ExplosiveViewRow.Name)),
            Width = new DataGridLength(3, DataGridLengthUnitType.Star),
            ElementStyle = left
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "О.В.",
            Binding = new Binding(nameof(ExplosiveViewRow.Unit)),
            Width = new DataGridLength(90),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Отримано",
            Binding = new Binding(nameof(ExplosiveViewRow.ReceivedText)),
            Width = new DataGridLength(110),
            ElementStyle = right
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Витрати",
            Binding = new Binding(nameof(ExplosiveViewRow.SpentText)),
            Width = new DataGridLength(110),
            ElementStyle = right
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Здати на склад",
            Binding = new Binding(nameof(ExplosiveViewRow.ReturnText)),
            Width = new DataGridLength(120),
            ElementStyle = right
        });

        var fmt = "#,##0.###";
        LinesGrid.ItemsSource = ExplosiveWorksAct.Build(_doc)
            .Select(r => new ExplosiveViewRow(
                r.Number.ToString(),
                r.Name,
                r.Unit,
                Ua.Num(r.ReceivedQty, fmt),
                Ua.Num(r.SpentQty, fmt),
                Ua.Num(r.ReturnQty, fmt)))
            .ToList();
    }

    private static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private void BuildQualityStateColumns()
    {
        LinesGrid.Columns.Clear();
        var left = (Style)FindResource("CellLeft");
        var center = (Style)FindResource("CellCenter");
        var right = (Style)FindResource("CellRight");
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Найменування матеріалу",
            Binding = new Binding(nameof(LineRow.Name)),
            Width = new DataGridLength(3, DataGridLengthUnitType.Star),
            ElementStyle = left
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Одиниці виміру",
            Binding = new Binding(nameof(LineRow.Unit)),
            Width = new DataGridLength(110),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Категорія",
            Binding = new Binding(nameof(LineRow.Category)),
            Width = new DataGridLength(90),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Кількість",
            Binding = new Binding(nameof(LineRow.QuantityText)),
            Width = new DataGridLength(100),
            ElementStyle = right
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ціна",
            Binding = new Binding(nameof(LineRow.PriceText)),
            Width = new DataGridLength(110),
            ElementStyle = right
        });
    }

    private void BuildColumns(bool withStructure, bool withSum)
    {
        LinesGrid.Columns.Clear();
        var left = (Style)FindResource("CellLeft");
        var center = (Style)FindResource("CellCenter");
        var right = (Style)FindResource("CellRight");
        if (withStructure)
        {
            LinesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Споруда",
                Binding = new Binding(nameof(LineRow.StructureName)),
                Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
                ElementStyle = left
            });
        }
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Найменування",
            Binding = new Binding(nameof(LineRow.Name)),
            Width = new DataGridLength(3, DataGridLengthUnitType.Star),
            ElementStyle = left
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "О.В.",
            Binding = new Binding(nameof(LineRow.Unit)),
            Width = new DataGridLength(80),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Категорія",
            Binding = new Binding(nameof(LineRow.Category)),
            Width = new DataGridLength(90),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Кількість",
            Binding = new Binding(nameof(LineRow.QuantityText)),
            Width = new DataGridLength(110),
            ElementStyle = right
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ціна",
            Binding = new Binding(nameof(LineRow.PriceText)),
            Width = new DataGridLength(110),
            ElementStyle = right
        });
        if (withSum)
        {
            LinesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Сума",
                Binding = new Binding(nameof(LineRow.SumText)),
                Width = new DataGridLength(120),
                ElementStyle = right
            });
        }
    }

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not LineRow row)
        {
            e.Row.Background = Brushes.Transparent;
            e.Row.FontWeight = FontWeights.Normal;
            return;
        }

        if (row.IsStructure)
        {
            e.Row.Background = GroupStripe;
            e.Row.Foreground = DarkOnLight;
            e.Row.FontWeight = FontWeights.SemiBold;
            return;
        }

        e.Row.FontWeight = FontWeights.Normal;
        if (_highlightPropertyId != null && row.PropertyId == _highlightPropertyId)
        {
            e.Row.Background = GroupStripe;
            e.Row.Foreground = DarkOnLight;
        }
        else
        {
            e.Row.Background = Brushes.Transparent;
            e.Row.Foreground = (Brush)FindResource("TextPrimary");
        }
    }

    private void NoteClick(object sender, RoutedEventArgs e)
    {
        var title = $"{DocTypeNames.Title(_doc.Type)} № {DocTypeNames.FormatNumber(_doc.Number)}";
        var dlg = new NoteEditWindow(_doc.Note, title) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _doc.Note = dlg.Note;
        AppStore.Instance.Save();
        ApplyNoteButton(_doc.Note);
    }

    private void EditDraftClick(object sender, RoutedEventArgs e)
    {
        OpenAsDraft = true;
        DialogResult = true;
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        var status = _doc.Status == DocStatus.Posted ? "проведений" : "чернетку";
        var ask = MessageBox.Show(
            $"Видалити {status} документ «{DocTypeNames.Title(_doc.Type)}» № {DocTypeNames.FormatNumber(_doc.Number)}?\n\n" +
            "Залишки на складі буде перераховано без цього документа.",
            "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes) return;

        var err = AppStore.Instance.DeleteDocument(_doc);
        if (err != null)
        {
            MessageBox.Show(err, "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Deleted = true;
        DialogResult = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void PrintClick(object sender, RoutedEventArgs e) =>
        DocumentExport.Print(this, _doc);

    private void ExcelClick(object sender, RoutedEventArgs e) =>
        DocumentExport.SaveExcel(this, _doc);

    private sealed record LineRow(
        Guid PropertyId,
        string Name,
        string Unit,
        string QuantityText,
        string StructureName,
        string PriceText,
        string SumText,
        string Category,
        bool IsStructure);

    private sealed record ExplosiveViewRow(
        string Number,
        string Name,
        string Unit,
        string ReceivedText,
        string SpentText,
        string ReturnText);
}
