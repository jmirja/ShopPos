using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Sales;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class SalesService : ISalesService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public SalesService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<List<PaymentMethodDto>> GetPaymentMethodsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.PaymentMethods.AsNoTracking()
            .Where(m => m.IsActive == true)
            .OrderBy(m => m.Id)
            .Select(m => new PaymentMethodDto(m.Id, m.Name))
            .ToListAsync();
    }

    public async Task<CompletedSale> CompleteSaleAsync(CompleteSaleRequest request)
    {
        // ---------------------------------------------------- basic checks
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("The bill is empty.");
        if (request.Lines.Any(l => l.Qty <= 0))
            throw new BusinessRuleException("Every quantity must be greater than zero.");
        if (request.Lines.Any(l => l.UnitPrice < 0))
            throw new BusinessRuleException("A price cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;
        var allowNegative = await AllowNegativeAsync(db);

        // ----------------------------------------- load what is being sold
        var variantIds = request.Lines.Select(l => l.VariantId).Distinct().ToList();
        var variants = await db.ProductVariants.Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        var productIds = variants.Values.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        // ----------------------------------------------------- the money
        decimal subtotal = 0;
        foreach (var line in request.Lines)
            subtotal += PaymentCalculator.Round(line.Qty * line.UnitPrice);

        var total = PaymentCalculator.Round(subtotal);
        if (total <= 0)
            throw new BusinessRuleException("The bill total must be greater than zero.");

        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.IsActive == true).ToDictionaryAsync(m => m.Id);
        foreach (var p in request.Payments)
            if (!methods.ContainsKey(p.PaymentMethodId))
                throw new BusinessRuleException("One of the payment methods is no longer available.");

        var summary = PaymentCalculator.Calculate(total, request.Payments.Select(p =>
            new PaymentInput(IsCash(methods[p.PaymentMethodId].Name), p.Amount)));
        if (summary.Error is not null)
            throw new BusinessRuleException(summary.Error);

        if (summary.Due > 0 && request.CustomerId is null)
            throw new BusinessRuleException(
                "Part of the bill is unpaid. Choose a customer to sell on credit, or collect the full amount.");

        if (request.CustomerId is Guid customerId &&
            !await db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive == true))
            throw new BusinessRuleException("This customer no longer exists.");

        // ------------------------------------------------------ the sale
        var sale = new Sale
        {
            Id = Guid.CreateVersion7(),
            InvoiceNo = await NextInvoiceNoAsync(db),
            BranchId = branchId,
            CustomerId = request.CustomerId,
            UserId = _user.UserId,
            SaleDate = DateTime.UtcNow,
            SubTotal = subtotal,
            DiscountAmount = 0,
            TaxAmount = 0,
            Total = total,
            PaidAmount = summary.Paid,
            DueAmount = summary.Due,
            Status = "Completed",
            Notes = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        db.Sales.Add(sale);

        // ----------------------------------------- lines and stock movements
        var soldSerials = new HashSet<Guid>();
        var consumedPlain = new Dictionary<Guid, decimal>();
        var consumedBatch = new Dictionary<Guid, decimal>();

        foreach (var line in request.Lines)
        {
            if (!variants.TryGetValue(line.VariantId, out var variant) ||
                !products.TryGetValue(variant.ProductId, out var product) ||
                variant.IsActive != true || product.IsActive != true)
                throw new BusinessRuleException("One of the items on the bill is no longer available.");

            var name = string.IsNullOrWhiteSpace(variant.VariantName)
                ? product.Name
                : $"{product.Name} - {variant.VariantName}";
            var lineTotal = PaymentCalculator.Round(line.Qty * line.UnitPrice);

            // ---- services: no stock
            if (product.ProductType == "Service")
            {
                AddLine(db, sale, variant, name, null, null, line.Qty, line.UnitPrice, variant.CostPrice, lineTotal);
                continue;
            }

            // ---- phones etc.: one IMEI per line
            if (product.TracksSerial)
            {
                if (line.SerialItemId is not Guid serialId || line.Qty != 1)
                    throw new BusinessRuleException($"'{name}' must be sold by IMEI, one phone per line.");

                var serial = await db.SerialItems.FirstOrDefaultAsync(s => s.Id == serialId);
                if (serial is null || serial.VariantId != variant.Id)
                    throw new BusinessRuleException($"The IMEI for '{name}' was not found.");
                if (serial.Status != "InStock")
                    throw new BusinessRuleException(
                        $"IMEI {serial.SerialNo} is no longer in stock (status: {serial.Status}). Remove it from the bill.");
                if (!soldSerials.Add(serial.Id))
                    throw new BusinessRuleException($"IMEI {serial.SerialNo} is on the bill twice.");

                serial.Status = "Sold";

                AddLine(db, sale, variant, $"{name} [{serial.SerialNo}]", serial.Id, null,
                    1, line.UnitPrice, variant.CostPrice, lineTotal);
                AddMovement(db, sale, variant, -1, variant.CostPrice, serial.Id, null);
                continue;
            }

            // ---- batch items: sell the batch that expires first
            if (product.TracksBatch)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var batches = await db.Batches
                    .Where(b => b.VariantId == variant.Id && (b.ExpiryDate == null || b.ExpiryDate >= today))
                    .Select(b => new
                    {
                        Batch = b,
                        OnHand = db.StockMovements
                            .Where(m => m.BatchId == b.Id && m.BranchId == branchId)
                            .Sum(m => (decimal?)m.QtyChange) ?? 0m
                    })
                    .ToListAsync();

                var ordered = batches
                    .OrderBy(x => x.Batch.ExpiryDate ?? DateOnly.MaxValue)
                    .ThenBy(x => x.Batch.CreatedAt)
                    .ToList();

                var need = line.Qty;
                foreach (var x in ordered)
                {
                    if (need <= 0) break;

                    consumedBatch.TryGetValue(x.Batch.Id, out var alreadyTaken);
                    var free = x.OnHand - alreadyTaken;
                    if (free <= 0) continue;

                    var take = Math.Min(free, need);
                    var cost = x.Batch.CostPrice > 0 ? x.Batch.CostPrice : variant.CostPrice;

                    AddLine(db, sale, variant, $"{name} (batch {x.Batch.BatchNo})", null, x.Batch.Id,
                        take, line.UnitPrice, cost, PaymentCalculator.Round(take * line.UnitPrice));
                    AddMovement(db, sale, variant, -take, cost, null, x.Batch.Id);

                    consumedBatch[x.Batch.Id] = alreadyTaken + take;
                    need -= take;
                }

                if (need > 0)
                    throw new BusinessRuleException(
                        $"Not enough unexpired stock for '{name}'. Short by {need:0.###}.");
                continue;
            }

            // ---- ordinary stock items
            var current = await db.StockMovements
                .Where(m => m.VariantId == variant.Id && m.BranchId == branchId)
                .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;

            consumedPlain.TryGetValue(variant.Id, out var already);
            if (!allowNegative && current - already < line.Qty)
                throw new BusinessRuleException(
                    $"Not enough stock for '{name}'. Available: {current - already:0.###}.");

            consumedPlain[variant.Id] = already + line.Qty;

            AddLine(db, sale, variant, name, null, null, line.Qty, line.UnitPrice, variant.CostPrice, lineTotal);
            AddMovement(db, sale, variant, -line.Qty, variant.CostPrice, null, null);
        }

        // ------------------------------------------------------ payments
        // Non-cash is taken as typed. Cash only counts up to what is still needed (the rest is change).
        var cashLeftToApply = summary.CashApplied;
        foreach (var p in request.Payments)
        {
            var amount = PaymentCalculator.Round(p.Amount);
            if (amount <= 0) continue;

            var applied = amount;
            if (IsCash(methods[p.PaymentMethodId].Name))
            {
                applied = Math.Min(amount, cashLeftToApply);
                cashLeftToApply -= applied;
                if (applied <= 0) continue;
            }

            db.Payments.Add(new Payment
            {
                Id = Guid.CreateVersion7(),
                BranchId = branchId,
                SaleId = sale.Id,
                CustomerId = request.CustomerId,
                PaymentMethodId = p.PaymentMethodId,
                Direction = "In",
                Amount = applied,
                ReferenceNo = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim(),
                PaidAt = DateTime.UtcNow,
                CreatedByUserId = _user.UserId
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.UserId,
            Action = "SaleCompleted",
            EntityName = "Sale",
            EntityId = sale.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        // One SaveChanges = one database transaction: everything above is saved together or not at all.
        await db.SaveChangesAsync();

        return new CompletedSale(sale.Id, sale.InvoiceNo!, total, summary.Paid, summary.Change, summary.Due);
    }

    // ================================================================ helpers

    private static bool IsCash(string methodName) =>
        string.Equals(methodName, "Cash", StringComparison.OrdinalIgnoreCase);

    private static void AddLine(ShopPosDbContext db, Sale sale, ProductVariant variant, string description,
        Guid? serialItemId, Guid? batchId, decimal qty, decimal unitPrice, decimal unitCost, decimal lineTotal)
    {
        db.SaleLines.Add(new SaleLine
        {
            // ids grow with every line, so ordering by id gives the cart order again (used by receipts)
            Id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(db.SaleLines.Local.Count)),
            SaleId = sale.Id,
            VariantId = variant.Id,
            SerialItemId = serialItemId,
            BatchId = batchId,
            Description = description.Length > 250 ? description[..250] : description,
            Qty = qty,
            UnitPrice = unitPrice,
            UnitCostSnapshot = unitCost,
            DiscountAmount = 0,
            TaxAmount = 0,
            LineTotal = lineTotal
        });
    }

    private void AddMovement(ShopPosDbContext db, Sale sale, ProductVariant variant, decimal qtyChange,
        decimal unitCost, Guid? serialItemId, Guid? batchId)
    {
        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.CreateVersion7(),
            BranchId = sale.BranchId,
            VariantId = variant.Id,
            BatchId = batchId,
            SerialItemId = serialItemId,
            QtyChange = qtyChange,
            MovementType = "Sale",
            ReferenceType = "Sale",
            ReferenceId = sale.Id,
            Reason = $"Sale {sale.InvoiceNo}",
            UnitCost = unitCost,
            CreatedByUserId = _user.UserId,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task<bool> AllowNegativeAsync(ShopPosDbContext db)
    {
        var value = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "stock.allow_negative"))?.SettingValue;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>INV-000001, INV-000002 ... The counter is saved together with the sale.</summary>
    private static async Task<string> NextInvoiceNoAsync(ShopPosDbContext db)
    {
        var prefix = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "invoice.prefix"))?.SettingValue;
        if (string.IsNullOrWhiteSpace(prefix)) prefix = "INV";

        var counter = await db.Settings.FirstOrDefaultAsync(s => s.SettingKey == "invoice.next");
        if (counter is null)
        {
            counter = new Setting { SettingKey = "invoice.next", SettingValue = "1" };
            db.Settings.Add(counter);
        }

        var number = long.TryParse(counter.SettingValue, out var n) ? n : 1;
        counter.SettingValue = (number + 1).ToString();

        return $"{prefix.Trim()}-{number:D6}";
    }
}
