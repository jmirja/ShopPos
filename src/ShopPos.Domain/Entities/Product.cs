using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Product
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public Guid? CategoryId { get; set; }

    public string ProductType { get; set; } = null!;

    public string? Brand { get; set; }

    public bool TracksSerial { get; set; }

    public bool TracksBatch { get; set; }

    public bool? IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Category? Category { get; set; }

    public virtual ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();
}
