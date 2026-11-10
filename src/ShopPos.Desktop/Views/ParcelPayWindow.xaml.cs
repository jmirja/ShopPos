using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ParcelPayWindow : Window
{
    private readonly ParcelPayViewModel _vm;

    public ParcelPayWindow(ParcelPayViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Title = viewModel.Title;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Validate()) DialogResult = true;
    }
}
