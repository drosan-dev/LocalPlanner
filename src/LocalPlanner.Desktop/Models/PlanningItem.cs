using System;

namespace LocalPlanner.Desktop.Models;

public sealed class PlanningItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Guid? ProjectId { get; set; }

    public DateTime? PlannedStartsAtUtc { get; set; }

    public DateTime? PlannedEndsAtUtc { get; set; }

    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;

    public bool IsAllDay { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }
}
