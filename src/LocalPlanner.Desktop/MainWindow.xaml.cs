using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalPlanner.Desktop.Models;
using LocalPlanner.Desktop.Services;
using LocalPlanner.Desktop.ViewModels;

namespace LocalPlanner.Desktop;

public partial class MainWindow : Window
{
    private const double HourSlotHeight = 64d;
    private const int MinutesStep = 15;
    private const int MinimumDurationMinutes = 30;
    private const int MinutesPerDay = 24 * 60;
    private const string NewEventDefaultTitle = "\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435";
    private static readonly CultureInfo RussianCulture = new("ru-RU");
    private static readonly SolidColorBrush MarkerFallbackBrush = CreateFrozenBrush(Color.FromRgb(0x1A, 0x73, 0xE8));

    private readonly EventRepository _eventRepository;
    private readonly ObservableCollection<EventListItemViewModel> _allEvents = new();
    private readonly ObservableCollection<EventListItemViewModel> _selectedDayEvents = new();
    private readonly ObservableCollection<WeekDayColumnViewModel> _selectedWeekDays = new();
    private readonly ObservableCollection<TimeSlotViewModel> _timeSlots = new();
    private readonly ObservableCollection<TimeGridEventViewModel> _selectedDayTimelineEvents = new();

    private Guid? _selectedEventId;
    private bool _isSynchronizingSelection;
    private bool _isTimelineCommitScheduled;
    private bool _isTimelineReloadScheduled;
    private UIElement? _activeTimelineCaptureElement;
    private Point? _lastTimelinePointerPosition;
    private CalendarViewMode _viewMode = CalendarViewMode.Month;
    private TimelineInteractionState? _activeTimelineInteraction;
    private Brush? _draftPreviewAccentBrush;
    private Brush? _draftPreviewSurfaceBrush;
    private Brush? _draftPreviewBorderBrush;

    public MainWindow()
    {
        InitializeComponent();

        _eventRepository = ((App)Application.Current).EventRepository
            ?? throw new InvalidOperationException("\u0425\u0440\u0430\u043D\u0438\u043B\u0438\u0449\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u043D\u0435 \u0438\u043D\u0438\u0446\u0438\u0430\u043B\u0438\u0437\u0438\u0440\u043E\u0432\u0430\u043D\u043E.");

        for (var hour = 0; hour < 24; hour++)
        {
            _timeSlots.Add(new TimeSlotViewModel(hour));
        }

        DailyEventsListBox.ItemsSource = _selectedDayEvents;
        WeekHeadersItemsControl.ItemsSource = _selectedWeekDays;
        WeekBodiesItemsControl.ItemsSource = _selectedWeekDays;
        WeekHoursItemsControl.ItemsSource = _timeSlots;
        DayHoursItemsControl.ItemsSource = _timeSlots;
        DaySlotLinesItemsControl.ItemsSource = _timeSlots;
        DayTimelineItemsControl.ItemsSource = _selectedDayTimelineEvents;
        TimezoneComboBox.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        TimezoneComboBox.SelectedItem = TimeZoneInfo.Local;
        MonthViewRadioButton.IsChecked = true;

        SeedEditorDefaults();
        SetSelectedDate(DateTime.Today, prepareQuickCreate: false);
        UpdateEditorVisibility(false);
        LoadEvents();
    }

    private void LoadEvents()
    {
        _allEvents.Clear();

        foreach (var calendarEvent in _eventRepository.GetActiveEvents())
        {
            _allEvents.Add(new EventListItemViewModel(calendarEvent));
        }

        RefreshSelectedDayEvents();
        RefreshSelectedWeekDays();
        RefreshCalendarChrome();
        RefreshCalendarMarkers();

        StatusTextBlock.Text = _allEvents.Count == 0
            ? "\u041A\u0430\u043B\u0435\u043D\u0434\u0430\u0440\u044C \u043F\u0443\u0441\u0442. \u0421\u043E\u0437\u0434\u0430\u0439\u0442\u0435 \u043F\u0435\u0440\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435."
            : $"\u0412\u0441\u0435\u0433\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u0439: {_allEvents.Count}.";
    }

    private void EventsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        if ((sender as Selector)?.SelectedItem is not EventListItemViewModel selected)
        {
            return;
        }

