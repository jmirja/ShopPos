using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopPos.Infrastructure;
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Desktop;

// Fully qualified base class: "ShopPos.Application" is also a namespace name.
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)   // finds appsettings.json next to the .exe
                .ConfigureServices((context, services) =>
                {
                    services.AddInfrastructure(context.Configuration);
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            // Smoke test: read the roles table to prove the database connection works.
            var factory = _host.Services.GetRequiredService<IDbContextFactory<ShopPosDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var roleCount = await db.Roles.CountAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Title = $"Shop POS - connected ({roleCount} roles)";
            window.Show();
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