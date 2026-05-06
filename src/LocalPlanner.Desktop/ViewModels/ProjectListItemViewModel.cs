using System;
using System.Collections.Generic;
using System.Linq;
using LocalPlanner.Desktop.Models;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class ProjectListItemViewModel
{
    public ProjectListItemViewModel(Project project, IReadOnlyList<Tag> tags)
    {
        Id = project.Id;
        Title = project.Title;
        Color = project.Color;
        Status = project.Status;
        Description = project.Description ?? string.Empty;
        Tags = tags;
        TagsText = tags.Count == 0
            ? "Без тегов"
            : string.Join(", ", tags.Select(tag => $"#{tag.Name}"));
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Color { get; }

    public string Status { get; }

    public string StatusText => Status switch
    {
        "Paused" => "На паузе",
        "Completed" => "Завершён",
        _ => "Активен"
    };

    public string Description { get; }

    public IReadOnlyList<Tag> Tags { get; }

    public string TagsText { get; }
}
