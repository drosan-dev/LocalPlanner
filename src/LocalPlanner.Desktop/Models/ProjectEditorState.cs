using System;

namespace LocalPlanner.Desktop.Models;

public sealed class ProjectEditorState
{
    public Guid? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Color { get; set; } = "#FF4F8DFD";

    public string Status { get; set; } = "Active";

    public string? Description { get; set; }

    public string? TagsText { get; set; }
}
