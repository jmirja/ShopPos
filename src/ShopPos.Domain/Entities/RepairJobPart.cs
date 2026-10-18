using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class RepairJobPart
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid VariantId { get; set; }

    public decimal Qty { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal UnitCost { get; set; }

    public virtual RepairJob Job { get; set; } = null!;

    public virtual ProductVariant Variant { get; set; } = null!;
}
