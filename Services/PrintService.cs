using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Tally.Models;

namespace Tally.Services;

/// <summary>
/// Print / PDF. Builds an invoice page from a party (like the print view of the HTML version)
/// and sends it to the Windows print dialog. For a PDF file, just pick
/// "Microsoft Print to PDF" as the printer there.
/// </summary>
public static class PrintService
{
    private static SolidColorBrush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush Ink = Solid(0x14, 0x17, 0x1C);
    private static readonly Brush Accent = Solid(0x7C, 0x3A, 0xED);
    private static readonly Brush AccentDark = Solid(0x5B, 0x21, 0xB6);
    private static readonly Brush AccentTint = Solid(0xF1, 0xEB, 0xFE);
    private static readonly Brush Grey = Solid(0x4A, 0x50, 0x58);
    private static readonly Brush Line = Solid(0xE2, 0xE4, 0xE9);
    private static readonly Brush Zebra = Solid(0xF6, 0xF8, 0xF8);
    private static readonly Brush Red = Solid(0xB9, 0x1C, 0x1C);
    private static readonly Brush RedTint = Solid(0xFE, 0xEC, 0xEC);

    // Every printed word comes from the language files, so the sheet follows the UI language.
    // Numbers and dates stay German on purpose - that is an app-wide rule (see Fmt).
    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>Column heading: the very label the screen uses, set in capitals.</summary>
    private static string Head(string key) => LocalizationManager.T(key).ToUpperInvariant();

    private static string Today() => DateTime.Today.ToString("dd.MM.yyyy", Fmt.De);

    public static void Print(Party party)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var document = BuildDocument(party, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        dialog.PrintDocument(paginator, string.Format(T("S_PrintJobInvoice"), party.Name));
    }

    public static FlowDocument BuildDocument(Party party, double pageWidth, double pageHeight)
    {
        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(60),           // approx. 16 mm margin
            ColumnWidth = pageWidth,                    // single column
            ColumnGap = 0,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = Ink,
            Background = Brushes.White,
        };

        // --- header row ---
        var meta = NewTable(1, 1);
        var metaRow = AddRow(meta);
        metaRow.Cells.Add(MetaCell(T("S_PrintInvoiceHeading"), TextAlignment.Left));
        metaRow.Cells.Add(MetaCell(string.Format(T("S_PrintCreatedOn"), Today()), TextAlignment.Right));
        doc.Blocks.Add(meta);

