using System;
using LocalPlanner.Desktop.Models;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class TaskItemListItemViewModel
{
    public TaskItemListItemViewModel(TaskItem item, string? projectTitle)
    {
        Id = item.Id;
        Title = item.Title;
        ProjectTitle = projectTitle;
        ProjectId = item.ProjectId;
        DueAtUtc = item.DueAtUtc;
        DueEndsAtUtc = item.DueEndsAtUtc;
        CreatedAtUtc = item.CreatedAtUtc;
        DueText = FormatDueDate(item);
        StatusText = item.IsCompleted ? "Готово" : "В работе";
        IsCompleted = item.IsCompleted;
        IsOnlyThisDay = item.IsOnlyThisDay;
        UpdatedAtUtc = item.UpdatedAtUtc;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string? ProjectTitle { get; }

    public Guid? ProjectId { get; }

    public DateTime? DueAtUtc { get; }

    public DateTime? DueEndsAtUtc { get; }

    public string DueText { get; }

    public string StatusText { get; }

    public bool IsCompleted { get; }

    public bool IsOnlyThisDay { get; }

    public DateTime UpdatedAtUtc { get; }

    public DateTime CreatedAtUtc { get; }

    private static string FormatDueDate(TaskItem item)
    {
        if (item.DueAtUtc is null)
        {
            return "Без срока";
        }

        var timezone = ResolveTimezone(item.TimezoneId);
        var due = TimeZoneInfo.ConvertTimeFromUtc(item.DueAtUtc.Value, timezone);
        if (item.DueEndsAtUtc is null)
        {
            return due.TimeOfDay == TimeSpan.Zero
                ? due.ToString("d MMMM")
                : $"{due:d MMMM} · {due:HH:mm}";
        }

        var dueEnd = TimeZoneInfo.ConvertTimeFromUtc(item.DueEndsAtUtc.Value, timezone);
        return $"{due:d MMMM} · {due:HH:mm}-{dueEnd:HH:mm}";
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
