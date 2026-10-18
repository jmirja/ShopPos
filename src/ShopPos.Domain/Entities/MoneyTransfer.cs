using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class MoneyTransfer
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public string Provider { get; set; } = null!;

    public string ReferenceNo { get; set; } = null!;

    public string Direction { get; set; } = null!;

    public Guid? CustomerId { get; set; }

    public string? SenderName { get; set; }

    public string? SenderPhone { get; set; }

    public string? ReceiverName { get; set; }

    public string? ReceiverPhone { get; set; }

    public decimal PrincipalAmount { get; set; }

    public decimal FeeCharged { get; set; }

    public decimal CommissionEarned { get; set; }

    public string Currency { get; set; } = null!;

    public DateTime TxnAt { get; set; }

    public string? Notes { get; set; }

    public Guid UserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual User User { get; set; } = null!;
}
