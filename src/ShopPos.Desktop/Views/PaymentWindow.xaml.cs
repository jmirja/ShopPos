using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class PaymentWindow : Window
{
    public PaymentWindow(PaymentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Put the cursor in the first amount box (Cash) so the cashier can type straight away.
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input,
            new Action(() => MoveFocus(new TraversalRequest(FocusNavigationDirection.First))));
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
