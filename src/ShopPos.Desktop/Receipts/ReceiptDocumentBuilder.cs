using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using ShopPos.Application.Receipts;

namespace ShopPos.Desktop.Receipts;

/// <summary>
/// Lays a receipt out as a list of blocks (text rows, dividers ...), measures each one and places them on pages.
/// Roll paper gets one page exactly as tall as the receipt; A4 is split into pages when it is long.
/// </summary>
public static class ReceiptDocumentBuilder
{
    private const double MmToDip = 96.0 / 25.4;
    private const double FeedMm = 10;                  // empty paper after the last line so the cutter does not cut text

    private static readonly FontFamily Font = new("Segoe UI");

    public static FixedDocument Build(ReceiptData data, ReceiptPaper paper, bool reprint)
    {
        var pageWidth = paper.PageWidthMm * MmToDip;
        var margin = paper.MarginMm * MmToDip;
        var contentWidth = pageWidth - 2 * margin;

        var blocks = CreateBlocks(data, contentWidth, paper.FontSize, reprint);
        foreach (var block in blocks)
            block.Measure(new Size(contentWidth, double.PositiveInfinity));

        // Roll paper: no height limit. A4: stop when the page is full.
        var limit = paper.PageHeightMm is { } h ? h * MmToDip - 2 * margin : double.PositiveInfinity;

        var pages = new List<List<FrameworkElement>> { new() };
        double used = 0, tallest = 0;
        foreach (var block in blocks)
        {
            var height = block.DesiredSize.Height;
            if (used + height > limit && pages[^1].Count > 0)
            {
                pages.Add(new List<FrameworkElement>());
                used = 0;
            }

            pages[^1].Add(block);
            used += height;
            tallest = Math.Max(tallest, used);
        }

        var pageHeight = paper.PageHeightMm is { } fixedHeight
            ? fixedHeight * MmToDip
            : tallest + 2 * margin + FeedMm * MmToDip;

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        foreach (var pageBlocks in pages)
        {
            var page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };

            var y = margin;
            foreach (var block in pageBlocks)
            {
                FixedPage.SetLeft(block, margin);
                FixedPage.SetTop(block, y);
                page.Children.Add(block);
                y += block.DesiredSize.Height;
            }

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            doc.Pages.Add(content);

            page.Measure(new Size(pageWidth, pageHeight));
            page.Arrange(new Rect(0, 0, pageWidth, pageHeight));
            page.UpdateLayout();
        }

        return doc;
    }

    // ============================================================== content

    private static List<FrameworkElement> CreateBlocks(ReceiptData d, double w, double fs, bool reprint)
    {
        var b = new List<FrameworkElement>();

        b.Add(Text(d.ShopName, w, fs * 1.5, bold: true, center: true));
        if (d.ShopAddress is not null) b.Add(Text(d.ShopAddress, w, fs * 0.9, center: true));
        if (d.ShopPhone is not null) b.Add(Text("Tel: " + d.ShopPhone, w, fs * 0.9, center: true));
        b.Add(Divider(w));

        if (d.IsVoided) b.Add(Text("*** VOIDED ***", w, fs * 1.2, bold: true, center: true));
        if (reprint) b.Add(Text("*** DUPLICATE COPY ***", w, fs, bold: true, center: true));

        b.Add(Pair("Invoice", d.InvoiceNo, w, fs, bold: true));
        b.Add(Pair("Date", d.SaleDateLocal.ToString("dd MMM yyyy HH:mm"), w, fs));
        if (d.Cashier.Length > 0) b.Add(Pair("Cashier", d.Cashier, w, fs));
        if (d.Customer is not null) b.Add(Pair("Customer", d.Customer, w, fs));
        b.Add(Divider(w));

        foreach (var line in d.Lines)
        {
            b.Add(Text(line.Description, w, fs, wrap: true));
            b.Add(Pair($"   {line.Qty:0.###} x {line.UnitPrice:N2}", line.LineTotal.ToString("N2"), w, fs));
        }
        b.Add(Divider(w));

        b.Add(Pair("Subtotal", d.SubTotal.ToString("N2"), w, fs));
        if (d.Discount > 0) b.Add(Pair("Discount", "-" + d.Discount.ToString("N2"), w, fs));
        if (d.Tax > 0) b.Add(Pair("Tax / VAT", d.Tax.ToString("N2"), w, fs));
        b.Add(Pair("TOTAL", d.Total.ToString("N2"), w, fs * 1.25, bold: true));
        b.Add(Gap(fs * 0.4));

        foreach (var p in d.Payments)
        {
            b.Add(Pair(p.Method, p.Amount.ToString("N2"), w, fs));
            if (p.Reference is not null) b.Add(Text("   ref: " + p.Reference, w, fs * 0.85));
        }
        if (d.Change is > 0) b.Add(Pair("Change", d.Change.Value.ToString("N2"), w, fs, bold: true));
        if (d.Due > 0) b.Add(Pair("DUE (on account)", d.Due.ToString("N2"), w, fs, bold: true));
        b.Add(Divider(w));

        if (d.Footer is not null) b.Add(Text(d.Footer, w, fs * 0.95, wrap: true, center: true));
        return b;
    }

    // ============================================================ building blocks

    private static TextBlock Text(string text, double width, double size,
        bool bold = false, bool center = false, bool wrap = true) => new()
    {
        Text = text,
        Width = width,
        FontFamily = Font,
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Foreground = Brushes.Black
    };

    private static Grid Pair(string left, string right, double width, double size, bool bold = false)
    {
        var grid = new Grid { Width = width };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var l = new TextBlock
        {
            Text = left, FontFamily = Font, FontSize = size, Foreground = Brushes.Black,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0)
        };
        var r = new TextBlock
        {
            Text = right, FontFamily = Font, FontSize = size, Foreground = Brushes.Black,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Grid.SetColumn(r, 1);
        grid.Children.Add(l);
        grid.Children.Add(r);
        return grid;
    }

    private static Line Divider(double width) => new()
    {
        X1 = 0, X2 = width, Y1 = 0.5, Y2 = 0.5,
        Width = width, Height = 1,
        Stroke = Brushes.Black, StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 3, 3 },
        Margin = new Thickness(0, 4, 0, 4)
    };

    private static FrameworkElement Gap(double height) => new Border { Height = height };
}
