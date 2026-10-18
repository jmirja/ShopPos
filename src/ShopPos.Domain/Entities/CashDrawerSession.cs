using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class CashDrawerSession
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid UserId { get; set; }

    public DateTime OpenedAt { get; set; }

    public decimal OpeningCash { get; set; }

    public DateTime? ClosedAt { get; set; }

    public decimal? ClosingCash { get; set; }

    public decimal? ExpectedCash { get; set; }

    public decimal? Difference { get; set; }

    public string? Notes { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
