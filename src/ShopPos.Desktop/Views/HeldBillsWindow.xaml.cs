using System.Windows;
using System.Windows.Input;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class HeldBillsWindow : Window
{
    private readonly HeldBillsViewModel _vm;

    public HeldBillsWindow(HeldBillsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _vm.LoadAsync();
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.ResumeSelectedAsync()) DialogResult = true;
    }

    private async void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Selected is not null && await _vm.ResumeSelectedAsync()) DialogResult = true;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            await _vm.DeleteSelectedAsync();         // shows the "select a bill" message
            return;
        }

        var answer = MessageBox.Show("Delete this held bill? It cannot be brought back.", "Held bills",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            await _vm.DeleteSelectedAsync();
    }
}
