using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class VCustomerDue
{
    public Guid? CustomerId { get; set; }

    public string? Name { get; set; }

    public string? Phone { get; set; }

    public decimal DueAmount { get; set; }
}
