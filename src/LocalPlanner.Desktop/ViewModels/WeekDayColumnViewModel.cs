using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class WeekDayColumnViewModel
{
    private static readonly CultureInfo RussianCulture = new("ru-RU");
    private static readonly Brush DefaultSurfaceBrush = CreateBrush(Color.FromRgb(0x13, 0x21, 0x36));
    private static readonly Brush DefaultBorderBrush = CreateBrush(Color.FromRgb(0x2A, 0x3A, 0x51));
    private static readonly Brush SelectedSurfaceBrush = CreateBrush(Color.FromRgb(0x16, 0x28, 0x47));
    private static readonly Brush SelectedBorderBrush = CreateBrush(Color.FromRgb(0x72, 0xA7, 0xF8));
    private static readonly Brush TodaySurfaceBrush = CreateBrush(Color.FromRgb(0x18, 0x24, 0x3A));
    private static readonly Brush TodayBorderBrush = CreateBrush(Color.FromRgb(0x4B, 0x63, 0x80));

    public WeekDayColumnViewModel(
        DateTime date,
        IReadOnlyList<EventListItemViewModel> events,
        IReadOnlyList<TimeSlotViewModel> timeSlots,
        IReadOnlyList<TimeGridEventViewModel> timeGridEvents,
        bool isSelected)
    {
        Date = date;
        Events = events;
        TimeSlots = timeSlots;
        TimeGridEvents = timeGridEvents;
        IsSelected = isSelected;
        IsToday = date.Date == DateTime.Today;
    }

    public DateTime Date { get; }

    public IReadOnlyList<EventListItemViewModel> Events { get; }

    public IReadOnlyList<TimeSlotViewModel> TimeSlots { get; }

    public IReadOnlyList<TimeGridEventViewModel> TimeGridEvents { get; }

    public bool IsSelected { get; }

    public bool IsToday { get; }

    public string DayOfWeekLabel => Date.ToString("ddd", RussianCulture);

    public string DayNumberLabel => Date.ToString("d MMM", RussianCulture);

    public Visibility EmptyStateVisibility => TimeGridEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string SummaryLabel =>
        Events.Count == 0
            ? "Нет событий"
            : Events.Count == 1
                ? "1 событие"
                : $"{Events.Count} события";

    public string AccessibilityLabel =>
        Events.Count == 0
            ? $"{DayNumberLabel}: событий нет."
            : $"{DayNumberLabel}: {string.Join(", ", Events.Select(x => x.Title))}.";

    public Brush SurfaceBrush =>
        IsSelected
            ? SelectedSurfaceBrush
            : IsToday
                ? TodaySurfaceBrush
                : DefaultSurfaceBrush;

    public Brush BorderBrush =>
        IsSelected
            ? SelectedBorderBrush
            : IsToday
                ? TodayBorderBrush
                : DefaultBorderBrush;

    public Thickness BorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
