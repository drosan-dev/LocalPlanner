using System;
using System.Globalization;
using System.Windows.Media;
using LocalPlanner.Desktop.Models;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class EventListItemViewModel
{
    private static readonly CultureInfo RussianCulture = new("ru-RU");
    private static readonly Color[] AccentPalette =
    {
        Color.FromRgb(0x1A, 0x73, 0xE8),
        Color.FromRgb(0x03, 0x96, 0xBE),
        Color.FromRgb(0x0B, 0x80, 0x43),
        Color.FromRgb(0xF4, 0x51, 0x1E),
        Color.FromRgb(0x8E, 0x24, 0xAA),
        Color.FromRgb(0xE3, 0x74, 0x00)
    };

    public EventListItemViewModel(CalendarEvent calendarEvent)
    {
        Id = calendarEvent.Id;
        Title = calendarEvent.Title;
        Description = calendarEvent.Description ?? string.Empty;
        StartsAtLocal = TimeZoneInfo.ConvertTimeFromUtc(calendarEvent.StartsAtUtc, TimeZoneInfo.Local);
        EndsAtLocal = TimeZoneInfo.ConvertTimeFromUtc(calendarEvent.EndsAtUtc, TimeZoneInfo.Local);
        TimezoneId = calendarEvent.TimezoneId;
        IsAllDay = calendarEvent.IsAllDay;
        RRuleText = calendarEvent.RRuleText ?? string.Empty;

        var accentColor = AccentPalette[Math.Abs(calendarEvent.Id.GetHashCode()) % AccentPalette.Length];
        AccentBrush = CreateBrush(accentColor);
        AccentSurfaceBrush = CreateBrush(Color.FromArgb(0x1A, accentColor.R, accentColor.G, accentColor.B));
        AccentBorderBrush = CreateBrush(Color.FromArgb(0x4D, accentColor.R, accentColor.G, accentColor.B));
        AccentBadgeBrush = CreateBrush(Color.FromArgb(0x26, accentColor.R, accentColor.G, accentColor.B));
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Description { get; }

    public DateTime StartsAtLocal { get; }

    public DateTime EndsAtLocal { get; }

    public string TimezoneId { get; }

    public bool IsAllDay { get; }

    public string RRuleText { get; }

    public Brush AccentBrush { get; }

    public Brush AccentSurfaceBrush { get; }

    public Brush AccentBorderBrush { get; }

    public Brush AccentBadgeBrush { get; }

    public DateTime Day => StartsAtLocal.Date;

    public string DayLabel => StartsAtLocal.ToString("dddd", RussianCulture);

    public string TimeLabel =>
        IsAllDay
            ? "Весь день"
            : $"{StartsAtLocal:HH:mm} - {EndsAtLocal:HH:mm}";

    public string ScheduleSummary =>
        IsAllDay
            ? $"{StartsAtLocal.ToString("d MMM yyyy", RussianCulture)} (весь день)"
            : $"{StartsAtLocal.ToString("d MMM yyyy HH:mm", RussianCulture)} - {EndsAtLocal:HH:mm}";

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
