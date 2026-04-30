using System;

namespace LocalPlanner.Desktop.ViewModels;

public sealed class TimeSlotViewModel
{
    public TimeSlotViewModel(int hour)
    {
        Hour = hour;
    }

    public int Hour { get; }

    public string Label => $"{Hour:00}:00";
}
