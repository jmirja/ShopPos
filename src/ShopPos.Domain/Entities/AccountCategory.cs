using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class AccountCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public bool IsSystem { get; set; }

    public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
