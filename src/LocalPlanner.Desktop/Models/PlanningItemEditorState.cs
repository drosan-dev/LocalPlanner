using System;

namespace LocalPlanner.Desktop.Models;

public sealed class PlanningItemEditorState
{
    public Guid? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    public DateTime? PlannedStartLocal { get; set; }

    public DateTime? PlannedEndLocal { get; set; }

    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;

    public bool IsAllDay { get; set; }
}
