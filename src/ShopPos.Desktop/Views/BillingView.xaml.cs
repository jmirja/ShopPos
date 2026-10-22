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

        // F2 jumps back to the search box from anywhere on the screen;
        // the Down arrow moves from the search box into the list of matches.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                FocusSearchBox();
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

    private void ClearBill_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillingViewModel vm) return;

        var answer = MessageBox.Show("Remove everything from this bill?", "Clear bill",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            vm.ClearCommand.Execute(null);
    }
}
