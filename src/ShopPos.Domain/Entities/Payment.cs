using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Payment
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid? SaleId { get; set; }

    public Guid? CustomerId { get; set; }

    public int PaymentMethodId { get; set; }

    public string Direction { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? ReferenceNo { get; set; }

    public DateTime PaidAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual PaymentMethod PaymentMethod { get; set; } = null!;

    public virtual Sale? Sale { get; set; }
}