        // --- title ---
        doc.Blocks.Add(new Paragraph(new Run(party.Name))
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 2),
        });

        if (!string.IsNullOrEmpty(party.DateText))
        {
            doc.Blocks.Add(new Paragraph(new Run(string.Format(T("S_PrintDateLine"), party.DateText)))
            {
                FontSize = 12,
                Foreground = Grey,
                Margin = new Thickness(0, 0, 0, 16),
            });
        }

        if (party.Items.Count == 0)
        {
            doc.Blocks.Add(new Paragraph(new Run(T("S_PrintNoItems"))) { Foreground = Grey });
            return doc;
        }

        // --- line items ---
        var table = NewTable(2.4, 1.3, 0.8, 1.1, 1.2, 1.7);
        table.Margin = new Thickness(0, 0, 0, 18);

        var headRow = AddRow(table);
        headRow.Cells.Add(HeadCell(Head("S_Drink"), TextAlignment.Left));
        headRow.Cells.Add(HeadCell(Head("S_Category"), TextAlignment.Left));
        headRow.Cells.Add(HeadCell(Head("S_Quantity"), TextAlignment.Right));
        headRow.Cells.Add(HeadCell(Head("S_UnitPriceColumn"), TextAlignment.Right));
        headRow.Cells.Add(HeadCell(Head("S_TotalPriceColumn"), TextAlignment.Right));
        headRow.Cells.Add(HeadCell(Head("S_StoreColumn"), TextAlignment.Left));

        var index = 0;
        foreach (var item in party.Items)
        {
            var row = AddRow(table);
            if (index++ % 2 == 1) row.Background = Zebra;

            row.Cells.Add(BodyCell(item.Name, TextAlignment.Left, bold: true));
            row.Cells.Add(BodyCell(string.IsNullOrWhiteSpace(item.Category) ? T("S_NoCategory") : item.Category, TextAlignment.Left));
            row.Cells.Add(BodyCell(Fmt.Qty(item.Quantity), TextAlignment.Right));
            row.Cells.Add(BodyCell(Fmt.Money(item.UnitPrice), TextAlignment.Right));
            row.Cells.Add(BodyCell(Fmt.Money(item.TotalPrice), TextAlignment.Right, bold: true));
            row.Cells.Add(BodyCell(string.IsNullOrWhiteSpace(item.Store) ? "—" : item.Store, TextAlignment.Left));
        }

        doc.Blocks.Add(table);

        // --- summary: categories on the left, total on the right ---
        var summary = NewTable(1.7, 1);
        var summaryRow = AddRow(summary);

        var chips = new Paragraph { Foreground = Grey, FontSize = 12, Margin = new Thickness(0, 4, 12, 0) };
        foreach (var total in party.CategoryTotals)
        {
            chips.Inlines.Add(new Run(total.Category + ": ") { Foreground = Grey });
            chips.Inlines.Add(new Run(Fmt.Money(total.Sum)) { FontWeight = FontWeights.Bold, Foreground = Ink });
            chips.Inlines.Add(new Run("      "));
        }
        summaryRow.Cells.Add(new TableCell(chips));

        var totalCell = new TableCell
        {
            Background = AccentTint,
            BorderBrush = Accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 10, 18, 10),
        };
        totalCell.Blocks.Add(new Paragraph(new Run(Head("S_GrandTotal")))
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentDark,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0),
        });
        totalCell.Blocks.Add(new Paragraph(new Run(Fmt.Money(party.TotalPrice)))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = AccentDark,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0),
        });
        totalCell.Blocks.Add(new Paragraph(new Run(party.ItemCountText))
        {
            FontSize = 11,
            Foreground = Grey,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0),
        });
        summaryRow.Cells.Add(totalCell);
        doc.Blocks.Add(summary);

        return doc;
    }

    // ---------------------------------------------------------------- Guest list (own sheet)

    public static void PrintGuests(Party party)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var document = BuildGuestDocument(party, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        dialog.PrintDocument(paginator, string.Format(T("S_PrintJobGuests"), party.Name));
    }

    public static FlowDocument BuildGuestDocument(Party party, double pageWidth, double pageHeight)
    {
        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(60),
            ColumnWidth = pageWidth,
            ColumnGap = 0,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = Ink,
            Background = Brushes.White,
        };

        // --- header row ---
        var meta = NewTable(1, 1);
        var metaRow = AddRow(meta);
        metaRow.Cells.Add(MetaCell(T("S_PrintGuestsHeading"), TextAlignment.Left));
        metaRow.Cells.Add(MetaCell(string.Format(T("S_PrintCreatedOn"), Today()), TextAlignment.Right));
        doc.Blocks.Add(meta);

        // --- title ---
        doc.Blocks.Add(new Paragraph(new Run(party.Name))
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 2),
        });

        if (!string.IsNullOrEmpty(party.DateText))
        {
            doc.Blocks.Add(new Paragraph(new Run(string.Format(T("S_PrintDateLine"), party.DateText)))
            {
                FontSize = 12,
                Foreground = Grey,
                Margin = new Thickness(0, 0, 0, 16),
            });
        }

        if (party.Guests.Count == 0)
        {
            doc.Blocks.Add(new Paragraph(new Run(T("S_PrintNoGuests"))) { Foreground = Grey });
            return doc;
        }

        // --- guests ---
        var table = NewTable(2.6, 1.2, 1.2, 1.2, 1.0);
        table.Margin = new Thickness(0, 0, 0, 18);

        var headRow = AddRow(table);
        headRow.Cells.Add(HeadCell(Head("S_GuestName"), TextAlignment.Left));
        headRow.Cells.Add(HeadCell(Head("S_OwesColumn"), TextAlignment.Right));
        headRow.Cells.Add(HeadCell(Head("S_PaidColumn"), TextAlignment.Right));
        headRow.Cells.Add(HeadCell(Head("S_OpenColumn"), TextAlignment.Right));
        headRow.Cells.Add(HeadCell(Head("S_PrintStatusColumn"), TextAlignment.Left));

        var index = 0;
        foreach (var guest in party.Guests)
        {
            var row = AddRow(table);
            if (index++ % 2 == 1) row.Background = Zebra;

            row.Cells.Add(BodyCell(string.IsNullOrWhiteSpace(guest.Name) ? "—" : guest.Name, TextAlignment.Left, bold: true));
            row.Cells.Add(BodyCell(Fmt.Money(guest.Owes), TextAlignment.Right));
            row.Cells.Add(BodyCell(Fmt.Money(guest.Paid), TextAlignment.Right));
            row.Cells.Add(BodyCell(Fmt.Money(guest.Open), TextAlignment.Right, bold: guest.Open > 0));
            row.Cells.Add(BodyCell(guest.Open <= 0 ? T("S_PrintStatusPaid") : T("S_PrintStatusOpen"), TextAlignment.Left));
        }

        doc.Blocks.Add(table);

        // --- summary: figures on the left, result on the right ---
        var summary = NewTable(1.7, 1);
        var summaryRow = AddRow(summary);

        var figures = new Paragraph { FontSize = 12, Margin = new Thickness(0, 4, 12, 0) };
        AddFigure(figures, T("S_SpentOnDrinks") + ": ", Fmt.Money(party.TotalPrice));
        AddFigure(figures, T("S_Collected") + ": ", Fmt.Money(party.GuestsPaid));
        AddFigure(figures, T("S_StillOpen") + ": ", Fmt.Money(party.GuestsOpen));
        summaryRow.Cells.Add(new TableCell(figures));

        var negative = party.IsProfitNegative;
        var resultCell = new TableCell
        {
            Background = negative ? RedTint : AccentTint,
            BorderBrush = negative ? Red : Accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 10, 18, 10),
        };
        resultCell.Blocks.Add(new Paragraph(new Run(Head("S_Result")))
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = negative ? Red : AccentDark,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0),
        });
        resultCell.Blocks.Add(new Paragraph(new Run(Fmt.Money(party.Profit)))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = negative ? Red : AccentDark,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0),
        });
        resultCell.Blocks.Add(new Paragraph(new Run(T("S_ResultFormula")))
        {
            FontSize = 11,
            Foreground = Grey,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0),
        });
        summaryRow.Cells.Add(resultCell);
        doc.Blocks.Add(summary);

        return doc;
    }

    private static void AddFigure(Paragraph target, string label, string value)
    {
        target.Inlines.Add(new Run(label) { Foreground = Grey });
        target.Inlines.Add(new Run(value) { FontWeight = FontWeights.Bold, Foreground = Ink });
        target.Inlines.Add(new Run("      "));
    }

    // ---------------------------------------------------------------- Building blocks

    private static Table NewTable(params double[] starWidths)
    {
        var table = new Table { CellSpacing = 0 };
        foreach (var width in starWidths)
            table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });
        return table;
    }

    private static TableRow AddRow(Table table)
    {
        if (table.RowGroups.Count == 0) table.RowGroups.Add(new TableRowGroup());
        var row = new TableRow();
        table.RowGroups[0].Rows.Add(row);
        return row;
    }

    private static TableCell MetaCell(string text, TextAlignment alignment) =>
        new(new Paragraph(new Run(text))
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Accent,
            TextAlignment = alignment,
            Margin = new Thickness(0),
        })
        {
            BorderBrush = Accent,
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            Padding = new Thickness(0, 0, 0, 8),
        };

    private static TableCell HeadCell(string text, TextAlignment alignment) =>
        new(new Paragraph(new Run(text))
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = alignment,
            Margin = new Thickness(0),
        })
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            Padding = new Thickness(10, 0, 10, 6),
        };

    private static TableCell BodyCell(string text, TextAlignment alignment, bool bold = false) =>
        new(new Paragraph(new Run(text))
        {
            TextAlignment = alignment,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0),
        })
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8),
        };
}
