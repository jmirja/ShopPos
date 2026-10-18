namespace ShopPos.Domain.Entities;

public partial class RepairStatusHistory
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string Status { get; set; } = null!;

    public string? Note { get; set; }

    public Guid? ChangedByUserId { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual User? ChangedByUser { get; set; }

    public virtual RepairJob Job { get; set; } = null!;
}
