using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Purchasing;
using ShopPos.Application.Shifts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

/// <summary>
/// Purchase orders and goods receiving.
///  - A purchase order is only a plan (Draft -> Ordered); it never touches stock or money.
///  - Receiving goods (with or without an order) is the real event: it adds stock, stores IMEIs and batches,
///    sets the new cost price, records what you owe and any payment made now.
/// </summary>
public class PurchaseService : IPurchaseService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public PurchaseService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    // ============================================================ reading

    public async Task<List<PoRow>> ListOrdersAsync(PoFilter filter, string? search)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var q = db.PurchaseOrders.AsNoTracking().Where(p => p.BranchId == branchId);

        q = filter switch
        {
            PoFilter.Open => q.Where(p => p.Status == "Draft" || p.Status == "Ordered" || p.Status == "PartiallyReceived"),
            PoFilter.Received => q.Where(p => p.Status == "Received"),
            PoFilter.Cancelled => q.Where(p => p.Status == "Cancelled"),
            _ => q
        };

        var t = search?.Trim();
        if (!string.IsNullOrEmpty(t))
            q = q.Where(p => p.PoNo.Contains(t) || db.Suppliers.Any(s => s.Id == p.SupplierId && s.Name.Contains(t)));

        var rows = await q.OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.PoNo)
            .Take(300)
            .Select(p => new
            {
                p.Id, p.PoNo, p.OrderDate, p.SupplierId, p.Status, p.Total,
                SupplierName = db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault(),
                Lines = db.PurchaseOrderLines.Count(l => l.PoId == p.Id)
            })
            .ToListAsync();

        return rows.Select(r => new PoRow(
            r.Id, r.PoNo, r.OrderDate, r.SupplierId, r.SupplierName ?? "", r.Status, r.Total, r.Lines)).ToList();
    }

    public async Task<PoDetail?> GetOrderAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return null;

        var supplierName = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync() ?? "";

        var lines = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PoId == id).OrderBy(l => l.Id).ToListAsync();

        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        var productIds = variants.Values.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var rows = lines.Select(l =>
        {
            var name = "(unknown item)";
            var serial = false;
            var batch = false;
            if (variants.TryGetValue(l.VariantId, out var v) && products.TryGetValue(v.ProductId, out var p))
            {
                name = string.IsNullOrWhiteSpace(v.VariantName) ? p.Name : $"{p.Name} - {v.VariantName}";
                serial = p.TracksSerial;
                batch = p.TracksBatch;
            }
            return new PoLineRow(l.Id, l.VariantId, name, l.QtyOrdered, l.QtyReceived, l.UnitCost, serial, batch);
        }).ToList();

        return new PoDetail(po.Id, po.PoNo, po.SupplierId, supplierName, po.OrderDate, po.Status, po.Notes, po.Total, rows);
    }

    // ===================================================== purchase orders

    public async Task<Guid> SaveOrderAsync(PoSave s)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");
        if (s.Lines.Count == 0)
            throw new BusinessRuleException("Add at least one item to the order.");
        if (s.Lines.Any(l => l.Qty <= 0))
            throw new BusinessRuleException("Every quantity must be greater than zero.");
        if (s.Lines.Any(l => l.UnitCost < 0))
            throw new BusinessRuleException("A cost cannot be negative.");
        if (s.Lines.Select(l => l.VariantId).Distinct().Count() != s.Lines.Count)
            throw new BusinessRuleException("The same item is on the order twice. Change its quantity instead.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        if (!await db.Suppliers.AnyAsync(x => x.Id == s.SupplierId && x.IsActive == true))
            throw new BusinessRuleException("Choose the supplier.");

        var variantIds = s.Lines.Select(l => l.VariantId).ToList();
        var variants = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToListAsync();
        if (variants.Count != variantIds.Count)
            throw new BusinessRuleException("One of the items no longer exists.");
        var productIds = variants.Select(v => v.ProductId).Distinct().ToList();
        if (await db.Products.AnyAsync(p => productIds.Contains(p.Id) && p.ProductType != "Stock"))
            throw new BusinessRuleException("Only stock items can be ordered (not services).");

        PurchaseOrder po;
        var isNew = s.Id == Guid.Empty;
        if (isNew)
        {
            po = new PurchaseOrder
            {
                Id = Guid.CreateVersion7(),
                PoNo = await NextNumberAsync(db, "po.next", "PO"),
                BranchId = branchId,
                Status = PoStatus.Draft,
                CreatedByUserId = _user.UserId
            };
            db.PurchaseOrders.Add(po);
        }
        else
        {
            po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == s.Id && p.BranchId == branchId)
                 ?? throw new BusinessRuleException("This order no longer exists.");
            if (po.Status != PoStatus.Draft)
                throw new BusinessRuleException("Only a draft order can be changed.");

            db.PurchaseOrderLines.RemoveRange(await db.PurchaseOrderLines.Where(l => l.PoId == po.Id).ToListAsync());
        }

        po.SupplierId = s.SupplierId;
        po.OrderDate = s.OrderDate;
        po.Notes = Clean(s.Notes);
        po.Total = s.Lines.Sum(l => Money(l.Qty * l.UnitCost));

        foreach (var l in s.Lines)
        {
            db.PurchaseOrderLines.Add(new PurchaseOrderLine
            {
                Id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(db.PurchaseOrderLines.Local.Count)),
                PoId = po.Id,
                VariantId = l.VariantId,
                QtyOrdered = l.Qty,
                QtyReceived = 0,
                UnitCost = Money(l.UnitCost)
            });
        }

        AddAudit(db, isNew ? "PoCreated" : "PoUpdated", "PurchaseOrder", po.Id);
        await db.SaveChangesAsync();
        return po.Id;
    }

    public async Task MarkOrderedAsync(Guid orderId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == orderId && p.BranchId == branchId)
                 ?? throw new BusinessRuleException("This order no longer exists.");
        if (po.Status != PoStatus.Draft)
            throw new BusinessRuleException("Only a draft order can be marked as ordered.");
        if (!await db.PurchaseOrderLines.AnyAsync(l => l.PoId == po.Id))
            throw new BusinessRuleException("The order has no items.");

        po.Status = PoStatus.Ordered;
        AddAudit(db, "PoOrdered", "PurchaseOrder", po.Id);
        await db.SaveChangesAsync();
    }

    public async Task CancelOrderAsync(Guid orderId, string reason)
    {
        var why = Clean(reason) ?? throw new BusinessRuleException("Type the reason for cancelling this order.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == orderId && p.BranchId == branchId)
                 ?? throw new BusinessRuleException("This order no longer exists.");

        if (po.Status is not (PoStatus.Draft or PoStatus.Ordered))
            throw new BusinessRuleException("Only an order where nothing has arrived yet can be cancelled.");

        po.Status = PoStatus.Cancelled;
        var note = string.IsNullOrEmpty(po.Notes) ? $"Cancelled: {why}" : $"{po.Notes} | Cancelled: {why}";
        po.Notes = note.Length > 480 ? note[..480] : note;

        AddAudit(db, "PoCancelled", "PurchaseOrder", po.Id);
        await db.SaveChangesAsync();
    }

    // ============================================================ receiving

    public async Task<ReceiptResult> ReceiveAsync(ReceiveRequest r)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");
        if (r.Lines.Count == 0)
            throw new BusinessRuleException("Add at least one item that arrived.");
        if (r.Lines.Any(l => l.Qty <= 0))
            throw new BusinessRuleException("Every quantity must be greater than zero.");
        if (r.Lines.Any(l => l.UnitCost < 0))
            throw new BusinessRuleException("A cost cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.SupplierId && s.IsActive == true)
                       ?? throw new BusinessRuleException("Choose the supplier.");

        // ---- the order this delivery belongs to (if any)
        PurchaseOrder? po = null;
        var poLines = new Dictionary<Guid, PurchaseOrderLine>();
        if (r.PoId is Guid poId)
        {
            po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId && p.BranchId == branchId)
                 ?? throw new BusinessRuleException("This order no longer exists.");
            if (po.SupplierId != supplier.Id)
                throw new BusinessRuleException("This order belongs to another supplier.");
            if (po.Status is PoStatus.Cancelled or PoStatus.Received)
                throw new BusinessRuleException($"This order is already {PoStatus.Label(po.Status).ToLowerInvariant()}.");

            poLines = (await db.PurchaseOrderLines.Where(l => l.PoId == poId).ToListAsync()).ToDictionary(l => l.Id);
        }

        // ---- the items (tracked: their cost price is updated)
        var variantIds = r.Lines.Select(l => l.VariantId).Distinct().ToList();
        var variants = await db.ProductVariants.Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        var productIds = variants.Values.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        // ---- IMEIs: check them all before anything is saved
        var allSerials = r.Lines.SelectMany(l => l.Serials).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var repeated = allSerials.GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).Take(3).ToList();
        if (repeated.Count > 0)
            throw new BusinessRuleException("The same IMEI / serial number appears twice: " + string.Join(", ", repeated));

        var existingSerials = allSerials.Count == 0
            ? new Dictionary<string, SerialItem>(StringComparer.OrdinalIgnoreCase)
            : (await db.SerialItems.Where(s => allSerials.Contains(s.SerialNo)).ToListAsync())
                .ToDictionary(s => s.SerialNo, StringComparer.OrdinalIgnoreCase);

        // ---- the receipt
        var now = DateTime.UtcNow;
        var receipt = new GoodsReceipt
        {
            Id = Guid.CreateVersion7(),
            BranchId = branchId,
            PoId = po?.Id,
            SupplierId = supplier.Id,
            ReceiptNo = await NextNumberAsync(db, "grn.next", "GR"),
            SupplierInvoiceNo = Clean(r.SupplierInvoiceNo),
            ReceivedDate = r.ReceivedDate,
            Notes = Clean(r.Notes),
            CreatedByUserId = _user.UserId
        };
        db.GoodsReceipts.Add(receipt);

        decimal total = 0;
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var line in r.Lines)
        {
            if (!variants.TryGetValue(line.VariantId, out var variant) ||
                !products.TryGetValue(variant.ProductId, out var product))
                throw new BusinessRuleException("One of the items no longer exists.");
            if (product.ProductType != "Stock")
                throw new BusinessRuleException($"'{product.Name}' is a service and cannot be purchased.");

            var name = string.IsNullOrWhiteSpace(variant.VariantName) ? product.Name : $"{product.Name} - {variant.VariantName}";
            var qty = line.Qty;
            var cost = Money(line.UnitCost);

            if (line.PoLineId is Guid poLineId)
            {
                if (!poLines.TryGetValue(poLineId, out var poLine) || poLine.VariantId != variant.Id)
                    throw new BusinessRuleException($"'{name}' is not on this order.");
                poLine.QtyReceived += qty;
            }

            var lineId = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(db.GoodsReceiptLines.Local.Count));
            string? batchNo = null;
            DateOnly? expiry = null;

            if (product.TracksSerial)
            {
                // phones: one IMEI per unit
                if (qty != Math.Floor(qty))
                    throw new BusinessRuleException($"'{name}' is tracked by IMEI, so the quantity must be a whole number.");

                var serials = line.Serials.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (serials.Count != (int)qty)
                    throw new BusinessRuleException($"'{name}': you received {qty:0} but entered {serials.Count} IMEI / serial number(s).");

                foreach (var number in serials)
                {
                    if (!IsValidSerial(number))
                        throw new BusinessRuleException(
                            $"'{number}' is not a valid IMEI / serial number. A 15-digit IMEI must pass the IMEI check digit.");

                    SerialItem item;
                    if (existingSerials.TryGetValue(number, out var found))
                    {
                        if (!(found.Status == "Removed" && found.VariantId == variant.Id))
                            throw new BusinessRuleException($"IMEI / serial {number} is already in the system (status: {found.Status}).");
                        item = found;
                        item.Status = "InStock";
                        item.GoodsReceiptLineId = lineId;
                    }
                    else
                    {
                        item = new SerialItem
                        {
                            Id = Guid.CreateVersion7(),
                            VariantId = variant.Id,
                            SerialNo = number,
                            Status = "InStock",
                            GoodsReceiptLineId = lineId,
                            CreatedAt = now
                        };
                        db.SerialItems.Add(item);
                    }

                    db.StockMovements.Add(NewMovement(branchId, variant.Id, 1, cost, receipt, item.Id, null, name));
                }
            }
            else if (product.TracksBatch)
            {
                // cosmetics: a batch number (and usually an expiry date)
                batchNo = Clean(line.BatchNo) ?? throw new BusinessRuleException($"'{name}': enter the batch number.");
                if (batchNo.Length > 60)
                    throw new BusinessRuleException($"'{name}': the batch number is too long.");
                expiry = line.ExpiryDate;
                if (expiry is { } e && e < today)
                    throw new BusinessRuleException($"'{name}': the expiry date is in the past.");

                var batch = await db.Batches.FirstOrDefaultAsync(b => b.VariantId == variant.Id && b.BatchNo == batchNo);
                if (batch is null)
                {
                    batch = new Batch
                    {
                        Id = Guid.CreateVersion7(),
                        VariantId = variant.Id,
                        BatchNo = batchNo,
                        ExpiryDate = expiry,
                        CostPrice = cost,
                        CreatedAt = now
                    };
                    db.Batches.Add(batch);
                }
                else if (expiry is not null)
                {
                    if (batch.ExpiryDate is null)
                        batch.ExpiryDate = expiry;
                    else if (batch.ExpiryDate != expiry)
                        throw new BusinessRuleException(
                            $"'{name}': batch {batchNo} already exists with expiry {batch.ExpiryDate.Value.ToString("dd MMM yyyy")}.");
                }

                db.StockMovements.Add(NewMovement(branchId, variant.Id, qty, cost, receipt, null, batch.Id, name));
            }
            else
            {
                db.StockMovements.Add(NewMovement(branchId, variant.Id, qty, cost, receipt, null, null, name));
            }

            db.GoodsReceiptLines.Add(new GoodsReceiptLine
            {
                Id = lineId,
                ReceiptId = receipt.Id,
                VariantId = variant.Id,
                PoLineId = line.PoLineId,
                Qty = qty,
                UnitCost = cost,
                BatchNo = batchNo,
                ExpiryDate = expiry
            });

            // the newest purchase price becomes the cost price used for profit
            if (cost > 0) variant.CostPrice = cost;

            total += Money(qty * cost);
        }

        // ---- the order is complete when everything ordered has arrived
        if (po is not null)
            po.Status = poLines.Values.All(l => l.QtyReceived >= l.QtyOrdered) ? PoStatus.Received : PoStatus.PartiallyReceived;

        // ---- what you owe, and what you pay right now
        var paid = Money(r.PaidNow);
        if (paid < 0 || paid > total)
            throw new BusinessRuleException($"The amount paid now must be between 0 and the total ({total:N2}).");

        receipt.Total = total;
        receipt.PaidAmount = paid;
        receipt.DueAmount = total - paid;

        if (paid > 0)
        {
            if (r.PaymentMethodId is null)
                throw new BusinessRuleException("Choose how you paid the supplier.");

            var method = await db.PaymentMethods.AsNoTracking()
                             .FirstOrDefaultAsync(m => m.Id == r.PaymentMethodId && m.IsActive == true)
                         ?? throw new BusinessRuleException("This payment method is not available.");

            if (string.Equals(method.Name, "Cash", StringComparison.OrdinalIgnoreCase) &&
                await _shifts.IsShiftRequiredAsync() && await _shifts.GetOpenShiftAsync() is null)
                throw new BusinessRuleException("Open your shift (cash drawer) first: this cash comes out of your drawer.");

            var payment = new SupplierPayment
            {
                Id = Guid.CreateVersion7(),
                BranchId = branchId,
                SupplierId = supplier.Id,
                ReceiptId = receipt.Id,
                PaymentMethodId = method.Id,
                Amount = paid,
                ReferenceNo = Clean(r.Reference),
                PaidAt = now,
                CreatedByUserId = _user.UserId
            };
            db.SupplierPayments.Add(payment);

            db.Payments.Add(new Payment
            {
                Id = Guid.CreateVersion7(),
                BranchId = branchId,
                SaleId = null,
                CustomerId = null,
                PaymentMethodId = method.Id,
                Direction = "Out",
                Amount = paid,
                ReferenceNo = $"SUPPAY:{payment.Id}",
                PaidAt = now,
                CreatedByUserId = _user.UserId
            });
        }

        AddAudit(db, "GoodsReceived", "GoodsReceipt", receipt.Id);
        await db.SaveChangesAsync();

        return new ReceiptResult(receipt.Id, receipt.ReceiptNo, total, paid, total - paid);
    }

    // ================================================================ helpers

    private StockMovement NewMovement(Guid branchId, Guid variantId, decimal qty, decimal cost,
        GoodsReceipt receipt, Guid? serialItemId, Guid? batchId, string itemName) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = branchId,
        VariantId = variantId,
        BatchId = batchId,
        SerialItemId = serialItemId,
        QtyChange = qty,
        MovementType = "Purchase",
        ReferenceType = "GoodsReceipt",
        ReferenceId = receipt.Id,
        Reason = $"Received {receipt.ReceiptNo}",
        UnitCost = cost,
        CreatedByUserId = _user.UserId,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>PO-000001 / GR-000001 ... The counter is saved together with the document.</summary>
    private static async Task<string> NextNumberAsync(ShopPosDbContext db, string key, string prefix)
    {
        var counter = await db.Settings.FirstOrDefaultAsync(s => s.SettingKey == key);
        if (counter is null)
        {
            counter = new Setting { SettingKey = key, SettingValue = "1" };
            db.Settings.Add(counter);
        }

        var number = long.TryParse(counter.SettingValue, out var n) ? n : 1;
        counter.SettingValue = (number + 1).ToString();
        return $"{prefix}-{number:D6}";
    }

    private void AddAudit(ShopPosDbContext db, string action, string entityName, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>4-40 letters, digits, "-" or "/". A 15-digit number must pass the IMEI (Luhn) check digit.</summary>
    private static bool IsValidSerial(string s)
    {
        if (s.Length < 4 || s.Length > 40) return false;
        if (!s.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '/')) return false;
        if (s.Length == 15 && s.All(char.IsAsciiDigit)) return PassesLuhn(s);
        return true;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}
