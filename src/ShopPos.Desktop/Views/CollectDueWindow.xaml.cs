using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class CollectDueWindow : Window
{
    private readonly CollectDueViewModel _vm;

    public CollectDueWindow(CollectDueViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await _vm.LoadAsync();
            AmountBox.Focus();
        };
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.ConfirmAsync()) DialogResult = true;
    }
}
