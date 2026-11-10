using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Parcels;
using ShopPos.Application.Shifts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

/// <summary>
/// The parcel desk. Money flows (all written to the payments table so the cash drawer stays right):
///   booking          : the customer pays the charge to you             (In,  key PCLBOOK:id)
///   cancel           : the charge is given back                       (Out, key PCLREFUND:id)
///   pay courier      : you pay the courier's cost for the parcel      (Out, key PCLCOST:id)
///   COD from courier : the courier hands over the cash-on-delivery    (In,  key PCLCOD:id)
///   COD to sender    : you pay that money to the sender               (Out, key PCLSEND:id)
/// The key is the first word of the payment's reference. That is how the app knows, without extra tables,
/// which step of a parcel is already done.
/// </summary>
public class ParcelService : IParcelService
{
    private const string Book = "PCLBOOK:", Refund = "PCLREFUND:", Cost = "PCLCOST:", CodIn = "PCLCOD:", CodOut = "PCLSEND:";

    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public ParcelService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    // ============================================================ couriers

    public async Task<List<CourierDto>> GetCouriersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Couriers.AsNoTracking()
            .Where(c => c.IsActive == true)
            .OrderBy(c => c.Name)
            .Select(c => new CourierDto(c.Id, c.Name, c.ContactInfo))
            .ToListAsync();
    }

    public async Task<CourierDto> AddCourierAsync(string name, string? contactInfo)
    {
        var n = Clean(name) ?? throw new BusinessRuleException("Enter the courier's name.");

        await using var db = await _factory.CreateDbContextAsync();

        if (await db.Couriers.AnyAsync(c => c.Name == n))
            throw new BusinessRuleException("A courier with this name already exists.");

        var courier = new Courier { Name = n, ContactInfo = Clean(contactInfo), IsActive = true };
        db.Couriers.Add(courier);
        await db.SaveChangesAsync();

        return new CourierDto(courier.Id, courier.Name, courier.ContactInfo);
    }

    // ============================================================ reading

    public async Task<List<ParcelRow>> ListAsync(ParcelFilter filter, int? courierId, string? search)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var q = db.ParcelBookings.AsNoTracking().Where(p => p.BranchId == branchId);

        var todayStart = DateTime.Today.ToUniversalTime();
        q = filter switch
        {
            ParcelFilter.Open => q.Where(p => p.Status == "Booked" || p.Status == "InTransit"),
            ParcelFilter.Today => q.Where(p => p.BookedAt >= todayStart),
            ParcelFilter.Last7Days => q.Where(p => p.BookedAt >= todayStart.AddDays(-6)),
            ParcelFilter.Last30Days => q.Where(p => p.BookedAt >= todayStart.AddDays(-29)),
            _ => q
        };

        if (courierId is int cid)
            q = q.Where(p => p.CourierId == cid);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            q = q.Where(p => (p.TrackingNo != null && p.TrackingNo.Contains(term))
                             || p.ReceiverName.Contains(term)
                             || (p.ReceiverPhone != null && p.ReceiverPhone.Contains(term))
                             || (p.SenderName != null && p.SenderName.Contains(term))
                             || (p.SenderPhone != null && p.SenderPhone.Contains(term)));
        }

        var rows = await q.OrderByDescending(p => p.BookedAt)
            .Take(400)
            .Select(p => new
            {
                p.Id, p.BookedAt, p.CourierId, p.TrackingNo, p.SenderName, p.SenderPhone,
                p.ReceiverName, p.ReceiverPhone, p.ReceiverAddress, p.WeightKg,
                p.ChargeCollected, p.CourierCost, p.CodAmount, p.Status, p.Notes,
                CourierName = db.Couriers.Where(c => c.Id == p.CourierId).Select(c => c.Name).FirstOrDefault(),
                UserName = db.Users.Where(u => u.Id == p.UserId).Select(u => u.FullName).FirstOrDefault()
            })
            .ToListAsync();

        var done = await LoadDoneKeysAsync(db);

        return rows.Select(r => new ParcelRow(
            r.Id, r.BookedAt, r.CourierId, r.CourierName ?? "", r.TrackingNo,
            r.SenderName, r.SenderPhone, r.ReceiverName, r.ReceiverPhone, r.ReceiverAddress,
            r.WeightKg, r.ChargeCollected, r.CourierCost, r.CodAmount, r.Status, r.Notes, r.UserName ?? "",
            CostSettled: done.Contains(Cost + r.Id),
            CodReceived: done.Contains(CodIn + r.Id),
            CodPaidToSender: done.Contains(CodOut + r.Id))).ToList();
    }

    // ============================================================ booking

    public async Task<Guid> BookAsync(NewParcel p)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");

        var receiver = Clean(p.ReceiverName) ?? throw new BusinessRuleException("Enter the receiver's name.");
        var address = Clean(p.ReceiverAddress) ?? throw new BusinessRuleException("Enter the receiver's address.");

        if (p.Charge < 0 || p.CourierCost < 0 || p.Cod < 0)
            throw new BusinessRuleException("The charge, the courier's cost and the COD amount cannot be negative.");
        if (p.WeightKg is < 0)
            throw new BusinessRuleException("The weight cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();

        var courier = await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == p.CourierId && c.IsActive == true)
                      ?? throw new BusinessRuleException("Choose the courier.");

        if (p.CustomerId is Guid customerId && !await db.Customers.AnyAsync(c => c.Id == customerId))
            throw new BusinessRuleException("This customer no longer exists.");

        var tracking = Clean(p.TrackingNo);
        if (tracking is not null &&
            await db.ParcelBookings.AnyAsync(x => x.CourierId == courier.Id && x.TrackingNo == tracking && x.Status != "Cancelled"))
            throw new BusinessRuleException("A parcel with this tracking number is already booked with this courier.");

        var charge = Money(p.Charge);

        PaymentMethod? method = null;
        if (charge > 0)
        {
            if (p.PaymentMethodId is null)
                throw new BusinessRuleException("Choose how the customer pays the charge.");

            method = await db.PaymentMethods.AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == p.PaymentMethodId && m.IsActive == true)
                     ?? throw new BusinessRuleException("This payment method is not available.");

            await RequireShiftForCashAsync(IsCash(method.Name), "this cash goes into your drawer");
        }

        var now = DateTime.UtcNow;
        var parcel = new ParcelBooking
        {
            Id = Guid.CreateVersion7(),
            BranchId = _user.BranchId,
            CourierId = courier.Id,
            TrackingNo = tracking,
            CustomerId = p.CustomerId,
            SenderName = Clean(p.SenderName),
            SenderPhone = Clean(p.SenderPhone),
            ReceiverName = receiver,
            ReceiverPhone = Clean(p.ReceiverPhone),
            ReceiverAddress = address,
            WeightKg = p.WeightKg,
            ChargeCollected = charge,
            CourierCost = Money(p.CourierCost),
            CodAmount = Money(p.Cod),
            Status = ParcelStatus.Booked,
            BookedAt = now,
            Notes = Clean(p.Notes),
            UserId = _user.UserId
            // Margin is calculated by the database
        };
        db.ParcelBookings.Add(parcel);

        if (method is not null)
            db.Payments.Add(NewPayment(method.Id, "In", charge, $"{Book}{parcel.Id} {courier.Name}", now));

        AddAudit(db, "ParcelBooked", parcel.Id);
        await db.SaveChangesAsync();
        return parcel.Id;
    }

    public async Task UpdateTrackingAsync(Guid parcelId, string? trackingNo)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var parcel = await LoadParcelAsync(db, parcelId);

        if (parcel.Status is ParcelStatus.Delivered or ParcelStatus.Returned or ParcelStatus.Cancelled)
            throw new BusinessRuleException("The tracking number of a finished parcel cannot be changed.");

        var tracking = Clean(trackingNo);
        if (tracking is not null && tracking.Length > 80)
            throw new BusinessRuleException("The tracking number is too long.");

        if (tracking is not null &&
            await db.ParcelBookings.AnyAsync(x => x.Id != parcel.Id && x.CourierId == parcel.CourierId
                                                  && x.TrackingNo == tracking && x.Status != "Cancelled"))
            throw new BusinessRuleException("Another parcel with this tracking number is already booked with this courier.");

        parcel.TrackingNo = tracking;
        AddAudit(db, "ParcelTracking", parcel.Id);
        await db.SaveChangesAsync();
    }

    public async Task ChangeStatusAsync(Guid parcelId, string newStatus)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var parcel = await LoadParcelAsync(db, parcelId);

        var allowed = parcel.Status switch
        {
            ParcelStatus.Booked => newStatus is ParcelStatus.InTransit or ParcelStatus.Delivered or ParcelStatus.Returned,
            ParcelStatus.InTransit => newStatus is ParcelStatus.Delivered or ParcelStatus.Returned,
            _ => false
        };
        if (!allowed)
            throw new BusinessRuleException($"A parcel that is '{ParcelStatus.Label(parcel.Status)}' cannot be moved to '{ParcelStatus.Label(newStatus)}'.");

        parcel.Status = newStatus;
        AddAudit(db, "ParcelStatus", parcel.Id);
        await db.SaveChangesAsync();
    }

    public async Task CancelAsync(Guid parcelId, string reason)
    {
        if (!_user.IsInRole("Owner", "Manager"))
            throw new BusinessRuleException("Only an Owner or Manager can cancel a parcel (the charge has to be refunded).");

        var why = Clean(reason) ?? throw new BusinessRuleException("Type the reason for cancelling this parcel.");

        await using var db = await _factory.CreateDbContextAsync();
        var parcel = await LoadParcelAsync(db, parcelId);

        if (parcel.Status != ParcelStatus.Booked)
            throw new BusinessRuleException("Only a parcel that is still 'Booked' can be cancelled. Use 'Returned' for parcels that came back.");

        var done = await LoadDoneKeysAsync(db);
        if (done.Contains(Cost + parcel.Id))
            throw new BusinessRuleException("The courier was already paid for this parcel, so it cannot be cancelled.");

        // give the charge back the way it was paid
        var bookRef = Book + parcel.Id;
        var original = await db.Payments.AsNoTracking()
            .Where(x => x.ReferenceNo != null && x.ReferenceNo.StartsWith(bookRef))
            .FirstOrDefaultAsync();

        var now = DateTime.UtcNow;
        if (original is not null)
        {
            var method = await db.PaymentMethods.AsNoTracking().FirstAsync(m => m.Id == original.PaymentMethodId);
            await RequireShiftForCashAsync(IsCash(method.Name), "the refund comes out of your drawer");

            db.Payments.Add(NewPayment(method.Id, "Out", original.Amount, $"{Refund}{parcel.Id}", now));
        }

        parcel.Status = ParcelStatus.Cancelled;
        var note = string.IsNullOrEmpty(parcel.Notes) ? $"Cancelled: {why}" : $"{parcel.Notes} | Cancelled: {why}";
        parcel.Notes = note.Length > 480 ? note[..480] : note;

        AddAudit(db, "ParcelCancelled", parcel.Id);
        await db.SaveChangesAsync();
    }

    // ============================================== settlement with couriers

    public async Task<List<CourierBalance>> GetCourierBalancesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var couriers = await db.Couriers.AsNoTracking().Where(c => c.IsActive == true).OrderBy(c => c.Name).ToListAsync();

        var parcels = await db.ParcelBookings.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.Status != "Cancelled")
            .Select(p => new { p.Id, p.CourierId, p.CourierCost, p.CodAmount, p.Status })
            .ToListAsync();

        var done = await LoadDoneKeysAsync(db);

        var result = new List<CourierBalance>();
        foreach (var c in couriers)
        {
            var mine = parcels.Where(p => p.CourierId == c.Id).ToList();

            var unpaid = mine.Where(p => p.CourierCost > 0 && !done.Contains(Cost + p.Id)).ToList();
            var codPending = mine.Where(p => p.Status == ParcelStatus.Delivered && p.CodAmount > 0
                                             && !done.Contains(CodIn + p.Id)).ToList();
            var codToPay = mine.Where(p => p.Status == ParcelStatus.Delivered && p.CodAmount > 0
                                           && done.Contains(CodIn + p.Id) && !done.Contains(CodOut + p.Id)).ToList();

            result.Add(new CourierBalance(
                c.Id, c.Name,
                unpaid.Count, unpaid.Sum(p => p.CourierCost),
                codPending.Count, codPending.Sum(p => p.CodAmount),
                codToPay.Count, codToPay.Sum(p => p.CodAmount)));
        }

        return result;
    }

    public async Task<decimal> PayCourierCostAsync(int courierId, int paymentMethodId, string? reference)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var method = await LoadMethodAsync(db, paymentMethodId);
        var done = await LoadDoneKeysAsync(db);
        var branchId = _user.BranchId;

        var parcels = await db.ParcelBookings.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.CourierId == courierId && p.Status != "Cancelled" && p.CourierCost > 0)
            .OrderBy(p => p.BookedAt)
            .ToListAsync();

        var open = parcels.Where(p => !done.Contains(Cost + p.Id)).ToList();
        if (open.Count == 0)
            throw new BusinessRuleException("There is nothing to pay this courier.");

        await RequireShiftForCashAsync(IsCash(method.Name), "this payment comes out of your drawer");

        var now = DateTime.UtcNow;
        var suffix = Clean(reference);
        foreach (var p in open)
            db.Payments.Add(NewPayment(method.Id, "Out", p.CourierCost, WithRef($"{Cost}{p.Id}", suffix), now));

        AddAudit(db, "CourierPaid", Guid.Empty);
        await db.SaveChangesAsync();
        return open.Sum(p => p.CourierCost);
    }

    public async Task<decimal> ReceiveCodAsync(int courierId, int paymentMethodId, string? reference)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var method = await LoadMethodAsync(db, paymentMethodId);
        var done = await LoadDoneKeysAsync(db);
        var branchId = _user.BranchId;

        var parcels = await db.ParcelBookings.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.CourierId == courierId
                        && p.Status == "Delivered" && p.CodAmount > 0)
            .OrderBy(p => p.BookedAt)
            .ToListAsync();

        var open = parcels.Where(p => !done.Contains(CodIn + p.Id)).ToList();
        if (open.Count == 0)
            throw new BusinessRuleException("This courier does not owe any COD money.");

        await RequireShiftForCashAsync(IsCash(method.Name), "this cash goes into your drawer");

        var now = DateTime.UtcNow;
        var suffix = Clean(reference);
        foreach (var p in open)
            db.Payments.Add(NewPayment(method.Id, "In", p.CodAmount, WithRef($"{CodIn}{p.Id}", suffix), now));

        AddAudit(db, "CodReceived", Guid.Empty);
        await db.SaveChangesAsync();
        return open.Sum(p => p.CodAmount);
    }

    public async Task PayCodToSenderAsync(Guid parcelId, int paymentMethodId, string? reference)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var parcel = await db.ParcelBookings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId)
                     ?? throw new BusinessRuleException("This parcel no longer exists.");

        if (parcel.Status != ParcelStatus.Delivered || parcel.CodAmount <= 0)
            throw new BusinessRuleException("This parcel has no COD money to pay out.");

        var done = await LoadDoneKeysAsync(db);
        if (!done.Contains(CodIn + parcel.Id))
            throw new BusinessRuleException("Record the COD money from the courier first.");
        if (done.Contains(CodOut + parcel.Id))
            throw new BusinessRuleException("The COD money was already paid to the sender.");

        var method = await LoadMethodAsync(db, paymentMethodId);
        await RequireShiftForCashAsync(IsCash(method.Name), "this payment comes out of your drawer");

        db.Payments.Add(NewPayment(method.Id, "Out", parcel.CodAmount,
            WithRef($"{CodOut}{parcel.Id}", Clean(reference)), DateTime.UtcNow));

        AddAudit(db, "CodPaidToSender", parcel.Id);
        await db.SaveChangesAsync();
    }

    // ================================================================ helpers

    private async Task<ParcelBooking> LoadParcelAsync(ShopPosDbContext db, Guid id)
    {
        var branchId = _user.BranchId;
        return await db.ParcelBookings.FirstOrDefaultAsync(p => p.Id == id && p.BranchId == branchId)
               ?? throw new BusinessRuleException("This parcel no longer exists.");
    }

    private static async Task<PaymentMethod> LoadMethodAsync(ShopPosDbContext db, int id) =>
        await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.IsActive == true)
        ?? throw new BusinessRuleException("This payment method is not available.");

    /// <summary>The keys of all parcel payments made so far, for example "PCLCOST:3f2a...".</summary>
    private static async Task<HashSet<string>> LoadDoneKeysAsync(ShopPosDbContext db)
    {
        var references = await db.Payments.AsNoTracking()
            .Where(p => p.ReferenceNo != null && p.ReferenceNo.StartsWith("PCL"))
            .Select(p => p.ReferenceNo!)
            .ToListAsync();

        return references.Select(r => r.Split(' ')[0]).ToHashSet();
    }

    private static string WithRef(string key, string? userReference)
    {
        var text = userReference is null ? key : $"{key} {userReference}";
        return text.Length > 80 ? text[..80] : text;
    }

    private Payment NewPayment(int methodId, string direction, decimal amount, string reference, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = _user.BranchId,
        SaleId = null,
        CustomerId = null,          // on purpose: a payment with a customer and no bill would reduce the customer's due
        PaymentMethodId = methodId,
        Direction = direction,
        Amount = amount,
        ReferenceNo = reference.Length > 80 ? reference[..80] : reference,
        PaidAt = now,
        CreatedByUserId = _user.UserId
    };

    private async Task RequireShiftForCashAsync(bool cashInvolved, string why)
    {
        if (cashInvolved && await _shifts.IsShiftRequiredAsync() && await _shifts.GetOpenShiftAsync() is null)
            throw new BusinessRuleException($"Open your shift (cash drawer) first: {why}. Open it on the Billing screen.");
    }

    private void AddAudit(ShopPosDbContext db, string action, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = "ParcelBooking",
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static bool IsCash(string name) => string.Equals(name, "Cash", StringComparison.OrdinalIgnoreCase);
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
