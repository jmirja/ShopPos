using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class BillingView : UserControl
{
    public BillingView()
    {
        InitializeComponent();

        // The bill's view-model lives for the whole login, but this view is created again every time you
        // open Billing, so hook up when shown and let go when hidden.
        Loaded += (_, _) =>
        {
            if (DataContext is BillingViewModel vm) vm.FocusSearchRequested += FocusSearchBox;
            SearchBox.Focus();
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is BillingViewModel vm) vm.FocusSearchRequested -= FocusSearchBox;
        };

        // F2 = back to the search box, F12 = pay, Down arrow = from the search box into the matches.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                FocusSearchBox();
                e.Handled = true;
            }
            else if (e.Key == Key.F12)
            {
                Pay_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Down && SearchBox.IsKeyboardFocused)
            {
                SearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        };
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        var payment = await vm.PreparePaymentAsync();
        if (payment is null) return;

        var window = new PaymentWindow(payment) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true)
            await vm.CompleteSaleAsync(payment);
    }

    private void ChooseCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        var picker = vm.CreateCustomerPicker();
        var window = new CustomerPickerWindow(picker) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true && picker.Chosen is not null)
            vm.Customer = picker.Chosen;
    }

    private void ClearBill_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        var answer = MessageBox.Show("Remove everything from this bill?", "Clear bill",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            vm.ClearCommand.Execute(null);
    }
}
