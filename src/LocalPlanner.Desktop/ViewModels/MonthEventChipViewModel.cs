using System;
using System.Windows.Media;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class MonthEventChipViewModel
{
    public MonthEventChipViewModel(EventListItemViewModel calendarEvent, DateTime displayStart)
    {
        Id = calendarEvent.Id;
        Title = calendarEvent.Title;
        IsAllDay = calendarEvent.IsAllDay;
        AccentBrush = calendarEvent.AccentBrush;
        AccentSurfaceBrush = calendarEvent.AccentSurfaceBrush;
        AccentBorderBrush = calendarEvent.AccentBorderBrush;
        DisplayText = calendarEvent.IsAllDay
            ? calendarEvent.Title
            : $"{displayStart:HH:mm} {calendarEvent.Title}";
    }

    public Guid Id { get; }

    public string Title { get; }

    public string DisplayText { get; }

    public bool IsAllDay { get; }

    public Brush AccentBrush { get; }

    public Brush AccentSurfaceBrush { get; }

    public Brush AccentBorderBrush { get; }
}
