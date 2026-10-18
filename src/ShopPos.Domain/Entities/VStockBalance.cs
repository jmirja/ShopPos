using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class VStockBalance
{
    public Guid VariantId { get; set; }

    public decimal? QtyOnHand { get; set; }
}
