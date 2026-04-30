using System;

namespace LocalPlanner.Desktop.Models;

public sealed class EventEditorState
{
    public Guid? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime StartLocal { get; set; } = DateTime.Now;

    public DateTime EndLocal { get; set; } = DateTime.Now.AddHours(1);

    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;

    public bool IsAllDay { get; set; }

    public string RRuleText { get; set; } = string.Empty;
}
