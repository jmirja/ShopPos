using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class RepairJob
{
    public Guid Id { get; set; }

    public string JobNo { get; set; } = null!;

    public Guid BranchId { get; set; }

    public Guid CustomerId { get; set; }

    public string? DeviceBrand { get; set; }

    public string? DeviceModel { get; set; }

    public string? Imei { get; set; }

    public string ProblemDescription { get; set; } = null!;

    public decimal EstimatedCost { get; set; }

    public decimal AdvancePaid { get; set; }

    public decimal? FinalCharge { get; set; }

    public string Status { get; set; } = null!;

    public DateTime ReceivedAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public int WarrantyDays { get; set; }

    public Guid? SaleId { get; set; }

    public string? Notes { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<RepairJobPart> RepairJobParts { get; set; } = new List<RepairJobPart>();

    public virtual ICollection<RepairStatusHistory> RepairStatusHistories { get; set; } = new List<RepairStatusHistory>();

    public virtual Sale? Sale { get; set; }
}
