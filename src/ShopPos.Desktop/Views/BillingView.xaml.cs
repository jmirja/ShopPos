using System.Globalization;
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
            else if (e.Key == Key.F6)
            {
                Hold_Click(this, new RoutedEventArgs());
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

        // Payments need an open shift (cash drawer) unless the shop switched that rule off.
        if (!await vm.IsShiftOkAsync())
        {
            var open = MessageBox.Show(
                "Your cash drawer shift is not open yet. Open it now?", "Shift",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (open != MessageBoxResult.Yes || !await TryOpenShiftAsync()) return;
        }

        var payment = await vm.PreparePaymentAsync();
        if (payment is null) return;

        var owner = Window.GetWindow(this);
        var window = new PaymentWindow(payment) { Owner = owner };
        if (window.ShowDialog() != true) return;

        var done = await vm.CompleteSaleAsync(payment);
        if (done is not null)
            await vm.Receipts.PrintAfterSaleAsync(done.SaleId, done.Change, owner);

        FocusSearchBox();
    }

    private async void Reprint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        await vm.Receipts.ReprintLastAsync(Window.GetWindow(this));
        FocusSearchBox();
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

    // ---------------------------------------------------------------- shift

    private async Task<bool> TryOpenShiftAsync()
    {
        if (DataContext is not BillingViewModel vm) return false;

        var input = new InputWindow("Open shift", "How much cash is in the drawer right now? (opening cash)", "0")
        {
            Owner = Window.GetWindow(this)
        };
        if (input.ShowDialog() != true) return false;

        if (!decimal.TryParse(input.Result, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            MessageBox.Show("Type the cash amount as a number, for example 2000.", "Open shift",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return await vm.OpenShiftAsync(amount);
    }

    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        await TryOpenShiftAsync();
        FocusSearchBox();
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        if (vm.HasItems)
        {
            MessageBox.Show("There is an unfinished bill. Finish it or put it on hold before closing the shift.",
                "Close shift", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var summary = await vm.GetShiftSummaryAsync();
        if (summary is null) return;

        var closeVm = vm.CreateShiftCloseViewModel(summary);
        var window = new ShiftCloseWindow(closeVm) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true && closeVm.Result is not null)
            await vm.AfterShiftClosedAsync(closeVm.Result);

        FocusSearchBox();
    }

    // ------------------------------------------------------------ held bills

    private async void Hold_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm || !vm.HasItems) return;

        var input = new InputWindow("Hold bill",
            "Write a short note so you can find this bill again (for example the customer's name). You can leave it empty.",
            vm.Customer?.Name ?? string.Empty)
        {
            Owner = Window.GetWindow(this)
        };
        if (input.ShowDialog() != true) return;

        await vm.HoldAsync(input.Result);
        FocusSearchBox();
    }

    private async void HeldBills_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        var heldVm = vm.CreateHeldBillsViewModel();
        var window = new HeldBillsWindow(heldVm) { Owner = Window.GetWindow(this) };
        window.ShowDialog();

        if (heldVm.Resumed is not null)
            await vm.ResumeAsync(heldVm.Resumed);
        else
            await vm.RefreshHeldCountAsync();

        FocusSearchBox();
    }

    // ----------------------------------------------------------------- voids

    private async void Void_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        var window = new VoidSaleWindow(vm.CreateVoidViewModel()) { Owner = Window.GetWindow(this) };
        window.ShowDialog();

        await vm.RefreshShiftAsync();
        FocusSearchBox();
    }
}
