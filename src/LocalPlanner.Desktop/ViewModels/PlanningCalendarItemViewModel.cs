using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LocalPlanner.Desktop.Models;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class PlanningCalendarItemViewModel
{
    private static readonly CultureInfo RussianCulture = new("ru-RU");
    private static readonly Brush DraftAccentBrush = CreateBrush(Color.FromRgb(0xF7, 0xC8, 0x73));
    private static readonly Brush DraftSurfaceBrush = CreateBrush(Color.FromArgb(0x24, 0xF7, 0xC8, 0x73));
    private static readonly Brush DraftBorderBrush = CreateBrush(Color.FromArgb(0xA6, 0xF7, 0xC8, 0x73));
    private static readonly Brush ConfirmedBadgeBrush = CreateBrush(Color.FromArgb(0x33, 0x8A, 0xB4, 0xF8));
    private static readonly Brush DraftBadgeBrush = CreateBrush(Color.FromArgb(0x33, 0xF7, 0xC8, 0x73));

    private PlanningCalendarItemViewModel(
        Guid id,
        string title,
        string description,
        DateTime startsAtLocal,
        DateTime endsAtLocal,
        bool isAllDay,
        bool isDraft,
        bool isEditing,
        Brush accentBrush,
        Brush surfaceBrush,
        Brush borderBrush,
        double top,
        double height)
    {
        Id = id;
        Title = title;
        Description = description;
        StartsAtLocal = startsAtLocal;
        EndsAtLocal = endsAtLocal;
        IsAllDay = isAllDay;
        IsDraft = isDraft;
        IsEditing = isEditing;
        AccentBrush = accentBrush;
        SurfaceBrush = surfaceBrush;
        BorderBrush = borderBrush;
        Top = top;
        Height = height;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Description { get; }

    public DateTime StartsAtLocal { get; }

    public DateTime EndsAtLocal { get; }

    public bool IsAllDay { get; }

    public bool IsDraft { get; }

    public bool IsEditing { get; }

    public Brush AccentBrush { get; }

    public Brush SurfaceBrush { get; }

    public Brush BorderBrush { get; }

    public double Top { get; }

    public double Height { get; }

    public string TimeLabel =>
        IsAllDay
            ? "Весь день"
            : $"{StartsAtLocal:HH:mm} - {EndsAtLocal:HH:mm}";

    public string DateLabel => StartsAtLocal.ToString("d MMMM", RussianCulture);

    public FontWeight TitleWeight => IsDraft ? FontWeights.Medium : FontWeights.SemiBold;

    public double Opacity => IsDraft ? 0.92d : 1d;

    public static PlanningCalendarItemViewModel FromEvent(EventListItemViewModel source, DateTime day, double top, double height, bool isEditing = false)
    {
        return new PlanningCalendarItemViewModel(
            source.Id,
            source.Title,
            source.Description,
            source.StartsAtLocal,
            source.EndsAtLocal,
            source.IsAllDay,
            isDraft: false,
            isEditing,
            source.AccentBrush,
            source.AccentSurfaceBrush,
            source.AccentBorderBrush,
            top,
            height);
    }

    public static PlanningCalendarItemViewModel FromPlanningItem(PlanningItem source, DateTime startsAtLocal, DateTime endsAtLocal, double top, double height, bool isEditing = false)
    {
        return new PlanningCalendarItemViewModel(
            source.Id,
            source.Title,
            source.Notes ?? string.Empty,
            startsAtLocal,
            endsAtLocal,
            source.IsAllDay,
            isDraft: true,
            isEditing,
            DraftAccentBrush,
            DraftSurfaceBrush,
            DraftBorderBrush,
            top,
            height);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
