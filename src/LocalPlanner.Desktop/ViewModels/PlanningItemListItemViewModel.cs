using System;
using LocalPlanner.Desktop.Models;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class PlanningItemListItemViewModel
{
    public PlanningItemListItemViewModel(PlanningItem item, string? projectTitle)
    {
        Id = item.Id;
        Title = item.Title;
        ProjectTitle = projectTitle;
        ScheduleText = FormatSchedule(item);
        UpdatedAtUtc = item.UpdatedAtUtc;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string? ProjectTitle { get; }

    public string ScheduleText { get; }

    public DateTime UpdatedAtUtc { get; }

    private static string FormatSchedule(PlanningItem item)
    {
        if (item.PlannedStartsAtUtc is null)
        {
            return "Без даты";
        }

        var timezone = ResolveTimezone(item.TimezoneId);
        var start = TimeZoneInfo.ConvertTimeFromUtc(item.PlannedStartsAtUtc.Value, timezone);
        if (item.IsAllDay)
        {
            return start.ToString("d MMMM");
        }

        var end = item.PlannedEndsAtUtc is null
            ? start.AddHours(1)
            : TimeZoneInfo.ConvertTimeFromUtc(item.PlannedEndsAtUtc.Value, timezone);

        return $"{start:d MMMM} · {start:HH:mm}-{end:HH:mm}";
    }

    private static TimeZoneInfo ResolveTimezone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
