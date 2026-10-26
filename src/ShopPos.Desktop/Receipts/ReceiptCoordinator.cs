using System.Windows;
using ShopPos.Application.Receipts;
using ShopPos.Desktop.ViewModels;
using ShopPos.Desktop.Views;

namespace ShopPos.Desktop.Receipts;

/// <summary>Decides what happens with a receipt: print it straight away, or show the preview window.</summary>
public class ReceiptCoordinator
{
    private readonly IReceiptService _receipts;

    public ReceiptCoordinator(IReceiptService receipts) => _receipts = receipts;

    /// <summary>Called right after a sale was saved. A problem here must never undo or hide the sale.</summary>
    public async Task PrintAfterSaleAsync(Guid saleId, decimal change, Window? owner)
    {
        try
        {
            var settings = await _receipts.GetSettingsAsync();
            var data = await _receipts.GetReceiptAsync(saleId, change);

            if (settings.AutoPrint)
            {
                try
                {
                    var document = ReceiptDocumentBuilder.Build(data, ReceiptPaper.Find(settings.Paper), reprint: false);
                    ReceiptPrinter.PrintDirect(document, settings.Printer, data.InvoiceNo);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"The sale was saved, but the receipt could not be printed automatically:\n\n{ex.Message}\n\n" +
                        "The preview will open so you can print it by hand.",
                        "Receipt", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            ShowPreview(data, settings, reprint: false, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The sale was saved, but the receipt could not be prepared:\n\n{ex.Message}",
                "Receipt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task ReprintLastAsync(Window? owner)
    {
        try
        {
            var data = await _receipts.GetLastReceiptAsync();
            if (data is null)
            {
                MessageBox.Show("There is no sale to print yet.", "Receipt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var settings = await _receipts.GetSettingsAsync();
            ShowPreview(data, settings, reprint: true, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Receipt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowPreview(ReceiptData data, ReceiptSettings settings, bool reprint, Window? owner)
    {
        var viewModel = new ReceiptViewModel(data, settings, reprint, _receipts);
        new ReceiptWindow(viewModel) { Owner = owner }.ShowDialog();
    }
}
