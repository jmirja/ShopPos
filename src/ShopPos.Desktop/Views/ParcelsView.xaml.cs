using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ParcelsView : UserControl
{
    public ParcelsView() => InitializeComponent();

    private Window? Owner => Window.GetWindow(this);

    private void ChooseCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelsViewModel vm) return;

        var picker = vm.CreateCustomerPicker();
        var window = new CustomerPickerWindow(picker) { Owner = Owner };
        if (window.ShowDialog() == true && picker.Chosen is not null)
            vm.UseCustomer(picker.Chosen);
    }

    private async void AddCourier_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelsViewModel vm) return;

        var input = new InputWindow("New courier", "What is the courier's name?") { Owner = Owner };
        if (input.ShowDialog() != true) return;

        await vm.AddCourierAsync(input.Result);
    }

    private async void CancelParcel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelsViewModel vm || vm.SelectedRow is null) return;

        var input = new InputWindow("Cancel parcel",
            "Why is this parcel being cancelled? The charge will be given back to the customer.")
        {
            Owner = Owner
        };
        if (input.ShowDialog() != true) return;

        await vm.CancelParcelAsync(input.Result);
    }

    private async void PayCourier_Click(object sender, RoutedEventArgs e) => await RunPayAsync(ParcelPayKind.CourierCost);

    private async void ReceiveCod_Click(object sender, RoutedEventArgs e) => await RunPayAsync(ParcelPayKind.CodFromCourier);

    private async void PayCodToSender_Click(object sender, RoutedEventArgs e) => await RunPayAsync(ParcelPayKind.CodToSender);

    private async Task RunPayAsync(ParcelPayKind kind)
    {
        if (DataContext is not ParcelsViewModel vm) return;

        var pay = await vm.CreatePayViewModelAsync(kind);
        if (pay is null) return;

        // The window stays open with the message when the payment is refused (for example: no open shift).
        var window = new ParcelPayWindow(pay) { Owner = Owner };
        while (window.ShowDialog() == true)
        {
            if (await vm.ConfirmPayAsync(kind, pay)) return;
            window = new ParcelPayWindow(pay) { Owner = Owner };
        }
    }
}
