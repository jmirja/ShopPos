using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace ShopPos.Desktop.Labels;

public static class LabelDocumentBuilder
{
    private const double MmToDip = 96.0 / 25.4;      // WPF measures in 1/96 inch
    private const string CurrencyPrefix = "Tk ";     // change here if you want another currency text

    public static FixedDocument Build(IEnumerable<LabelItem> labels, LabelLayout layout, bool showPrice)
    {
        var pageWidth = layout.PageWidthMm * MmToDip;
        var pageHeight = layout.PageHeightMm * MmToDip;
        var labelWidth = layout.LabelWidthMm * MmToDip;
        var labelHeight = layout.LabelHeightMm * MmToDip;
        var perPage = layout.Columns * layout.Rows;

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        FixedPage? page = null;
        var slot = 0;

        foreach (var label in labels)
        {
            if (page is null || slot == perPage)
            {
                if (page is not null) AddPage(doc, page, pageWidth, pageHeight);
                page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };
                slot = 0;
            }

            var column = slot % layout.Columns;
            var row = slot / layout.Columns;

            var element = BuildLabel(label, labelWidth, labelHeight, showPrice);
            FixedPage.SetLeft(element, (layout.MarginLeftMm + column * (layout.LabelWidthMm + layout.GapXMm)) * MmToDip);
            FixedPage.SetTop(element, (layout.MarginTopMm + row * (layout.LabelHeightMm + layout.GapYMm)) * MmToDip);
            page.Children.Add(element);
            slot++;
        }

        if (page is not null) AddPage(doc, page, pageWidth, pageHeight);
        return doc;
    }

    private static void AddPage(FixedDocument doc, FixedPage page, double width, double height)
    {
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        doc.Pages.Add(content);

        page.Measure(new Size(width, height));
        page.Arrange(new Rect(0, 0, width, height));
        page.UpdateLayout();
    }

    private static FrameworkElement BuildLabel(LabelItem item, double width, double height, bool showPrice)
    {
        var pad = 1.5 * MmToDip;
        var font = Math.Clamp(height * 0.13, 6.5, 11);
        var ean = Ean13.Normalize(item.Barcode, out _) ?? item.Barcode;

        var grid = new Grid { Width = width, Height = height, Background = Brushes.White };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock
        {
            Text = item.Name,
            FontSize = font,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = font * 2.7,                              // at most two lines
            Margin = new Thickness(pad, pad, pad, 1)
        };
        Grid.SetRow(name, 0);
        grid.Children.Add(name);

        var quietZone = pad + 2 * MmToDip;                       // empty space left and right of the bars
        var bars = new BarcodeElement
        {
            Code = ean,
            Margin = new Thickness(quietZone, 2, quietZone, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(bars, 1);
        grid.Children.Add(bars);

        var footer = new Grid { Margin = new Thickness(pad, 0, pad, pad) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var digits = new TextBlock
        {
            Text = ean,
            FontFamily = new FontFamily("Consolas"),
            FontSize = font * 0.95,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(digits, 0);
        footer.Children.Add(digits);

        if (showPrice)
        {
            var priceText = item.Price % 1 == 0 ? item.Price.ToString("N0") : item.Price.ToString("N2");
            var price = new TextBlock
            {
                Text = CurrencyPrefix + priceText,
                FontSize = font * 1.25,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(price, 1);
            footer.Children.Add(price);
        }

        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        return grid;
    }
}
