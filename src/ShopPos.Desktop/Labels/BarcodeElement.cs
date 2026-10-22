using System.Windows;
using System.Windows.Media;

namespace ShopPos.Desktop.Labels;

/// <summary>Draws an EAN-13 barcode as sharp vector bars that fill the element's size.</summary>
public class BarcodeElement : FrameworkElement
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(BarcodeElement),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var ean = Ean13.Normalize(Code, out _);
        if (ean is null) return;

        var modules = Ean13.Modules(ean);
        var moduleWidth = RenderSize.Width / modules.Length;
        var height = RenderSize.Height;

        var i = 0;
        while (i < modules.Length)
        {
            if (!modules[i]) { i++; continue; }

            var start = i;
            while (i < modules.Length && modules[i]) i++;       // merge neighbouring black modules into one bar

            dc.DrawRectangle(Brushes.Black, null,
                new Rect(start * moduleWidth, 0, (i - start) * moduleWidth, height));
        }
    }
}
