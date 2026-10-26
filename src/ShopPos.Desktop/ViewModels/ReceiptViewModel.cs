using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Receipts;
using ShopPos.Desktop.Receipts;

namespace ShopPos.Desktop.ViewModels;

public partial class ReceiptViewModel : ObservableObject
{
    private readonly ReceiptData _data;
    private readonly ReceiptSettings _settings;
    private readonly bool _reprint;
    private readonly IReceiptService _service;

    public ReceiptViewModel(ReceiptData data, ReceiptSettings settings, bool reprint, IReceiptService service)
    {
        _data = data;
        _settings = settings;
        _reprint = reprint;
        _service = service;
        _selectedPaper = ReceiptPaper.Find(settings.Paper);
        Rebuild();
    }

    public string Title => $"Receipt {_data.InvoiceNo}";
    public ReceiptPaper[] Papers => ReceiptPaper.All;

    [ObservableProperty] private ReceiptPaper _selectedPaper;
    [ObservableProperty] private bool _rememberPaper;
    [ObservableProperty] private IDocumentPaginatorSource? _document;
    [ObservableProperty] private string? _message;

    partial void OnSelectedPaperChanged(ReceiptPaper value) => Rebuild();

    private FixedDocument Rebuild()
    {
        var doc = ReceiptDocumentBuilder.Build(_data, SelectedPaper, _reprint);
        Document = doc;
        return doc;
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        try
        {
            var doc = Rebuild();
            var dialog = ReceiptPrinter.CreateDialog(_settings.Printer);

            if (dialog.ShowDialog() != true) return;

            dialog.PrintDocument(doc.DocumentPaginator, _data.InvoiceNo);
            Message = "Sent to the printer.";

            if (RememberPaper)
                await _service.SavePaperAsync(SelectedPaper.Key);
        }
        catch (Exception ex)
        {
            Message = "Printing failed: " + ex.Message;
        }
    }
}
