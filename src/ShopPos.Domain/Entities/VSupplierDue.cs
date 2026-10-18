using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class VSupplierDue
{
    public Guid? SupplierId { get; set; }

    public string? Name { get; set; }

    public decimal DueAmount { get; set; }
}
