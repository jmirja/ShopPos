using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopPos.Application.Auth;
using ShopPos.Desktop.Receipts;
using ShopPos.Desktop.ViewModels;
using ShopPos.Desktop.Views;
using ShopPos.Infrastructure;

namespace ShopPos.Desktop;

// Fully qualified base class: "ShopPos.Application" is also a namespace name.
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // We open windows from code, so the app must not quit when the first dialog closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)   // finds appsettings.json next to the .exe
                .ConfigureServices((context, services) =>
                {
                    services.AddInfrastructure(context.Configuration);

                    services.AddSingleton<ICurrentUser, CurrentUser>();
                    services.AddSingleton<ReceiptCoordinator>();

                    // View-models
                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<FirstRunViewModel>();
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<ProductsViewModel>();
                    services.AddTransient<CategoriesViewModel>();
                    services.AddTransient<StockViewModel>();
                    services.AddTransient<BillingViewModel>();
                    services.AddTransient<CustomersViewModel>();

                    // Windows
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<FirstRunWindow>();
                    services.AddTransient<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            var sp = _host.Services;
            var auth = sp.GetRequiredService<IAuthService>();

            // First run: no accounts yet, so create the owner account.
            if (!await auth.HasAnyUserAsync())
            {
                var firstRun = sp.GetRequiredService<FirstRunWindow>();
                if (firstRun.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            // Login -> main window. "Log out" brings the login window back.
            while (true)
            {
                var login = sp.GetRequiredService<LoginWindow>();
                if (login.ShowDialog() != true) break;           // window closed = exit app

                var main = sp.GetRequiredService<MainWindow>();
                main.ShowDialog();                                // returns when the window closes

                var mainVm = (MainViewModel)main.DataContext;
                sp.GetRequiredService<ICurrentUser>().SignOut();
                if (!mainVm.LoggedOut) break;                     // closed with X = exit app
            }

            Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Shop POS - startup error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
