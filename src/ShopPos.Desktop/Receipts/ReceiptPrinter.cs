using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ShopPos.Desktop.Receipts;

public static class ReceiptPrinter
{
    /// <summary>A print dialog that already points at the configured receipt printer (or the Windows default).</summary>
    public static PrintDialog CreateDialog(string? printerName)
    {
        var dialog = new PrintDialog();

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            var server = new LocalPrintServer();
            dialog.PrintQueue = server.GetPrintQueue(printerName.Trim());   // throws if there is no such printer
        }

        return dialog;
    }

    /// <summary>Prints without asking anything (used right after a sale).</summary>
    public static void PrintDirect(FixedDocument document, string? printerName, string jobName)
    {
        var dialog = CreateDialog(printerName);
        dialog.PrintDocument(document.DocumentPaginator, jobName);
    }
}
