using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class AdvanceWindow : Window
{
    private readonly AdvanceViewModel _vm;

    public AdvanceWindow(AdvanceViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => AmountBox.Focus();
    }

    // The money is recorded by the caller after this window closes with OK; a bad value stays on screen.
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Build() is not null) DialogResult = true;
    }
}
