using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ShiftCloseWindow : Window
{
    private readonly ShiftCloseViewModel _vm;

    public ShiftCloseWindow(ShiftCloseViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => CountedBox.Focus();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.CloseAsync()) DialogResult = true;
    }
}
