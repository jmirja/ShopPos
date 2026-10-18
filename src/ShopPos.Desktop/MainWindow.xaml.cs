using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.LogoutRequested += Close;   // "Log out" simply closes this window
    }
}
