using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Shifts;
using ShopPos.Application.Transfers;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

/// <summary>
/// Money transfers (Western Union, Ria, MoneyGram). You do the real transfer in the provider's own software;
/// this ledger records it, keeps the cash drawer right and shows what to settle with each provider.
///
///   Send    : the customer pays principal + fee to you.             Drawer: + (principal + fee)
///   Receive : you pay the principal out to the customer in cash.    Drawer: - principal
///
/// Only the commission is your income. The principal just passes through.
/// The money that moved is also written to the payments table (without a customer, so customer dues
/// are not affected) so that the shift's expected cash includes it.
/// </summary>
public class TransferService : ITransferService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public TransferService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    // ============================================================ reading

    public async Task<List<TransferRow>> ListAsync(DateOnly day, string? provider, string? search)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;
        var (from, to) = DayRange(day);

        var q = db.MoneyTransfers.AsNoTracking()
            .Where(t => t.BranchId == branchId && t.TxnAt >= from && t.TxnAt < to);

        if (!string.IsNullOrWhiteSpace(provider))
            q = q.Where(t => t.Provider == provider);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            q = q.Where(t => t.ReferenceNo.Contains(term)
                             || (t.SenderName != null && t.SenderName.Contains(term))
                             || (t.ReceiverName != null && t.ReceiverName.Contains(term))
                             || (t.SenderPhone != null && t.SenderPhone.Contains(term))
                             || (t.ReceiverPhone != null && t.ReceiverPhone.Contains(term)));
        }

        var rows = await q.OrderByDescending(t => t.TxnAt)
            .Take(500)
            .Select(t => new
            {
                t.Id, t.TxnAt, t.Provider, t.Direction, t.ReferenceNo,
                t.SenderName, t.SenderPhone, t.ReceiverName, t.ReceiverPhone,
                t.PrincipalAmount, t.FeeCharged, t.CommissionEarned, t.Notes,
                UserName = db.Users.Where(u => u.Id == t.UserId).Select(u => u.FullName).FirstOrDefault(),
                Reversed = db.MoneyTransfers.Any(r => r.BranchId == t.BranchId && r.Provider == t.Provider
                                                      && r.ReferenceNo == t.ReferenceNo && r.Direction == t.Direction
                                                      && r.PrincipalAmount < 0)
            })
            .ToListAsync();

        return rows.Select(r => new TransferRow(
            r.Id, r.TxnAt, r.Provider, r.Direction, r.ReferenceNo,
            r.SenderName, r.SenderPhone, r.ReceiverName, r.ReceiverPhone,
            r.PrincipalAmount, r.FeeCharged, r.CommissionEarned, r.Notes, r.UserName ?? "",
            IsReversal: r.PrincipalAmount < 0,
            IsCancelled: r.PrincipalAmount > 0 && r.Reversed)).ToList();
    }

    public async Task<DailySummary> GetSummaryAsync(DateOnly day)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;
        var (from, to) = DayRange(day);

        var rows = await db.MoneyTransfers.AsNoTracking()
            .Where(t => t.BranchId == branchId && t.TxnAt >= from && t.TxnAt < to)
            .Select(t => new
            {
                t.Provider, t.Direction, t.PrincipalAmount, t.FeeCharged, t.CommissionEarned,
                Reversed = db.MoneyTransfers.Any(r => r.BranchId == t.BranchId && r.Provider == t.Provider
                                                      && r.ReferenceNo == t.ReferenceNo && r.Direction == t.Direction
                                                      && r.PrincipalAmount < 0)
            })
            .ToListAsync();

        var providers = new List<DailyProviderSummary>();
        foreach (var (key, _) in TransferProviders.All)
        {
            var mine = rows.Where(r => r.Provider == key).ToList();
            var sends = mine.Where(r => r.Direction == TransferDirection.Send).ToList();
            var receives = mine.Where(r => r.Direction == TransferDirection.Receive).ToList();

            // Reversal lines carry negative amounts, so a cancelled transfer adds up to zero.
            var sentPrincipal = sends.Sum(r => r.PrincipalAmount);
            var fees = sends.Sum(r => r.FeeCharged);
            var sendCommission = sends.Sum(r => r.CommissionEarned);
            var receivedPrincipal = receives.Sum(r => r.PrincipalAmount);
            var receiveCommission = receives.Sum(r => r.CommissionEarned);

            providers.Add(new DailyProviderSummary(
                key,
                SendCount: sends.Count(r => r.PrincipalAmount > 0 && !r.Reversed),
                ReceiveCount: receives.Count(r => r.PrincipalAmount > 0 && !r.Reversed),
                SentPrincipal: sentPrincipal,
                ReceivedPrincipal: receivedPrincipal,
                FeesCollected: fees,
                Commission: sendCommission + receiveCommission,
                CashIn: sentPrincipal + fees,
                CashOut: receivedPrincipal,
                // To the provider: what you took for sends, less your commission.
                // From the provider: what you paid out for receives, plus your commission on them.
                NetDueToProvider: (sentPrincipal + fees - sendCommission) - (receivedPrincipal + receiveCommission)));
        }

        return new DailySummary(day, providers,
            providers.Sum(p => p.Commission),
            providers.Sum(p => p.CashIn - p.CashOut));
    }

    // ============================================================ recording

    public async Task<Guid> RecordAsync(NewTransfer t)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");

        if (!TransferProviders.All.Any(p => p.Key == t.Provider))
            throw new BusinessRuleException("Choose the provider.");
        if (t.Direction != TransferDirection.Send && t.Direction != TransferDirection.Receive)
            throw new BusinessRuleException("Choose whether the customer is sending or receiving.");

        var reference = Clean(t.ReferenceNo) ?? throw new BusinessRuleException("Enter the reference number (MTCN / PIN).");
        if (reference.Length > 60)
            throw new BusinessRuleException("The reference number is too long.");

        var sender = Clean(t.SenderName) ?? throw new BusinessRuleException("Enter the sender's name.");
        var receiver = Clean(t.ReceiverName) ?? throw new BusinessRuleException("Enter the receiver's name.");

        var principal = Money(t.Principal);
        var isSend = t.Direction == TransferDirection.Send;
        var fee = isSend ? Money(t.Fee) : 0m;                 // a payout has no fee for the customer
        var commission = Money(t.Commission);

        if (principal <= 0)
            throw new BusinessRuleException("Enter the transfer amount.");
        if (fee < 0 || commission < 0)
            throw new BusinessRuleException("The fee and commission cannot be negative.");
        if (isSend && commission > fee)
            throw new BusinessRuleException("Your commission cannot be more than the fee the customer paid.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        if (t.CustomerId is Guid customerId && !await db.Customers.AnyAsync(c => c.Id == customerId))
            throw new BusinessRuleException("This customer no longer exists.");

        // The same transfer must not be entered twice (unless the first one was cancelled).
        var duplicate = await db.MoneyTransfers.AnyAsync(x =>
            x.BranchId == branchId && x.Provider == t.Provider && x.ReferenceNo == reference
            && x.Direction == t.Direction && x.PrincipalAmount > 0
            && !db.MoneyTransfers.Any(r => r.BranchId == x.BranchId && r.Provider == x.Provider
                                           && r.ReferenceNo == x.ReferenceNo && r.Direction == x.Direction
                                           && r.PrincipalAmount < 0));
        if (duplicate)
            throw new BusinessRuleException("This transfer was already recorded.");

        // ---- how the money moves through the drawer
        PaymentMethod method;
        if (isSend)
        {
            if (t.PaymentMethodId is null)
                throw new BusinessRuleException("Choose how the customer pays.");
            method = await db.PaymentMethods.AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == t.PaymentMethodId && m.IsActive == true)
                     ?? throw new BusinessRuleException("This payment method is not available.");
        }
        else
        {
            method = await FindCashMethodAsync(db);
        }

        await RequireShiftForCashAsync(IsCash(method.Name),
            isSend ? "this cash goes into your drawer" : "the payout comes out of your drawer");

        var now = DateTime.UtcNow;
        var label = TransferProviders.LabelOf(t.Provider);

        var transfer = new MoneyTransfer
        {
            Id = Guid.CreateVersion7(),
            BranchId = branchId,
            Provider = t.Provider,
            ReferenceNo = reference,
            Direction = t.Direction,
            CustomerId = t.CustomerId,
            SenderName = sender,
            SenderPhone = Clean(t.SenderPhone),
            ReceiverName = receiver,
            ReceiverPhone = Clean(t.ReceiverPhone),
            PrincipalAmount = principal,
            FeeCharged = fee,
            CommissionEarned = commission,
            Currency = "BDT",
            TxnAt = now,
            Notes = Clean(t.Notes),
            UserId = _user.UserId
        };
        db.MoneyTransfers.Add(transfer);

        db.Payments.Add(NewPayment(branchId, method.Id,
            isSend ? "In" : "Out",
            isSend ? principal + fee : principal,
            Trim80($"{label} {reference}"), now));

        AddAudit(db, "TransferRecorded", transfer.Id);
        await db.SaveChangesAsync();
        return transfer.Id;
    }

    public async Task ReverseAsync(Guid transferId, string reason)
    {
        if (!_user.IsInRole("Owner", "Manager"))
            throw new BusinessRuleException("Only an Owner or Manager can cancel a transfer.");

        var why = Clean(reason) ?? throw new BusinessRuleException("Type the reason for cancelling this transfer.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var original = await db.MoneyTransfers.AsNoTracking()
                           .FirstOrDefaultAsync(x => x.Id == transferId && x.BranchId == branchId)
                       ?? throw new BusinessRuleException("This transfer no longer exists.");

        if (original.PrincipalAmount <= 0)
            throw new BusinessRuleException("This line is already a cancellation.");

        var alreadyReversed = await db.MoneyTransfers.AnyAsync(r =>
            r.BranchId == branchId && r.Provider == original.Provider && r.ReferenceNo == original.ReferenceNo
            && r.Direction == original.Direction && r.PrincipalAmount < 0);
        if (alreadyReversed)
            throw new BusinessRuleException("This transfer was already cancelled.");

        var isSend = original.Direction == TransferDirection.Send;
        var label = TransferProviders.LabelOf(original.Provider);
        var originalReference = Trim80($"{label} {original.ReferenceNo}");

        // The money goes back the way it came: a send is refunded with the method the customer used,
        // a payout is taken back in cash.
        PaymentMethod method;
        if (isSend)
        {
            var originalPayment = await db.Payments.AsNoTracking()
                .Where(p => p.SaleId == null && p.ReferenceNo == originalReference)
                .OrderBy(p => p.PaidAt)
                .FirstOrDefaultAsync();

            method = originalPayment is null
                ? await FindCashMethodAsync(db)
                : await db.PaymentMethods.AsNoTracking().FirstAsync(m => m.Id == originalPayment.PaymentMethodId);
        }
        else
        {
            method = await FindCashMethodAsync(db);
        }

        await RequireShiftForCashAsync(IsCash(method.Name),
            isSend ? "the refund comes out of your drawer" : "the money comes back into your drawer");

        var now = DateTime.UtcNow;
        var note = $"REVERSAL: {why}";

        db.MoneyTransfers.Add(new MoneyTransfer
        {
            Id = Guid.CreateVersion7(),
            BranchId = branchId,
            Provider = original.Provider,
            ReferenceNo = original.ReferenceNo,
            Direction = original.Direction,
            CustomerId = original.CustomerId,
            SenderName = original.SenderName,
            SenderPhone = original.SenderPhone,
            ReceiverName = original.ReceiverName,
            ReceiverPhone = original.ReceiverPhone,
            PrincipalAmount = -original.PrincipalAmount,
            FeeCharged = -original.FeeCharged,
            CommissionEarned = -original.CommissionEarned,
            Currency = original.Currency,
            TxnAt = now,
            Notes = note.Length > 480 ? note[..480] : note,
            UserId = _user.UserId
        });

        db.Payments.Add(NewPayment(branchId, method.Id,
            isSend ? "Out" : "In",
            isSend ? original.PrincipalAmount + original.FeeCharged : original.PrincipalAmount,
            Trim80($"Reversal {label} {original.ReferenceNo}"), now));

        AddAudit(db, "TransferReversed", original.Id);
        await db.SaveChangesAsync();
    }

    // ================================================================ helpers

    private Payment NewPayment(Guid branchId, int methodId, string direction, decimal amount, string reference, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = branchId,
        SaleId = null,
        CustomerId = null,          // on purpose: payments with a customer and no bill would reduce the customer's due
        PaymentMethodId = methodId,
        Direction = direction,
        Amount = amount,
        ReferenceNo = reference,
        PaidAt = now,
        CreatedByUserId = _user.UserId
    };

    private static async Task<PaymentMethod> FindCashMethodAsync(ShopPosDbContext db)
    {
        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.IsActive == true).ToListAsync();
        return methods.FirstOrDefault(m => IsCash(m.Name))
               ?? throw new BusinessRuleException("A payment method called 'Cash' is needed for transfers.");
    }

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
            EntityName = "MoneyTransfer",
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>The start and end (in UTC) of a day on this computer's clock.</summary>
    private static (DateTime From, DateTime To) DayRange(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        return (start, start.AddDays(1));
    }

    private static bool IsCash(string name) => string.Equals(name, "Cash", StringComparison.OrdinalIgnoreCase);
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Trim80(string text) => text.Length > 80 ? text[..80] : text;

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
