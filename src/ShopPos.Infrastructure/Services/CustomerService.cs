using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Customers;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public CustomerService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<List<CustomerDto>> SearchAsync(string? term)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var t = term?.Trim();
        var q = db.Customers.AsNoTracking().Where(c => c.IsActive == true);

        if (!string.IsNullOrEmpty(t))
            q = q.Where(c => c.Name.Contains(t) || (c.Phone != null && c.Phone.Contains(t)));

        return await q.OrderBy(c => c.Name)
            .Take(30)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Phone))
            .ToListAsync();
    }

    public async Task<CustomerDto> QuickAddAsync(string name, string? phone)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0)
            throw new BusinessRuleException("Customer name is required.");

        var ph = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        await using var db = await _factory.CreateDbContextAsync();

        if (ph is not null)
        {
            var existing = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Phone == ph);
            if (existing is not null)
                throw new BusinessRuleException($"A customer with this phone number already exists: {existing.Name}.");
        }

        var entity = new Customer
        {
            Id = Guid.CreateVersion7(),
            Name = n,
            Phone = ph,
            OpeningDue = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(entity);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = "Create",
            EntityName = "Customer",
            EntityId = entity.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return new CustomerDto(entity.Id, entity.Name, entity.Phone);
    }
}
