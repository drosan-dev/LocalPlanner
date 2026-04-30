using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class MonthDayCellViewModel
{
    private static readonly CultureInfo RussianCulture = new("ru-RU");
    private static readonly Brush CurrentMonthSurfaceBrush = CreateBrush(Color.FromRgb(0x13, 0x21, 0x36));
    private static readonly Brush OutsideMonthSurfaceBrush = CreateBrush(Color.FromRgb(0x0D, 0x16, 0x27));
    private static readonly Brush SelectedSurfaceBrush = CreateBrush(Color.FromRgb(0x16, 0x28, 0x47));
    private static readonly Brush DefaultBorderBrush = CreateBrush(Color.FromRgb(0x24, 0x30, 0x42));
    private static readonly Brush TodayBorderBrush = CreateBrush(Color.FromRgb(0x8A, 0xB4, 0xF8));
    private static readonly Brush SelectedBorderBrush = CreateBrush(Color.FromRgb(0x72, 0xA7, 0xF8));
    private static readonly Brush PrimaryTextBrush = CreateBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
    private static readonly Brush MutedTextBrush = CreateBrush(Color.FromRgb(0x7F, 0x8E, 0xA3));

    public MonthDayCellViewModel(
        DateTime date,
        DateTime displayMonth,
        DateTime selectedDate,
        bool isToday,
        IReadOnlyList<MonthEventBarViewModel> eventBars,
        IReadOnlyList<MonthEventChipViewModel> visibleChips,
        int overflowCount,
        string accessibilityLabel)
    {
        Date = date.Date;
        IsInCurrentMonth = Date.Month == displayMonth.Month && Date.Year == displayMonth.Year;
        IsToday = isToday;
        IsSelected = Date == selectedDate.Date;
        EventBars = eventBars;
        VisibleChips = visibleChips;
        OverflowCount = overflowCount;
        AccessibilityLabel = accessibilityLabel;
    }

    public DateTime Date { get; }

    public bool IsInCurrentMonth { get; }

    public bool IsToday { get; }

    public bool IsSelected { get; }

    public IReadOnlyList<MonthEventBarViewModel> EventBars { get; }

    public IReadOnlyList<MonthEventChipViewModel> VisibleChips { get; }

    public int OverflowCount { get; }

    public string DayNumberLabel => Date.Day.ToString(CultureInfo.InvariantCulture);

    public string AccessibilityLabel { get; }

    public string OverflowLabel => $"+{OverflowCount} \u0435\u0449\u0451";

    public Visibility OverflowVisibility => OverflowCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush SurfaceBrush => IsSelected
        ? SelectedSurfaceBrush
        : IsInCurrentMonth
            ? CurrentMonthSurfaceBrush
            : OutsideMonthSurfaceBrush;

    public Brush BorderBrush => IsSelected
        ? SelectedBorderBrush
        : IsToday
            ? TodayBorderBrush
            : DefaultBorderBrush;

    public Thickness BorderThickness => IsSelected ? new Thickness(2) : IsToday ? new Thickness(1.5) : new Thickness(1);

    public Brush DayNumberBrush => IsInCurrentMonth ? PrimaryTextBrush : MutedTextBrush;

    public FontWeight DayNumberWeight => IsToday || IsSelected ? FontWeights.Bold : FontWeights.SemiBold;

    public string DayCaption => Date.ToString("d MMMM", RussianCulture);

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
