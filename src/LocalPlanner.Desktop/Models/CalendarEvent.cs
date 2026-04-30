using System;

namespace LocalPlanner.Desktop.Models;

public sealed class CalendarEvent
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;

    public bool IsAllDay { get; set; }

    public string? RRuleText { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
}
