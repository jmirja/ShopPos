using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class PurchasingView : UserControl
{
    public PurchasingView() => InitializeComponent();

    private Window? Owner => Window.GetWindow(this);

    // ---------------------------------------------------------------- suppliers

    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchasingViewModel root) return;

        var pay = root.Suppliers.CreatePayViewModel();
        var window = new PaySupplierWindow(pay) { Owner = Owner };

        if (window.ShowDialog() == true && pay.Result is not null)
            await root.Suppliers.AfterPaidAsync(pay.Result);
    }

    private async void ReceiveDirect_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchasingViewModel root) return;

        var receive = root.Suppliers.CreateDirectReceiveViewModel();
        if (receive is not null)
            await RunReceiveAsync(root, receive);
    }

    // ------------------------------------------------------------ purchase orders

    private async void ReceiveFromPo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchasingViewModel root) return;

        var receive = root.Orders.CreateReceiveViewModel();
        if (receive is not null)
            await RunReceiveAsync(root, receive);
    }

    private async void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchasingViewModel root || root.Orders.Detail is null) return;

        var input = new InputWindow("Cancel order",
            $"Why is order {root.Orders.Detail.PoNo} being cancelled?") { Owner = Owner };
        if (input.ShowDialog() != true) return;

        await root.Orders.CancelOrderAsync(input.Result);
    }

    // ------------------------------------------------------------------ shared

    private async Task RunReceiveAsync(PurchasingViewModel root, ReceiveGoodsViewModel receive)
    {
        var window = new ReceiveGoodsWindow(receive) { Owner = Owner };
        if (window.ShowDialog() != true || receive.Result is not { } r) return;

        await root.RefreshAllAsync();

        var message = $"Goods received: {r.ReceiptNo}, total {r.Total:N2}.";
        if (r.Paid > 0) message += $" Paid {r.Paid:N2}.";
        if (r.Due > 0) message += $" Still owed to the supplier: {r.Due:N2}.";

        root.Suppliers.InfoMessage = message;
        root.Orders.InfoMessage = message;
    }
}
