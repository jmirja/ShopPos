using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Desktop.Labels;

namespace ShopPos.Desktop.ViewModels;

/// <summary>One product variant in the "Print labels" window.</summary>
public partial class LabelLine : ObservableObject
{
    public LabelLine(string name, string? barcode, decimal price)
    {
        Name = name;
        Price = price;
        BarcodeText = barcode?.Trim() ?? string.Empty;
        Ean = Ean13.Normalize(BarcodeText, out var problem);
        Problem = problem;
    }

    public string Name { get; }
    public decimal Price { get; }
    public string BarcodeText { get; }
    public string? Ean { get; }
    public string Problem { get; }
    public bool CanPrint => Ean is not null;

    [ObservableProperty] private int _copies = 1;
}

public partial class LabelPrintViewModel : ObservableObject
{
    private const int MaxLabels = 3000;

    public LabelPrintViewModel(IEnumerable<LabelLine> lines)
    {
        foreach (var line in lines) Lines.Add(line);
        _selectedLayout = LabelLayout.Presets[0];
    }

    public ObservableCollection<LabelLine> Lines { get; } = new();
    public LabelLayout[] Layouts => LabelLayout.Presets;

    [ObservableProperty] private LabelLayout _selectedLayout;
    [ObservableProperty] private bool _showPrice = true;
    [ObservableProperty] private IDocumentPaginatorSource? _document;
    [ObservableProperty] private string? _message;

    partial void OnSelectedLayoutChanged(LabelLayout value) => Preview();
    partial void OnShowPriceChanged(bool value) => Preview();

    [RelayCommand]
    private void Preview()
    {
        Document = BuildDocument();
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var doc = BuildDocument();
            Document = doc;
            if (doc is null) return;

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() == true)
                dialog.PrintDocument(doc.DocumentPaginator, "Barcode labels");
        }
        catch (Exception ex)
        {
            Message = "Printing failed: " + ex.Message;
        }
    }

    private FixedDocument? BuildDocument()
    {
        Message = null;

        var items = new List<LabelItem>();
        foreach (var line in Lines.Where(l => l.CanPrint && l.Copies > 0))
            for (var i = 0; i < line.Copies; i++)
                items.Add(new LabelItem(line.Ean!, line.Name, line.Price));

        if (items.Count == 0)
        {
            Message = "Nothing to print. Set Copies to 1 or more for an item that has a valid EAN-13 barcode.";
            return null;
        }
        if (items.Count > MaxLabels)
        {
            Message = $"Too many labels ({items.Count}). Print at most {MaxLabels} at a time.";
            return null;
        }

        var doc = LabelDocumentBuilder.Build(items, SelectedLayout, ShowPrice);
        Message = $"{items.Count} label(s) on {doc.Pages.Count} page(s).";
        return doc;
    }
}
