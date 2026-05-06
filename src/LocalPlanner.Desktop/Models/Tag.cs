using System;

namespace LocalPlanner.Desktop.Models;

public sealed class Tag
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
