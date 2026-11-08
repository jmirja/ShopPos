using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class RepairsView : UserControl
{
    public RepairsView() => InitializeComponent();

    private Window? Owner => Window.GetWindow(this);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepairsViewModel vm) return;

        // A new job gets its slip printed straight away.
        var createdId = await vm.SaveJobAsync();
        if (createdId is Guid id)
            await vm.Receipts.PrintJobSlipAsync(id, Owner);
    }

    private void ChooseCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepairsViewModel vm) return;

        var picker = vm.CreateCustomerPicker();
        var window = new CustomerPickerWindow(picker) { Owner = Owner };
        if (window.ShowDialog() == true && picker.Chosen is not null)
            vm.NewCustomer = picker.Chosen;
    }

    private async void Advance_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepairsViewModel vm) return;

        var advance = await vm.CreateAdvanceViewModelAsync();
        if (advance is null) return;

        var window = new AdvanceWindow(advance) { Owner = Owner };
        if (window.ShowDialog() == true)
            await vm.TakeAdvanceAsync(advance);
    }

    private async void Deliver_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepairsViewModel vm) return;

        var delivery = vm.CreateDeliveryViewModel();
        if (delivery is null) return;

        var deliveryWindow = new RepairDeliveryWindow(delivery) { Owner = Owner };
        if (deliveryWindow.ShowDialog() != true) return;

        // Whatever is still unpaid (after the advance) is paid now, or left on the customer's account.
        PaymentViewModel? payment = null;
        if (delivery.Balance > 0)
        {
            payment = await vm.PrepareDeliveryPaymentAsync(delivery.Balance);
            if (payment is null) return;

            var paymentWindow = new PaymentWindow(payment) { Owner = Owner };
            if (paymentWindow.ShowDialog() != true) return;
        }

        var done = await vm.DeliverAsync(delivery, payment);
        if (done is not null)
            await vm.Receipts.PrintAfterSaleAsync(done.SaleId, done.Change, Owner);
    }

    private async void PrintSlip_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepairsViewModel vm || vm.Detail is null) return;
        await vm.Receipts.PrintJobSlipAsync(vm.Detail.Id, Owner);
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RepairsViewModel vm || vm.Detail is null) return;

        var input = new InputWindow("Cancel repair job",
            $"Why is job {vm.Detail.JobNo} being cancelled? Any advance payment will be refunded.")
        {
            Owner = Owner
        };
        if (input.ShowDialog() != true) return;

        await vm.CancelJobAsync(input.Result);
    }
}
