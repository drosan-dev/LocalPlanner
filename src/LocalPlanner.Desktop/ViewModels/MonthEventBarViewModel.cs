using System;
using System.Windows;
using System.Windows.Media;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class MonthEventBarViewModel
{
    private static readonly Brush PlaceholderBrush = CreateBrush(Colors.Transparent);

    private MonthEventBarViewModel(DateTime day)
    {
        Id = Guid.Empty;
        Title = string.Empty;
        Date = day.Date;
        IsStart = true;
        IsEnd = true;
        DisplayText = string.Empty;
        AccentBrush = PlaceholderBrush;
        AccentSurfaceBrush = PlaceholderBrush;
        AccentBorderBrush = PlaceholderBrush;
        Visibility = Visibility.Hidden;
    }

    public MonthEventBarViewModel(EventListItemViewModel calendarEvent, DateTime day, DateTime segmentStart, DateTime segmentEnd)
    {
        Id = calendarEvent.Id;
        Title = calendarEvent.Title;
        Date = day.Date;
        IsStart = Date == segmentStart.Date;
        IsEnd = Date == segmentEnd.Date;
        AccentBrush = calendarEvent.AccentBrush;
        AccentSurfaceBrush = calendarEvent.AccentSurfaceBrush;
        AccentBorderBrush = calendarEvent.AccentBorderBrush;
        DisplayText = IsStart ? calendarEvent.Title : string.Empty;
        Visibility = Visibility.Visible;
    }

    public Guid Id { get; }

    public string Title { get; }

    public DateTime Date { get; }

    public bool IsStart { get; }

    public bool IsEnd { get; }

    public string DisplayText { get; }

    public Brush AccentBrush { get; }

    public Brush AccentSurfaceBrush { get; }

    public Brush AccentBorderBrush { get; }

    public Visibility Visibility { get; }

    public CornerRadius CornerRadius => new(IsStart ? 8 : 2, IsEnd ? 8 : 2, IsEnd ? 8 : 2, IsStart ? 8 : 2);

    public Thickness Margin => new(IsStart ? 0 : -8, 2, IsEnd ? 0 : -8, 0);

    public static MonthEventBarViewModel Placeholder(DateTime day)
    {
        return new MonthEventBarViewModel(day);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