        ApplySelectionToEditor(selected);
    }

    private void EventCard_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EventListItemViewModel selected } &&
            TryToggleSelectedEventEditor(selected.Id))
        {
            e.Handled = true;
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var editorState = ReadEditorState();
            var saved = _eventRepository.Save(editorState);
            LoadEvents();
            SelectEvent(saved.Id);
            StatusTextBlock.Text = $"\u0421\u043E\u0431\u044B\u0442\u0438\u0435 \u00AB{saved.Title}\u00BB \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u043E.";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = exception.Message;
        }
    }

    private void NewButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        PrepareQuickCreateForDate(selectedDate, "\u0417\u0430\u043F\u043E\u043B\u043D\u0438\u0442\u0435 \u0444\u043E\u0440\u043C\u0443 \u043D\u043E\u0432\u043E\u0433\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u044F.");
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedEventId is null)
        {
            StatusTextBlock.Text = "\u0421\u043D\u0430\u0447\u0430\u043B\u0430 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435.";
            return;
        }

        if (_eventRepository.SoftDelete(_selectedEventId.Value))
        {
            CloseEditor();
            LoadEvents();
            StatusTextBlock.Text = "\u0421\u043E\u0431\u044B\u0442\u0438\u0435 \u0443\u0434\u0430\u043B\u0435\u043D\u043E.";
        }
        else
        {
            StatusTextBlock.Text = "\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0443\u0434\u0430\u043B\u0438\u0442\u044C \u0441\u043E\u0431\u044B\u0442\u0438\u0435.";
        }
    }

    private void Calendar_OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshCalendarMarkers();
    }

    private void MiniCalendar_OnSelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        SetSelectedDate(MiniCalendar.SelectedDate ?? DateTime.Today, prepareQuickCreate: false);
    }

    private void MonthCalendar_OnSelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        SetSelectedDate(MonthCalendar.SelectedDate ?? DateTime.Today, prepareQuickCreate: false);
    }

    private void MonthCalendar_OnDisplayDateChanged(object? sender, CalendarDateChangedEventArgs e)
    {
        RefreshCalendarChrome();
        RefreshCalendarMarkers();
    }

    private void ViewModeRadioButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string tag)
        {
            return;
        }

        var nextMode = tag switch
        {
            "Day" => CalendarViewMode.Day,
            "Week" => CalendarViewMode.Week,
            _ => CalendarViewMode.Month
        };

        SetViewMode(nextMode);
    }

    private void PreviousRangeButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateRange(-1);
    }

    private void NextRangeButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateRange(1);
    }

    private void WeekDayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DateTime date)
        {
            return;
        }

        SetSelectedDate(date, prepareQuickCreate: false);
        SetViewMode(CalendarViewMode.Day);
        DayViewRadioButton.IsChecked = true;
    }

    private void TodayButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetSelectedDate(DateTime.Today, prepareQuickCreate: false);
    }

    private void QuickCreateSelectedDateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        PrepareQuickCreateForDate(selectedDate, $"\u041F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043B\u0435\u043D\u043E \u043D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435 \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}.");
    }

    private void CloseEditorButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseEditor();
        StatusTextBlock.Text = "\u041F\u0430\u043D\u0435\u043B\u044C \u0441\u0432\u0435\u0434\u0435\u043D\u0438\u0439 \u0437\u0430\u043A\u0440\u044B\u0442\u0430.";
    }

    private void TimelineEventThumb_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TimelineEventDragSurface_OnMouseLeftButtonDown(sender, e);
    }

    private void TimelineEventDragSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTimelineResizeHandleOrigin(sender as DependencyObject, e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginPointerTimelineInteraction(sender, e, TimelineInteractionMode.Move);
    }

    private void TimelineResizeSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginPointerTimelineInteraction(sender, e, TimelineInteractionMode.ResizeEnd);
    }

    private void TimelineCreateSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject originalSource && TryGetTimelineEvent(originalSource, out _))
        {
            return;
        }

        BeginTimelineCreationInteraction(sender, e);
    }

    private void TimelineInteractionSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeTimelineInteraction is null || _activeTimelineCaptureElement is not FrameworkElement captureElement)
        {
            return;
        }

        if (!ReferenceEquals(sender, captureElement) || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        if (_lastTimelinePointerPosition is not Point previousPosition)
        {
            _lastTimelinePointerPosition = currentPosition;
            return;
        }

        var verticalChange = currentPosition.Y - previousPosition.Y;
        _lastTimelinePointerPosition = currentPosition;

        if (Math.Abs(verticalChange) > double.Epsilon)
        {
            UpdateTimelineInteraction(verticalChange, e.GetPosition(captureElement));
        }
    }

    private void TimelineInteractionSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPointerTimelineInteraction(sender as UIElement, releaseCapture: true);
        e.Handled = true;
    }

    private void TimelineInteractionSurface_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndPointerTimelineInteraction(sender as UIElement, releaseCapture: false);
    }

    private void BeginPointerTimelineInteraction(object sender, MouseButtonEventArgs e, TimelineInteractionMode mode)
    {
        BeginTimelineInteraction(sender, e, mode);

        if (_activeTimelineInteraction is null || sender is not UIElement element)
        {
            return;
        }

        _activeTimelineCaptureElement = element;
        _lastTimelinePointerPosition = e.GetPosition(this);
        element.CaptureMouse();
        e.Handled = true;
    }

    private void BeginTimelineCreationInteraction(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var start = ResolveTimelineDraftStart(element, e.GetPosition(element));
        var end = start.AddMinutes(MinimumDurationMinutes);
        _activeTimelineInteraction = new TimelineInteractionState(
            null,
            start.Date,
            start,
            end,
            TimelineInteractionMode.Create);

        _isSynchronizingSelection = true;
        DailyEventsListBox.SelectedItem = null;
        _isSynchronizingSelection = false;

        _selectedEventId = null;
        DeleteButton.IsEnabled = false;
        StatusTextBlock.Text = $"\u0427\u0435\u0440\u043D\u043E\u0432\u0438\u043A \u043D\u043E\u0432\u043E\u0433\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u044F: {start:HH:mm} - {end:HH:mm}.";
        _activeTimelineCaptureElement = element;
        _lastTimelinePointerPosition = e.GetPosition(this);
        element.CaptureMouse();
        UpdateTimelinePreview(_activeTimelineInteraction, dayChanged: false);
        e.Handled = true;
    }

    private void EndPointerTimelineInteraction(UIElement? element, bool releaseCapture)
    {
        if (_activeTimelineInteraction is null)
        {
            _activeTimelineCaptureElement = null;
            _lastTimelinePointerPosition = null;
            HideWeekDragPreview();
            HideDayDragPreview();
            return;
        }
        var capturedElement = _activeTimelineCaptureElement;

        if (_activeTimelineInteraction.EventId is Guid activeEventId)
        {
            SetWeekTimelineEventOpacity(activeEventId, 1d);
        }
        _activeTimelineCaptureElement = null;
        _lastTimelinePointerPosition = null;

        if (releaseCapture && capturedElement?.IsMouseCaptured == true)
        {
            capturedElement.ReleaseMouseCapture();
        }

        HideWeekDragPreview();
        HideDayDragPreview();

        if (TryHandleTimelineEventClick())
        {
            _activeTimelineInteraction = null;
            return;
        }

        ScheduleTimelineInteractionCommit();
    }

    private EventEditorState ReadEditorState()
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            throw new InvalidOperationException("\u0423\u043A\u0430\u0436\u0438\u0442\u0435 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u044F.");
        }

        if (StartDatePicker.SelectedDate is null || EndDatePicker.SelectedDate is null)
        {
            throw new InvalidOperationException("\u0423\u043A\u0430\u0436\u0438\u0442\u0435 \u0434\u0430\u0442\u0443 \u043D\u0430\u0447\u0430\u043B\u0430 \u0438 \u043E\u043A\u043E\u043D\u0447\u0430\u043D\u0438\u044F.");
        }

        if (!TimeSpan.TryParseExact(StartTimeTextBox.Text, "hh\\:mm", CultureInfo.InvariantCulture, out var startTime))
        {
            throw new InvalidOperationException("\u0412\u0440\u0435\u043C\u044F \u043D\u0430\u0447\u0430\u043B\u0430 \u0434\u043E\u043B\u0436\u043D\u043E \u0431\u044B\u0442\u044C \u0432 \u0444\u043E\u0440\u043C\u0430\u0442\u0435 \u0427\u0427:\u041C\u041C.");
        }

        if (!TimeSpan.TryParseExact(EndTimeTextBox.Text, "hh\\:mm", CultureInfo.InvariantCulture, out var endTime))
        {
            throw new InvalidOperationException("\u0412\u0440\u0435\u043C\u044F \u043E\u043A\u043E\u043D\u0447\u0430\u043D\u0438\u044F \u0434\u043E\u043B\u0436\u043D\u043E \u0431\u044B\u0442\u044C \u0432 \u0444\u043E\u0440\u043C\u0430\u0442\u0435 \u0427\u0427:\u041C\u041C.");
        }

        var startLocal = StartDatePicker.SelectedDate.Value.Date.Add(startTime);
        var endLocal = EndDatePicker.SelectedDate.Value.Date.Add(endTime);
        if (endLocal <= startLocal)
        {
            throw new InvalidOperationException("\u041E\u043A\u043E\u043D\u0447\u0430\u043D\u0438\u0435 \u0434\u043E\u043B\u0436\u043D\u043E \u0431\u044B\u0442\u044C \u043F\u043E\u0437\u0436\u0435 \u043D\u0430\u0447\u0430\u043B\u0430.");
        }

        var timezone = TimezoneComboBox.SelectedItem as TimeZoneInfo ?? TimeZoneInfo.Local;

        return new EventEditorState
        {
            Id = _selectedEventId,
            Title = TitleTextBox.Text,
            Description = DescriptionTextBox.Text,
            StartLocal = startLocal,
            EndLocal = endLocal,
            TimezoneId = timezone.Id,
            IsAllDay = AllDayCheckBox.IsChecked ?? false,
            RRuleText = RRuleTextBox.Text
        };
    }

    private void SeedEditorDefaults(DateTime? selectedDateOverride = null)
    {
        var selectedDate = selectedDateOverride ?? MonthCalendar.SelectedDate?.Date ?? DateTime.Today;

        TitleTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        StartDatePicker.SelectedDate = selectedDate;
        EndDatePicker.SelectedDate = selectedDate;
        StartTimeTextBox.Text = "09:00";
        EndTimeTextBox.Text = "10:00";
        TimezoneComboBox.SelectedItem = TimeZoneInfo.Local;
        AllDayCheckBox.IsChecked = false;
        RRuleTextBox.Text = string.Empty;
    }

    private void SelectEvent(Guid eventId)
    {
        var selected = _allEvents.FirstOrDefault(item => item.Id == eventId);
        if (selected is null)
        {
            return;
        }

        SetSelectedDate(selected.StartsAtLocal.Date, prepareQuickCreate: false);
        ApplySelectionToEditor(selected);
        DailyEventsListBox.ScrollIntoView(DailyEventsListBox.SelectedItem);
    }

    private void ApplySelectionToEditor(EventListItemViewModel selected)
    {
        _selectedEventId = selected.Id;
        DeleteButton.IsEnabled = true;

        TitleTextBox.Text = selected.Title;
        DescriptionTextBox.Text = selected.Description;
        StartDatePicker.SelectedDate = selected.StartsAtLocal.Date;
        EndDatePicker.SelectedDate = selected.EndsAtLocal.Date;
        StartTimeTextBox.Text = selected.StartsAtLocal.ToString("HH:mm");
        EndTimeTextBox.Text = selected.EndsAtLocal.ToString("HH:mm");
        AllDayCheckBox.IsChecked = selected.IsAllDay;
        RRuleTextBox.Text = selected.RRuleText;
        TimezoneComboBox.SelectedItem =
            TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(zone => zone.Id == selected.TimezoneId)
            ?? TimeZoneInfo.Local;

        _isSynchronizingSelection = true;
        DailyEventsListBox.SelectedItem = _selectedDayEvents.FirstOrDefault(item => item.Id == selected.Id);
        _isSynchronizingSelection = false;

        if ((MonthCalendar.SelectedDate ?? DateTime.Today).Date != selected.StartsAtLocal.Date)
        {
            SetSelectedDate(selected.StartsAtLocal.Date, prepareQuickCreate: false);
        }

        OpenEditor();
        UpdateEditorModeChrome(isEditing: true, selected.StartsAtLocal.Date, selected.Title);
        StatusTextBlock.Text = $"\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435: \u00AB{selected.Title}\u00BB.";
    }

    private void SetSelectedDate(DateTime date, bool prepareQuickCreate)
    {
        _isSynchronizingSelection = true;
        MiniCalendar.SelectedDate = date;
        MiniCalendar.DisplayDate = date;
        MonthCalendar.SelectedDate = date;
        if (MonthCalendar.DisplayDate.Month != date.Month || MonthCalendar.DisplayDate.Year != date.Year)
        {
            MonthCalendar.DisplayDate = date;
        }

        _isSynchronizingSelection = false;

        RefreshSelectedDayEvents();
        RefreshSelectedWeekDays();
        RefreshCalendarChrome();
        RefreshCalendarMarkers();

        if (prepareQuickCreate)
        {
            PrepareQuickCreateForDate(date, $"\u041D\u043E\u0432\u044B\u0439 \u0447\u0435\u0440\u043D\u043E\u0432\u0438\u043A \u043F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043B\u0435\u043D \u043D\u0430 {date.ToString("d MMMM", RussianCulture)}.");
        }
    }

    private void SetViewMode(CalendarViewMode viewMode)
    {
        _viewMode = viewMode;

        MonthViewContainer.Visibility = viewMode == CalendarViewMode.Month ? Visibility.Visible : Visibility.Collapsed;
        WeekViewContainer.Visibility = viewMode == CalendarViewMode.Week ? Visibility.Visible : Visibility.Collapsed;
        DayViewContainer.Visibility = viewMode == CalendarViewMode.Day ? Visibility.Visible : Visibility.Collapsed;

        RefreshCalendarChrome();
    }

    private void RefreshSelectedDayEvents()
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        _selectedDayEvents.Clear();

        foreach (var item in _allEvents
                     .Where(item => GetDisplayDay(item) == selectedDate)
                     .OrderBy(GetDisplayStart))
        {
            _selectedDayEvents.Add(item);
        }

        _selectedDayTimelineEvents.Clear();
        foreach (var timelineEvent in BuildTimeGridEvents(selectedDate, _selectedDayEvents))
        {
            _selectedDayTimelineEvents.Add(timelineEvent);
        }

        SelectedDateTitleTextBlock.Text = $"\u0421\u043E\u0431\u044B\u0442\u0438\u044F \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}";
        SelectedDateSubtitleTextBlock.Text = _selectedDayEvents.Count == 0
            ? "\u041D\u0430 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u0443\u044E \u0434\u0430\u0442\u0443 \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u043D\u0435\u0442"
            : $"{_selectedDayEvents.Count} \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u00B7 {selectedDate.ToString("dddd", RussianCulture)}";
        QuickCreateSelectedDateButton.Content = _selectedEventId is null
            ? $"\u0421\u043E\u0437\u0434\u0430\u0442\u044C \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}"
            : $"\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435 \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}";

        DayAgendaTitleTextBlock.Text = selectedDate.ToString("dddd, d MMMM", RussianCulture);
        DayAgendaSubtitleTextBlock.Text = _selectedDayTimelineEvents.Count == 0
            ? "\u041D\u0435\u0442 \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438"
            : $"{_selectedDayTimelineEvents.Count} \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u043D\u0430 \u0448\u043A\u0430\u043B\u0435 \u0434\u043D\u044F";
        DayAgendaEmptyTextBlock.Text = _selectedDayEvents.Count == 0
            ? "\u041D\u0430 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0439 \u0434\u0435\u043D\u044C \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u043D\u0435\u0442."
            : "\u0421\u043E\u0431\u044B\u0442\u0438\u044F \u0435\u0441\u0442\u044C, \u043D\u043E \u0434\u043B\u044F \u043F\u043E\u0447\u0430\u0441\u043E\u0432\u043E\u0439 \u0448\u043A\u0430\u043B\u044B \u043F\u043E\u0434\u0445\u043E\u0434\u044F\u0442 \u0442\u043E\u043B\u044C\u043A\u043E \u043D\u0435 all-day.";
        DayAgendaEmptyTextBlock.Visibility = _selectedDayTimelineEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshSelectedWeekDays()
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        var weekStart = GetWeekStart(selectedDate);
        _selectedWeekDays.Clear();

        for (var offset = 0; offset < 7; offset++)
        {
            var day = weekStart.AddDays(offset);
            var dayEvents = _allEvents
                .Where(item => GetDisplayDay(item) == day)
                .OrderBy(GetDisplayStart)
                .ToList();

            _selectedWeekDays.Add(new WeekDayColumnViewModel(
                day,
                dayEvents,
                _timeSlots.ToList(),
                BuildTimeGridEvents(day, dayEvents),
                day == selectedDate));
        }
    }

    private void RefreshCalendarChrome()
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        var weekStart = GetWeekStart(selectedDate);
        var weekEnd = weekStart.AddDays(6);

        switch (_viewMode)
        {
            case CalendarViewMode.Day:
                CurrentRangeTextBlock.Text = selectedDate.ToString("dddd, d MMMM", RussianCulture);
                CurrentRangeSubtitleTextBlock.Text = _selectedDayTimelineEvents.Count == 0
                    ? "\u041F\u043E\u0447\u0430\u0441\u043E\u0432\u0430\u044F \u043F\u043E\u0432\u0435\u0441\u0442\u043A\u0430 \u0434\u043D\u044F \u043F\u0443\u0441\u0442\u0430"
                    : $"{_selectedDayTimelineEvents.Count} \u0441\u043E\u0431\u044B\u0442\u0438\u0439 \u043D\u0430 \u043F\u043E\u0447\u0430\u0441\u043E\u0432\u043E\u0439 \u0448\u043A\u0430\u043B\u0435";
                break;
            case CalendarViewMode.Week:
                CurrentRangeTextBlock.Text = $"{weekStart.ToString("d MMM", RussianCulture)} - {weekEnd.ToString("d MMM yyyy", RussianCulture)}";
                CurrentRangeSubtitleTextBlock.Text = string.Empty;
                break;
            default:
                CurrentRangeTextBlock.Text = MonthCalendar.DisplayDate.ToString("MMMM yyyy", RussianCulture);
                CurrentRangeSubtitleTextBlock.Text = "\u0421\u043E\u0431\u044B\u0442\u0438\u044F \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u043E\u0433\u043E \u0434\u043D\u044F";
                break;

        }
    }

    private void RefreshCalendarMarkers()
    {
        ApplyCalendarMarkers(MiniCalendar, compact: true);
        ApplyCalendarMarkers(MonthCalendar, compact: false);
    }

    private void NavigateRange(int direction)
    {
        var anchorDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        var targetDate = _viewMode switch
        {
            CalendarViewMode.Day => anchorDate.AddDays(direction),
            CalendarViewMode.Week => anchorDate.AddDays(direction * 7),
            _ => anchorDate.AddMonths(direction)
        };

        SetSelectedDate(targetDate, prepareQuickCreate: false);
    }

    private void PrepareQuickCreateForDate(DateTime date, string statusMessage)
    {
        _isSynchronizingSelection = true;
        DailyEventsListBox.SelectedItem = null;
        _isSynchronizingSelection = false;

        _selectedEventId = null;
        DeleteButton.IsEnabled = false;
        SeedEditorDefaults(date);
        OpenEditor();
        UpdateEditorModeChrome(isEditing: false, date, null);
        StatusTextBlock.Text = statusMessage;
    }

    private void ApplyDraftEditorState(DateTime start, DateTime end, string statusMessage)
    {
        SetSelectedDate(start.Date, prepareQuickCreate: false);

        _isSynchronizingSelection = true;
        DailyEventsListBox.SelectedItem = null;
        _isSynchronizingSelection = false;

        _selectedEventId = null;
        DeleteButton.IsEnabled = false;
        SeedEditorDefaults(start.Date);
        StartDatePicker.SelectedDate = start.Date;
        EndDatePicker.SelectedDate = end.Date;
        StartTimeTextBox.Text = start.ToString("HH:mm");
        EndTimeTextBox.Text = end.ToString("HH:mm");
        OpenEditor();
        UpdateEditorModeChrome(isEditing: false, start.Date, null);
        StatusTextBlock.Text = statusMessage;
    }

    private void OpenEditor()
    {
        UpdateEditorVisibility(true);
    }

    private bool TryToggleSelectedEventEditor(Guid eventId)
    {
        if (_selectedEventId != eventId || EditorPanelBorder.Visibility != Visibility.Visible)
        {
            return false;
        }

        CloseEditor();
        StatusTextBlock.Text = "\u0420\u0435\u0434\u0430\u043A\u0442\u043E\u0440 \u0441\u043E\u0431\u044B\u0442\u0438\u044F \u0437\u0430\u043A\u0440\u044B\u0442.";
        return true;
    }

    private void CloseEditor()
    {
        _selectedEventId = null;
        DeleteButton.IsEnabled = false;
        _isSynchronizingSelection = true;
        DailyEventsListBox.SelectedItem = null;
        _isSynchronizingSelection = false;
        SeedEditorDefaults();
        UpdateEditorVisibility(false);
    }

    private void UpdateEditorVisibility(bool isOpen)
    {
        EditorPanelBorder.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        EditorSpacerColumn.Width = isOpen ? new GridLength(18d) : new GridLength(0d);
        EditorColumn.Width = isOpen ? new GridLength(360d) : new GridLength(0d);
    }

    private void UpdateEditorModeChrome(bool isEditing, DateTime selectedDate, string? eventTitle)
    {
        if (isEditing)
        {
            EditorModeTitleTextBlock.Text = "\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u044F";
            EditorModeSubtitleTextBlock.Text = string.IsNullOrWhiteSpace(eventTitle)
                ? selectedDate.ToString("d MMMM", RussianCulture)
                : $"\u00AB{eventTitle}\u00BB, {selectedDate.ToString("d MMMM", RussianCulture)}";
            QuickCreateSelectedDateButton.Content = $"\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435 \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}";
            return;
        }

        EditorModeTitleTextBlock.Text = "\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435";
        EditorModeSubtitleTextBlock.Text = selectedDate.ToString("d MMMM", RussianCulture);
        QuickCreateSelectedDateButton.Content = $"\u0421\u043E\u0437\u0434\u0430\u0442\u044C \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}";
    }

    private void ApplyCalendarMarkers(System.Windows.Controls.Calendar calendar, bool compact)
    {
        if (!calendar.IsLoaded)
        {
            return;
        }

        var eventsByDate = _allEvents
            .GroupBy(item => item.Day)
            .ToDictionary(group => group.Key, group => group.ToList());
        var selectedDate = calendar.SelectedDate?.Date;

        calendar.Dispatcher.BeginInvoke(() =>
        {
            foreach (var button in FindVisualChildren<CalendarDayButton>(calendar))
            {
                button.ClearValue(BackgroundProperty);
                button.ClearValue(BorderBrushProperty);
                button.ClearValue(BorderThicknessProperty);
                button.ClearValue(FontWeightProperty);
                button.ClearValue(ToolTipProperty);

                if (button.DataContext is not DateTime date || !eventsByDate.TryGetValue(date.Date, out var dayEvents))
                {
                    continue;
                }

                var accentBrush = dayEvents[0].AccentBrush as SolidColorBrush ?? MarkerFallbackBrush;
                var accentColor = accentBrush.Color;
                var backgroundAlpha = compact ? (byte)0x10 : (byte)0x1A;
                var borderAlpha = compact ? (byte)0x50 : (byte)0x70;

                button.Background = CreateFrozenBrush(Color.FromArgb(backgroundAlpha, accentColor.R, accentColor.G, accentColor.B));
                button.BorderBrush = CreateFrozenBrush(Color.FromArgb(borderAlpha, accentColor.R, accentColor.G, accentColor.B));
                button.BorderThickness = selectedDate == date.Date ? new Thickness(2) : new Thickness(1.5);
                button.FontWeight = FontWeights.SemiBold;
                button.ToolTip = dayEvents.Count == 1
                    ? $"1 \u0441\u043E\u0431\u044B\u0442\u0438\u0435: {dayEvents[0].Title}"
                    : $"{dayEvents.Count} \u0441\u043E\u0431\u044B\u0442\u0438\u044F";
            }
        }, DispatcherPriority.Loaded);
    }

    private IReadOnlyList<TimeGridEventViewModel> BuildTimeGridEvents(DateTime day, IEnumerable<EventListItemViewModel> dayEvents)
    {
        var results = new List<TimeGridEventViewModel>();

        foreach (var item in dayEvents.Where(x => !x.IsAllDay))
        {
            var displayStart = GetDisplayStart(item);
            var displayEnd = GetDisplayEnd(item);
            var (clampedStart, clampedEnd, top, height) = CalculateTimelineBounds(day, displayStart, displayEnd);

            if (clampedEnd <= clampedStart)
            {
                continue;
            }

            results.Add(new TimeGridEventViewModel(item, day, clampedStart, clampedEnd, top, height));
        }

        return results.OrderBy(x => x.Top).ToList();
    }

    private void BeginTimelineInteraction(object sender, MouseButtonEventArgs e, TimelineInteractionMode mode)
    {
        if (!TryGetTimelineEvent(sender, out var timeGridEvent))
        {
            return;
        }

        if (_allEvents.FirstOrDefault(item => item.Id == timeGridEvent.Id) is not { } selected)
        {
            return;
        }

        _activeTimelineInteraction = new TimelineInteractionState(
            selected.Id,
            timeGridEvent.Day,
            selected.StartsAtLocal,
            selected.EndsAtLocal,
            mode);

        if (mode == TimelineInteractionMode.Move && sender is FrameworkElement element)
        {
            _activeTimelineInteraction.PointerOffsetMinutes = ResolveTimelinePointerOffsetMinutes(timeGridEvent, e.GetPosition(element));
        }

        if (ShouldUseWeekDragPreview(_activeTimelineInteraction))
        {
            SetWeekTimelineEventOpacity(selected.Id, 0d);
        }

        UpdateTimelinePreview(_activeTimelineInteraction, dayChanged: false);
    }

    private bool TryHandleTimelineEventClick()
    {
        if (_activeTimelineInteraction is not { Mode: TimelineInteractionMode.Move, EventId: Guid eventId } interaction)
        {
            return false;
        }

        if (!IsSameTimelineMoment(interaction.OriginalStart, interaction.PreviewStart) ||
            !IsSameTimelineMoment(interaction.OriginalEnd, interaction.PreviewEnd))
        {
            return false;
        }

        if (TryToggleSelectedEventEditor(eventId))
        {
            return true;
        }

        if (_allEvents.FirstOrDefault(item => item.Id == eventId) is not { } selected)
        {
            return false;
        }

        ApplySelectionToEditor(selected);
        return true;
    }

    private void UpdateTimelineInteraction(double verticalChange, Point localPointerPosition)
    {
        if (_activeTimelineInteraction is null)
        {
            return;
        }

        var previousPreviewDay = _activeTimelineInteraction.PreviewStart.Date;
        _activeTimelineInteraction.AccumulatedVerticalDelta += verticalChange;
        var minuteDelta = SnapMinutes(_activeTimelineInteraction.AccumulatedVerticalDelta / HourSlotHeight * 60d);
        var targetDay = ResolveTimelineInteractionDay(_activeTimelineInteraction);
        var dayStart = targetDay.Date;
        var dayEnd = dayStart.AddDays(1);
        var duration = _activeTimelineInteraction.OriginalEnd - _activeTimelineInteraction.OriginalStart;

        switch (_activeTimelineInteraction.Mode)
        {
            case TimelineInteractionMode.Move:
                var shiftedStart = ResolveTimelineMoveStart(_activeTimelineInteraction, targetDay);
                var shiftedEnd = shiftedStart.Add(duration);

                if (shiftedStart < dayStart)
                {
                    shiftedStart = dayStart;
                    shiftedEnd = shiftedStart.Add(duration);
                }

                if (shiftedEnd > dayEnd)
                {
                    shiftedEnd = dayEnd;
                    shiftedStart = shiftedEnd - duration;
                    if (shiftedStart < dayStart)
                    {
                        shiftedStart = dayStart;
                    }
                }

                _activeTimelineInteraction.PreviewStart = shiftedStart;
                _activeTimelineInteraction.PreviewEnd = shiftedEnd;
                StatusTextBlock.Text = $"\u041F\u0435\u0440\u0435\u043D\u043E\u0441: {shiftedStart:ddd, d MMM HH:mm} - {shiftedEnd:HH:mm}.";
                break;

            case TimelineInteractionMode.ResizeEnd:
                var resizedEnd = _activeTimelineInteraction.OriginalEnd.AddMinutes(minuteDelta);
                var minimumEnd = _activeTimelineInteraction.OriginalStart.AddMinutes(MinimumDurationMinutes);
                if (resizedEnd < minimumEnd)
                {
                    resizedEnd = minimumEnd;
                }

                if (resizedEnd > dayEnd)
                {
                    resizedEnd = dayEnd;
                }

                _activeTimelineInteraction.PreviewStart = _activeTimelineInteraction.OriginalStart;
                _activeTimelineInteraction.PreviewEnd = resizedEnd;
                StatusTextBlock.Text = $"\u041D\u043E\u0432\u0430\u044F \u0434\u043B\u0438\u0442\u0435\u043B\u044C\u043D\u043E\u0441\u0442\u044C: {_activeTimelineInteraction.PreviewStart:HH:mm} - {resizedEnd:HH:mm}.";
                break;

            case TimelineInteractionMode.Create:
                var (draftStart, draftEnd) = ResolveTimelineDraftRange(_activeTimelineInteraction, localPointerPosition);
                _activeTimelineInteraction.PreviewStart = draftStart;
                _activeTimelineInteraction.PreviewEnd = draftEnd;
                StatusTextBlock.Text = $"\u0427\u0435\u0440\u043D\u043E\u0432\u0438\u043A \u043D\u043E\u0432\u043E\u0433\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u044F: {draftStart:HH:mm} - {draftEnd:HH:mm}.";
                break;
        }

        var dayChanged = previousPreviewDay != _activeTimelineInteraction.PreviewStart.Date;
        UpdateTimelinePreview(_activeTimelineInteraction, dayChanged);
        RefreshCalendarChrome();
    }

    private void CommitTimelineInteraction()
    {
        if (_activeTimelineInteraction is null)
        {
            return;
        }

        var interaction = _activeTimelineInteraction;
        _activeTimelineInteraction = null;

        if (interaction.Mode == TimelineInteractionMode.Create)
        {
            try
            {
                var timezone = TimezoneComboBox.SelectedItem as TimeZoneInfo ?? TimeZoneInfo.Local;
                var created = _eventRepository.Save(new EventEditorState
                {
                    Id = null,
                    Title = NewEventDefaultTitle,
                    Description = string.Empty,
                    StartLocal = interaction.PreviewStart,
                    EndLocal = interaction.PreviewEnd,
                    TimezoneId = timezone.Id,
                    IsAllDay = false,
                    RRuleText = string.Empty
                });

                LoadEvents();
                SelectEvent(created.Id);
                StatusTextBlock.Text = $"\u0421\u043E\u0431\u044B\u0442\u0438\u0435 \u0441\u043E\u0437\u0434\u0430\u043D\u043E \u043D\u0430 {interaction.PreviewStart:HH:mm}.";
            }
            catch (Exception exception)
            {
                ApplyDraftEditorState(
                    interaction.PreviewStart,
                    interaction.PreviewEnd,
                    exception.Message);
            }

            return;
        }

        if (_allEvents.FirstOrDefault(item => item.Id == interaction.EventId) is not { } existing)
        {
            ScheduleTimelineReload();
            return;
        }

        try
        {
            var updated = _eventRepository.Save(new EventEditorState
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                StartLocal = interaction.PreviewStart,
                EndLocal = interaction.PreviewEnd,
                TimezoneId = existing.TimezoneId,
                IsAllDay = existing.IsAllDay,
                RRuleText = existing.RRuleText
            });

            ScheduleTimelineReload();
            StatusTextBlock.Text = interaction.Mode == TimelineInteractionMode.Move
                ? $"\u0421\u043E\u0431\u044B\u0442\u0438\u0435 \u00AB{updated.Title}\u00BB \u043F\u0435\u0440\u0435\u043D\u0435\u0441\u0435\u043D\u043E \u043D\u0430 {interaction.PreviewStart:HH:mm}."
                : $"\u0414\u043B\u0438\u0442\u0435\u043B\u044C\u043D\u043E\u0441\u0442\u044C \u0441\u043E\u0431\u044B\u0442\u0438\u044F \u00AB{updated.Title}\u00BB \u0438\u0437\u043C\u0435\u043D\u0435\u043D\u0430 \u0434\u043E {interaction.PreviewEnd:HH:mm}.";
        }
        catch (Exception exception)
        {
            ScheduleTimelineReload();
            StatusTextBlock.Text = exception.Message;
        }
    }

    private void ScheduleTimelineInteractionCommit()
    {
        if (_activeTimelineInteraction is null || _isTimelineCommitScheduled)
        {
            return;
        }

        _isTimelineCommitScheduled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _isTimelineCommitScheduled = false;
            CommitTimelineInteraction();
        }), DispatcherPriority.Background);
    }

    private void ScheduleTimelineReload()
    {
        if (_isTimelineReloadScheduled)
        {
            return;
        }

        _isTimelineReloadScheduled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _isTimelineReloadScheduled = false;
            LoadEvents();
        }), DispatcherPriority.ApplicationIdle);
    }

    private static bool TryGetTimelineEvent(object sender, out TimeGridEventViewModel timeGridEvent)
    {
        if (sender is FrameworkElement element && element.DataContext is TimeGridEventViewModel directEvent)
        {
            timeGridEvent = directEvent;
            return true;
        }

        var dependencyObject = sender as DependencyObject;
        while (dependencyObject is not null)
        {
            if (dependencyObject is FrameworkElement frameworkElement &&
                frameworkElement.DataContext is TimeGridEventViewModel nestedEvent)
            {
                timeGridEvent = nestedEvent;
                return true;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        timeGridEvent = null!;
        return false;
    }

    private static bool IsTimelineResizeHandleOrigin(DependencyObject? interactionSurface, DependencyObject? originalSource)
    {
        var current = originalSource;
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: "TimelineResizeHandle" })
            {
                return true;
            }

            if (interactionSurface is not null && ReferenceEquals(current, interactionSurface))
            {
                break;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void UpdateTimelinePreview(TimelineInteractionState interaction, bool dayChanged)
    {
        if (interaction.Mode == TimelineInteractionMode.Create)
        {
            ShowDraftTimelinePreview(interaction);
            return;
        }

        if (ShouldUseWeekDragPreview(interaction))
        {
            ShowWeekDragPreview(interaction);
            return;
        }

        if (dayChanged)
        {
            RefreshSelectedDayEvents();
            RefreshSelectedWeekDays();
            return;
        }
        if (interaction.EventId is Guid eventId)
        {
            foreach (var timelineEvent in EnumerateVisibleTimelineEvents(eventId, interaction.PreviewStart.Date))
            {
                var (clampedStart, clampedEnd, top, height) =
                    CalculateTimelineBounds(interaction.PreviewStart.Date, interaction.PreviewStart, interaction.PreviewEnd);
                timelineEvent.UpdateLayout(clampedStart, clampedEnd, top, height);
            }
        }

        DayAgendaEmptyTextBlock.Visibility = _selectedDayTimelineEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool ShouldUseWeekDragPreview(TimelineInteractionState interaction)
    {
        return _viewMode == CalendarViewMode.Week && interaction.Mode == TimelineInteractionMode.Move;
    }

    private void ShowDraftTimelinePreview(TimelineInteractionState interaction)
    {
        if (_viewMode == CalendarViewMode.Week)
        {
            ShowWeekDraftPreview(interaction);
            HideDayDragPreview();
            return;
        }

        ShowDayDraftPreview(interaction);
        HideWeekDragPreview();
    }

    private void ShowWeekDragPreview(TimelineInteractionState interaction)
    {
        const double timelineCardHorizontalMargin = 6d;

        if (_allEvents.FirstOrDefault(item => item.Id == interaction.EventId) is not { } eventItem)
        {
            return;
        }

        if (!TryGetWeekDayColumnBounds(interaction.PreviewStart.Date, out var columnLeft, out var columnWidth))
        {
            return;
        }
        var (clampedStart, clampedEnd, top, height) =
            CalculateTimelineBounds(interaction.PreviewStart.Date, interaction.PreviewStart, interaction.PreviewEnd);

        WeekDragPreviewCanvas.Visibility = Visibility.Visible;
        WeekDragPreviewCanvas.Width = WeekBodiesItemsControl.ActualWidth;
        WeekDragPreviewCanvas.Height = 1536d;

        WeekDragPreviewCard.Width = GetTimelinePreviewWidth(columnWidth - (timelineCardHorizontalMargin * 2d));
        WeekDragPreviewCard.Height = height;
        WeekDragPreviewCard.Background = eventItem.AccentSurfaceBrush;
        WeekDragPreviewCard.BorderBrush = eventItem.AccentBorderBrush;
        WeekDragPreviewTitleTextBlock.Text = eventItem.Title;
        WeekDragPreviewTimeTextBlock.Text =
            $"{clampedStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {clampedEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        WeekDragPreviewTimeTextBlock.Foreground = eventItem.AccentBrush;
        WeekDragPreviewDescriptionTextBlock.Text = eventItem.Description;

        Canvas.SetLeft(
            WeekDragPreviewCard,
            ClampPreviewLeft(columnLeft + timelineCardHorizontalMargin, WeekDragPreviewCard.Width, WeekDragPreviewCanvas.Width));
        Canvas.SetTop(WeekDragPreviewCard, top);
    }

    private void HideWeekDragPreview()
    {
        WeekDragPreviewCanvas.Visibility = Visibility.Collapsed;
    }

    private void HideDayDragPreview()
    {
        DayDragPreviewCanvas.Visibility = Visibility.Collapsed;
    }

    private void SetWeekTimelineEventOpacity(Guid eventId, double opacity)
    {
        foreach (var presenter in FindVisualChildren<ContentPresenter>(WeekBodiesItemsControl))
        {
            if (presenter.DataContext is TimeGridEventViewModel timelineEvent && timelineEvent.Id == eventId)
            {
                presenter.Opacity = opacity;
            }
        }
    }

    private bool TryGetWeekDayColumnBounds(DateTime day, out double left, out double width)
    {
        foreach (var presenter in FindVisualChildren<ContentPresenter>(WeekBodiesItemsControl))
        {
            if (presenter.DataContext is not WeekDayColumnViewModel column || column.Date != day.Date)
            {
                continue;
            }

            var border = FindVisualChildren<Border>(presenter).FirstOrDefault();
            var targetElement = (FrameworkElement?)border ?? presenter;
            if (targetElement.ActualWidth <= 0d)
            {
                continue;
            }

            var point = targetElement.TranslatePoint(new Point(0d, 0d), WeekDragPreviewCanvas);
            left = point.X;
            width = targetElement.ActualWidth;
            return true;
        }

        left = 0d;
        width = 0d;
        return false;
    }

    private IEnumerable<TimeGridEventViewModel> EnumerateVisibleTimelineEvents(Guid eventId, DateTime day)
    {
        foreach (var timelineEvent in _selectedDayTimelineEvents.Where(item => item.Id == eventId && item.Day == day.Date))
        {
            yield return timelineEvent;
        }

        foreach (var timelineEvent in _selectedWeekDays
                     .SelectMany(column => column.TimeGridEvents)
                     .Where(item => item.Id == eventId && item.Day == day.Date))
        {
            yield return timelineEvent;
        }
    }

    private DateTime ResolveTimelineInteractionDay(TimelineInteractionState interaction)
    {
        if (_viewMode != CalendarViewMode.Week || interaction.Mode != TimelineInteractionMode.Move || WeekBodiesItemsControl.ActualWidth <= 0d)
        {
            return interaction.Day;
        }

        var pointer = Mouse.GetPosition(WeekBodiesItemsControl);
        var columnCount = _selectedWeekDays.Count;
        if (columnCount <= 0)
        {
            return interaction.Day;
        }

        var normalizedX = Math.Max(0d, Math.Min(pointer.X, WeekBodiesItemsControl.ActualWidth - 1d));
        var columnWidth = WeekBodiesItemsControl.ActualWidth / columnCount;
        if (columnWidth <= 0d)
        {
            return interaction.Day;
        }

        var columnIndex = Math.Clamp((int)(normalizedX / columnWidth), 0, columnCount - 1);
        return _selectedWeekDays[columnIndex].Date;
    }

    private DateTime ResolveTimelineMoveStart(TimelineInteractionState interaction, DateTime targetDay)
    {
        var pointer = _viewMode == CalendarViewMode.Week
            ? Mouse.GetPosition(WeekBodiesItemsControl)
            : Mouse.GetPosition(DayTimelineItemsControl);
        var rawMinutes = (pointer.Y / HourSlotHeight * 60d) - interaction.PointerOffsetMinutes;
        var durationMinutes = Math.Max(MinimumDurationMinutes, (interaction.OriginalEnd - interaction.OriginalStart).TotalMinutes);
        var maxStartMinutes = Math.Max(0d, MinutesPerDay - durationMinutes);
        var snappedMinutes = SnapMinutes(rawMinutes);
        var clampedMinutes = Math.Clamp(snappedMinutes, 0, (int)Math.Floor(maxStartMinutes));
        return targetDay.Date.AddMinutes(clampedMinutes);
    }

    private static double ResolveTimelinePointerOffsetMinutes(TimeGridEventViewModel timeGridEvent, Point localPosition)
    {
        if (timeGridEvent.Height <= 0d)
        {
            return 0d;
        }

        var durationMinutes = Math.Max(MinimumDurationMinutes, (timeGridEvent.DisplayEnd - timeGridEvent.DisplayStart).TotalMinutes);
        var normalizedOffset = Math.Clamp(localPosition.Y, 0d, timeGridEvent.Height) / timeGridEvent.Height;
        return durationMinutes * normalizedOffset;
    }

    private (DateTime start, DateTime end) ResolveTimelineDraftRange(TimelineInteractionState interaction, Point localPointerPosition)
    {
        var pointerMinutes = SnapMinutes(localPointerPosition.Y / HourSlotHeight * 60d);
        var originMinutes = (int)(interaction.OriginalStart - interaction.OriginalStart.Date).TotalMinutes;
        var clampedPointerMinutes = Math.Clamp(pointerMinutes, 0, MinutesPerDay);

        if (clampedPointerMinutes >= originMinutes)
        {
            var endMinutes = Math.Min(MinutesPerDay, Math.Max(originMinutes + MinimumDurationMinutes, clampedPointerMinutes));
            return (
                interaction.OriginalStart.Date.AddMinutes(originMinutes),
                interaction.OriginalStart.Date.AddMinutes(endMinutes));
        }

        var startMinutes = Math.Max(0, Math.Min(originMinutes - MinimumDurationMinutes, clampedPointerMinutes));
        return (
            interaction.OriginalStart.Date.AddMinutes(startMinutes),
            interaction.OriginalStart.Date.AddMinutes(originMinutes));
    }

    private DateTime ResolveTimelineDraftStart(FrameworkElement element, Point localPosition)
    {
        var day = element.DataContext is WeekDayColumnViewModel column
            ? column.Date
            : MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        var minutes = SnapMinutes(localPosition.Y / HourSlotHeight * 60d);
        var maxStartMinutes = MinutesPerDay - MinimumDurationMinutes;
        var clampedMinutes = Math.Clamp(minutes, 0, maxStartMinutes);
        return day.Date.AddMinutes(clampedMinutes);
    }

    private void ShowWeekDraftPreview(TimelineInteractionState interaction)
    {
        const double timelineCardHorizontalMargin = 6d;

        if (!TryGetWeekDayColumnBounds(interaction.PreviewStart.Date, out var columnLeft, out var columnWidth))
        {
            return;
        }

        EnsureDraftPreviewBrushes();
        var (clampedStart, clampedEnd, top, height) =
            CalculateTimelineBounds(interaction.PreviewStart.Date, interaction.PreviewStart, interaction.PreviewEnd);

        WeekDragPreviewCanvas.Visibility = Visibility.Visible;
        WeekDragPreviewCanvas.Width = WeekBodiesItemsControl.ActualWidth;
        WeekDragPreviewCanvas.Height = 1536d;

        WeekDragPreviewCard.Width = GetTimelinePreviewWidth(columnWidth - (timelineCardHorizontalMargin * 2d));
        WeekDragPreviewCard.Height = height;
        WeekDragPreviewCard.Background = _draftPreviewSurfaceBrush;
        WeekDragPreviewCard.BorderBrush = _draftPreviewBorderBrush;
        WeekDragPreviewTitleTextBlock.Text = "\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435";
        WeekDragPreviewTimeTextBlock.Text =
            $"{clampedStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {clampedEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        WeekDragPreviewTimeTextBlock.Foreground = _draftPreviewAccentBrush;
        WeekDragPreviewDescriptionTextBlock.Text = "\u041E\u0442\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u043C\u044B\u0448\u044C, \u0447\u0442\u043E\u0431\u044B \u0437\u0430\u043F\u043E\u043B\u043D\u0438\u0442\u044C \u0447\u0435\u0440\u043D\u043E\u0432\u0438\u043A \u0441\u043F\u0440\u0430\u0432\u0430.";

        Canvas.SetLeft(
            WeekDragPreviewCard,
            ClampPreviewLeft(columnLeft + timelineCardHorizontalMargin, WeekDragPreviewCard.Width, WeekDragPreviewCanvas.Width));
        Canvas.SetTop(WeekDragPreviewCard, top);
    }

    private void ShowDayDraftPreview(TimelineInteractionState interaction)
    {
        const double timelineCardHorizontalMargin = 6d;

        EnsureDraftPreviewBrushes();
        var (clampedStart, clampedEnd, top, height) =
            CalculateTimelineBounds(interaction.PreviewStart.Date, interaction.PreviewStart, interaction.PreviewEnd);

        DayDragPreviewCanvas.Visibility = Visibility.Visible;
        DayDragPreviewCanvas.Width = DayTimelineItemsControl.ActualWidth;
        DayDragPreviewCanvas.Height = 1536d;

        DayDragPreviewCard.Width = GetTimelinePreviewWidth(DayTimelineItemsControl.ActualWidth - (timelineCardHorizontalMargin * 2d));
        DayDragPreviewCard.Height = height;
        DayDragPreviewCard.Background = _draftPreviewSurfaceBrush;
        DayDragPreviewCard.BorderBrush = _draftPreviewBorderBrush;
        DayDragPreviewTitleTextBlock.Text = "\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435";
        DayDragPreviewTimeTextBlock.Text =
            $"{clampedStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {clampedEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        DayDragPreviewTimeTextBlock.Foreground = _draftPreviewAccentBrush;
        DayDragPreviewDescriptionTextBlock.Text = "\u041E\u0442\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u043C\u044B\u0448\u044C, \u0447\u0442\u043E\u0431\u044B \u0437\u0430\u043F\u043E\u043B\u043D\u0438\u0442\u044C \u0447\u0435\u0440\u043D\u043E\u0432\u0438\u043A \u0441\u043F\u0440\u0430\u0432\u0430.";

        Canvas.SetLeft(
            DayDragPreviewCard,
            ClampPreviewLeft(timelineCardHorizontalMargin, DayDragPreviewCard.Width, DayDragPreviewCanvas.Width));
        Canvas.SetTop(DayDragPreviewCard, top);
    }

    private void EnsureDraftPreviewBrushes()
    {
        _draftPreviewAccentBrush ??= FindResource("AccentBrush") as Brush ?? MarkerFallbackBrush;
        _draftPreviewSurfaceBrush ??= FindResource("AccentSoftBrush") as Brush ?? CreateFrozenBrush(Color.FromArgb(0x33, 0x2D, 0x6C, 0xDF));
        _draftPreviewBorderBrush ??= FindResource("AccentBrush") as Brush ?? MarkerFallbackBrush;
    }

    private DateTime GetDisplayDay(EventListItemViewModel item)
    {
        return TryGetPreviewInteraction(item.Id, out var interaction)
            ? interaction.PreviewStart.Date
            : item.Day;
    }

    private DateTime GetDisplayStart(EventListItemViewModel item)
    {
        return TryGetPreviewInteraction(item.Id, out var interaction)
            ? interaction.PreviewStart
            : item.StartsAtLocal;
    }

    private DateTime GetDisplayEnd(EventListItemViewModel item)
    {
        return TryGetPreviewInteraction(item.Id, out var interaction)
            ? interaction.PreviewEnd
            : item.EndsAtLocal;
    }

    private bool TryGetPreviewInteraction(Guid eventId, out TimelineInteractionState interaction)
    {
        if (_activeTimelineInteraction is { EventId: Guid activeEventId } activeInteraction && activeEventId == eventId)
        {
            interaction = activeInteraction;
            return true;
        }

        interaction = null!;
        return false;
    }

    private static bool IsSameTimelineMoment(DateTime left, DateTime right)
    {
        return left == right;
    }

    private static double GetTimelinePreviewWidth(double availableWidth)
    {
        return Math.Max(0d, availableWidth);
    }

    private static double ClampPreviewLeft(double requestedLeft, double previewWidth, double canvasWidth)
    {
        if (canvasWidth <= 0d)
        {
            return 0d;
        }

        var maxLeft = Math.Max(0d, canvasWidth - Math.Max(0d, previewWidth));
        return Math.Clamp(requestedLeft, 0d, maxLeft);
    }

    private static (DateTime clampedStart, DateTime clampedEnd, double top, double height) CalculateTimelineBounds(
        DateTime day,
        DateTime start,
        DateTime end)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        var clampedStart = start < dayStart ? dayStart : start;
        var clampedEnd = end > dayEnd ? dayEnd : end;
        var minutesFromMidnight = Math.Max(0d, (clampedStart - dayStart).TotalMinutes);
        var durationMinutes = Math.Max(MinimumDurationMinutes, (clampedEnd - clampedStart).TotalMinutes);
        var top = minutesFromMidnight / 60d * HourSlotHeight;
        var height = Math.Max(28d, durationMinutes / 60d * HourSlotHeight);

        return (clampedStart, clampedEnd, top, height);
    }

    private static int SnapMinutes(double minutes)
    {
        return (int)(Math.Round(minutes / MinutesStep, MidpointRounding.AwayFromZero) * MinutesStep);
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dependencyObject)
        where T : DependencyObject
    {
        if (dependencyObject is null)
        {
            yield break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(dependencyObject); index++)
        {
            var child = VisualTreeHelper.GetChild(dependencyObject, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var nestedChild in FindVisualChildren<T>(child))
            {
                yield return nestedChild;
            }
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private enum CalendarViewMode
    {
        Day,
        Week,
        Month
    }

    private enum TimelineInteractionMode
    {
        Move,
        ResizeEnd,
        Create
    }

    private sealed class TimelineInteractionState
    {
        public TimelineInteractionState(Guid? eventId, DateTime day, DateTime originalStart, DateTime originalEnd, TimelineInteractionMode mode)
        {
            EventId = eventId;
            Day = day.Date;
            OriginalStart = originalStart;
            OriginalEnd = originalEnd;
            PreviewStart = originalStart;
            PreviewEnd = originalEnd;
            Mode = mode;
        }

        public Guid? EventId { get; }

        public DateTime Day { get; }

        public DateTime OriginalStart { get; }

        public DateTime OriginalEnd { get; }

        public DateTime PreviewStart { get; set; }

        public DateTime PreviewEnd { get; set; }

        public TimelineInteractionMode Mode { get; }

        public double AccumulatedVerticalDelta { get; set; }

        public double PointerOffsetMinutes { get; set; }
    }
}
