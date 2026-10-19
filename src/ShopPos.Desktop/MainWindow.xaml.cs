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

        // Open the Home screen as soon as the window appears.
        Loaded += async (_, _) => await viewModel.NavigateCommand.ExecuteAsync("Home");
    }
}
