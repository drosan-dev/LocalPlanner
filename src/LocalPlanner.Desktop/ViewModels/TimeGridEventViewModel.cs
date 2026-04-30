using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class TimeGridEventViewModel : INotifyPropertyChanged
{
    public TimeGridEventViewModel(EventListItemViewModel source, DateTime day, DateTime displayStart, DateTime displayEnd, double top, double height)
    {
        Source = source;
        Day = day.Date;
        _displayStart = displayStart;
        _displayEnd = displayEnd;
        _top = top;
        _height = height;
    }

    private DateTime _displayStart;
    private DateTime _displayEnd;
    private double _top;
    private double _height;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EventListItemViewModel Source { get; }

    public Guid Id => Source.Id;

    public DateTime Day { get; }

    public DateTime DisplayStart => _displayStart;

    public DateTime DisplayEnd => _displayEnd;

    public double Top
    {
        get => _top;
        private set
        {
            if (Math.Abs(_top - value) < 0.001d)
            {
                return;
            }

            _top = value;
            OnPropertyChanged();
        }
    }

    public double Height
    {
        get => _height;
        private set
        {
            if (Math.Abs(_height - value) < 0.001d)
            {
                return;
            }

            _height = value;
            OnPropertyChanged();
        }
    }

    public string Title => Source.Title;

    public string Description => Source.Description;

    public string TimeLabel =>
        Source.IsAllDay
            ? "Весь день"
            : $"{DisplayStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {DisplayEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    public Brush AccentBrush => Source.AccentBrush;

    public Brush AccentSurfaceBrush => Source.AccentSurfaceBrush;

    public Brush AccentBorderBrush => Source.AccentBorderBrush;

    public void UpdateLayout(DateTime displayStart, DateTime displayEnd, double top, double height)
    {
        var timeChanged = displayStart != _displayStart || displayEnd != _displayEnd;
        _displayStart = displayStart;
        _displayEnd = displayEnd;
        Top = top;
        Height = height;

        if (timeChanged)
        {
            OnPropertyChanged(nameof(DisplayStart));
            OnPropertyChanged(nameof(DisplayEnd));
            OnPropertyChanged(nameof(TimeLabel));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
