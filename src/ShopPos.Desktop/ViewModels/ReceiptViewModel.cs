using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Receipts;
using ShopPos.Desktop.Receipts;

namespace ShopPos.Desktop.ViewModels;

public partial class ReceiptViewModel : ObservableObject
{
    private readonly Func<ReceiptPaper, FixedDocument> _build;
    private readonly string _jobName;
    private readonly ReceiptSettings _settings;
    private readonly IReceiptService _service;

    /// <summary>Preview of a sale receipt.</summary>
    public ReceiptViewModel(ReceiptData data, ReceiptSettings settings, bool reprint, IReceiptService service)
        : this($"Receipt {data.InvoiceNo}", data.InvoiceNo, paper => ReceiptDocumentBuilder.Build(data, paper, reprint),
            settings, service)
    {
    }

    /// <summary>Preview of any other printed document (for example the repair job slip).</summary>
    public ReceiptViewModel(string title, string jobName, Func<ReceiptPaper, FixedDocument> build,
        ReceiptSettings settings, IReceiptService service)
    {
        Title = title;
        _jobName = jobName;
        _build = build;
        _settings = settings;
        _service = service;
        _selectedPaper = ReceiptPaper.Find(settings.Paper);
        Rebuild();
    }

    public string Title { get; }
    public ReceiptPaper[] Papers => ReceiptPaper.All;

    [ObservableProperty] private ReceiptPaper _selectedPaper;
    [ObservableProperty] private bool _rememberPaper;
    [ObservableProperty] private IDocumentPaginatorSource? _document;
    [ObservableProperty] private string? _message;

    partial void OnSelectedPaperChanged(ReceiptPaper value) => Rebuild();

    private FixedDocument Rebuild()
    {
        var doc = _build(SelectedPaper);
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

            dialog.PrintDocument(doc.DocumentPaginator, _jobName);
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
