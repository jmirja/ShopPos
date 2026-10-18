using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class VLowStock
{
    public Guid VariantId { get; set; }

    public string ProductName { get; set; } = null!;

    public string? VariantName { get; set; }

    public string Sku { get; set; } = null!;

    public decimal ReorderLevel { get; set; }

    public decimal QtyOnHand { get; set; }
}
