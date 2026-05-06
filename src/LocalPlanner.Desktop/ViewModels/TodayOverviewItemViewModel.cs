using System;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class TodayOverviewItemViewModel
{
    public TodayOverviewItemViewModel(Guid id, string sourceKind, string title, string detail, string badgeText)
    {
        Id = id;
        SourceKind = sourceKind;
        Title = title;
        Detail = detail;
        BadgeText = badgeText;
    }

    public Guid Id { get; }

    public string SourceKind { get; }

    public string Title { get; }

    public string Detail { get; }

    public string BadgeText { get; }

    public bool IsTask => string.Equals(SourceKind, "Task", StringComparison.Ordinal);
}
