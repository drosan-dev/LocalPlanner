using System;

namespace LocalPlanner.Desktop.Models;

public sealed class TaskItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Guid? ProjectId { get; set; }

    public DateTime? DueAtUtc { get; set; }

    public DateTime? DueEndsAtUtc { get; set; }

    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;

    public bool IsCompleted { get; set; }

    public bool IsOnlyThisDay { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }
}
