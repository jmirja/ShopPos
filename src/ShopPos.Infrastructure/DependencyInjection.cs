using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopPos.Application.Auth;
using ShopPos.Application.Billing;
using ShopPos.Application.Catalog;
using ShopPos.Application.Customers;
using ShopPos.Application.Receipts;
using ShopPos.Application.Sales;
using ShopPos.Application.Shifts;
using ShopPos.Application.Stock;
using ShopPos.Infrastructure.Persistence;
using ShopPos.Infrastructure.Security;
using ShopPos.Infrastructure.Services;

namespace ShopPos.Infrastructure;
 
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("Connection string 'Default' is missing.");
 
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 26));
 
        services.AddDbContextFactory<ShopPosDbContext>(options =>
            options.UseMySql(cs, serverVersion));

        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IStockService, StockService>();
        services.AddSingleton<IBillingService, BillingService>();
        services.AddSingleton<ISalesService, SalesService>();
        services.AddSingleton<ICustomerService, CustomerService>();
        services.AddSingleton<IReceiptService, ReceiptService>();
        services.AddSingleton<IShiftService, ShiftService>();
        services.AddSingleton<IHeldBillService, HeldBillService>();
        services.AddSingleton<ISaleVoidService, SaleVoidService>();    
 
        return services;
    }
}