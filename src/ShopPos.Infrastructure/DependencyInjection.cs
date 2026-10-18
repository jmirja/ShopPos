using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopPos.Application.Auth;
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
 
        return services;
    }
}