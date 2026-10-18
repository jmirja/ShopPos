using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Setting
{
    public string SettingKey { get; set; } = null!;

    public string? SettingValue { get; set; }
}
