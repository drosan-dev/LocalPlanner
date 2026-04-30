using System;
using System.Globalization;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class MonthHeaderViewModel
{
    private static readonly CultureInfo RussianCulture = new("ru-RU");

    public MonthHeaderViewModel(DayOfWeek dayOfWeek)
    {
        DayOfWeek = dayOfWeek;
    }

    public DayOfWeek DayOfWeek { get; }

    public string Label => RussianCulture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek);
}
