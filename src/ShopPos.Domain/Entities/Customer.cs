using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Customer
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }

    public decimal OpeningDue { get; set; }

    public bool? IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<MoneyTransfer> MoneyTransfers { get; set; } = new List<MoneyTransfer>();

    public virtual ICollection<ParcelBooking> ParcelBookings { get; set; } = new List<ParcelBooking>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<RepairJob> RepairJobs { get; set; } = new List<RepairJob>();

    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
