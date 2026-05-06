using System;

namespace LocalPlanner.Desktop.Models;

public sealed class TaskItemEditorState
{
    public Guid? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Guid? ProjectId { get; set; }

    public DateTime? DueLocal { get; set; }

    public DateTime? DueEndLocal { get; set; }

    public bool IsOnlyThisDay { get; set; }

    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;
}
