namespace ShopPos.Desktop.Labels;

/// <summary>One printed label.</summary>
public record LabelItem(string Barcode, string Name, decimal Price);

/// <summary>Paper and label sizes in millimetres.</summary>
public record LabelLayout(
    string Name,
    double PageWidthMm, double PageHeightMm,
    double LabelWidthMm, double LabelHeightMm,
    int Columns, int Rows,
    double MarginLeftMm = 0, double MarginTopMm = 0,
    double GapXMm = 0, double GapYMm = 0)
{
    public static readonly LabelLayout[] Presets =
    {
        new("Roll label 50 x 30 mm (one label per page)", 50, 30, 50, 30, 1, 1),
        new("Roll label 40 x 25 mm (one label per page)", 40, 25, 40, 25, 1, 1),
        new("A4 sticker sheet, 3 x 8 (70 x 37 mm)",        210, 297, 70, 37, 3, 8)
    };
}
