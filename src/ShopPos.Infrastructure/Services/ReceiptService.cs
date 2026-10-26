using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Receipts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class ReceiptService : IReceiptService
{
    private static readonly string[] SettingKeys =
    {
        "shop.name", "shop.address", "shop.phone", "receipt.footer",
        "receipt.paper", "receipt.auto_print", "receipt.printer"
    };

    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public ReceiptService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<ReceiptData> GetReceiptAsync(Guid saleId, decimal? changeGiven = null)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == saleId)
                   ?? throw new BusinessRuleException("This sale no longer exists.");

        var settings = await ReadSettingsAsync(db);

        var cashier = await db.Users.AsNoTracking()
            .Where(u => u.Id == sale.UserId).Select(u => u.FullName).FirstOrDefaultAsync() ?? string.Empty;

        string? customer = null;
        if (sale.CustomerId is Guid customerId)
            customer = await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId).Select(c => c.Name).FirstOrDefaultAsync();

        // Line ids were created in cart order (time-ordered ids), so ordering by id keeps the cart order.
        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => l.SaleId == saleId)
            .OrderBy(l => l.Id)
            .Select(l => new { l.Description, l.Qty, l.UnitPrice, l.LineTotal })
            .ToListAsync();

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId == saleId && p.Direction == "In")
            .Join(db.PaymentMethods.AsNoTracking(), p => p.PaymentMethodId, m => m.Id,
                (p, m) => new { Method = m.Name, p.Amount, p.ReferenceNo, p.PaidAt })
            .OrderBy(x => x.PaidAt)
            .ToListAsync();

        return new ReceiptData(
            ShopName: Get(settings, "shop.name") ?? "Shop",
            ShopAddress: Get(settings, "shop.address"),
            ShopPhone: Get(settings, "shop.phone"),
            Footer: Get(settings, "receipt.footer"),
            InvoiceNo: sale.InvoiceNo ?? string.Empty,
            SaleDateLocal: DateTime.SpecifyKind(sale.SaleDate, DateTimeKind.Utc).ToLocalTime(),
            Cashier: cashier,
            Customer: customer,
            Lines: lines.Select(l => new ReceiptLine(l.Description ?? string.Empty, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
            SubTotal: sale.SubTotal,
            Discount: sale.DiscountAmount,
            Tax: sale.TaxAmount,
            Total: sale.Total,
            Payments: payments.Select(p => new ReceiptPayment(p.Method, p.Amount, p.ReferenceNo)).ToList(),
            Paid: sale.PaidAmount,
            Due: sale.DueAmount,
            Change: changeGiven,
            IsVoided: sale.Status == "Voided");
    }

    public async Task<ReceiptData?> GetLastReceiptAsync()
    {
        Guid? id;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var branchId = _user.BranchId;
            id = await db.Sales.AsNoTracking()
                .Where(s => s.BranchId == branchId && s.Status == "Completed")
                .OrderByDescending(s => s.SaleDate)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync();
        }

        return id is null ? null : await GetReceiptAsync(id.Value);
    }

    public async Task<ReceiptSettings> GetSettingsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var settings = await ReadSettingsAsync(db);

        var paper = Get(settings, "receipt.paper");
        if (paper is not ("80mm" or "58mm" or "A4")) paper = "80mm";

        var auto = Get(settings, "receipt.auto_print");
        var autoPrint = !(string.Equals(auto, "false", StringComparison.OrdinalIgnoreCase) || auto == "0");

        return new ReceiptSettings(paper, autoPrint, Get(settings, "receipt.printer"));
    }

    public async Task SavePaperAsync(string paper)
    {
        if (paper is not ("80mm" or "58mm" or "A4"))
            throw new BusinessRuleException("Unknown paper size.");

        await using var db = await _factory.CreateDbContextAsync();

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.SettingKey == "receipt.paper");
        if (setting is null)
            db.Settings.Add(new Setting { SettingKey = "receipt.paper", SettingValue = paper });
        else
            setting.SettingValue = paper;

        await db.SaveChangesAsync();
    }

    // ================================================================ helpers

    private static async Task<Dictionary<string, string?>> ReadSettingsAsync(ShopPosDbContext db) =>
        await db.Settings.AsNoTracking()
            .Where(s => SettingKeys.Contains(s.SettingKey))
            .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);

    /// <summary>The trimmed value, or null when the setting is missing or blank.</summary>
    private static string? Get(Dictionary<string, string?> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value)) return null;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
