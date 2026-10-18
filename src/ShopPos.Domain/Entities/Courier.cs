using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Courier
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? ContactInfo { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<ParcelBooking> ParcelBookings { get; set; } = new List<ParcelBooking>();
}
