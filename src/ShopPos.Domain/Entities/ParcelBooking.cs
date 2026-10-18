using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class ParcelBooking
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public int CourierId { get; set; }

    public string? TrackingNo { get; set; }

    public Guid? CustomerId { get; set; }

    public string? SenderName { get; set; }

    public string? SenderPhone { get; set; }

    public string ReceiverName { get; set; } = null!;

    public string? ReceiverPhone { get; set; }

    public string? ReceiverAddress { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal ChargeCollected { get; set; }

    public decimal CourierCost { get; set; }

    public decimal? Margin { get; set; }

    public decimal CodAmount { get; set; }

    public string Status { get; set; } = null!;

    public DateTime BookedAt { get; set; }

    public string? Notes { get; set; }

    public Guid UserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual Courier Courier { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual User User { get; set; } = null!;
}
