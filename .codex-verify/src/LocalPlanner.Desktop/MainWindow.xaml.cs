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
            ?? throw new InvalidOperationException("Хранилище событий не инициализировано.");

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
        SetSelectedDate(DateTime.Today, prepareQuickCreate: true);
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
            ? "Календарь пуст. Создайте первое событие."
            : $"Всего событий: {_allEvents.Count}.";
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

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var editorState = ReadEditorState();
            var saved = _eventRepository.Save(editorState);
            LoadEvents();
            SelectEvent(saved.Id);
            StatusTextBlock.Text = $"Событие «{saved.Title}» сохранено.";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = exception.Message;
        }
    }

    private void NewButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        PrepareQuickCreateForDate(selectedDate, "Заполните форму нового события.");
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedEventId is null)
        {
            StatusTextBlock.Text = "Сначала выберите событие.";
            return;
        }

        if (_eventRepository.SoftDelete(_selectedEventId.Value))
        {
            NewButton_OnClick(sender, e);
            LoadEvents();
            StatusTextBlock.Text = "Событие удалено.";
        }
        else
        {
            StatusTextBlock.Text = "Не удалось удалить событие.";
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

        SetSelectedDate(MiniCalendar.SelectedDate ?? DateTime.Today, prepareQuickCreate: true);
    }

    private void MonthCalendar_OnSelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        SetSelectedDate(MonthCalendar.SelectedDate ?? DateTime.Today, prepareQuickCreate: true);
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

        SetSelectedDate(date, prepareQuickCreate: true);
        SetViewMode(CalendarViewMode.Day);
        DayViewRadioButton.IsChecked = true;
    }

    private void TodayButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetSelectedDate(DateTime.Today, prepareQuickCreate: true);
    }

    private void QuickCreateSelectedDateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        PrepareQuickCreateForDate(selectedDate, $"Подготовлено новое событие на {selectedDate.ToString("d MMMM", RussianCulture)}.");
    }

    private void TimelineEventThumb_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryGetTimelineEvent(sender, out var timeGridEvent) &&
            _allEvents.FirstOrDefault(item => item.Id == timeGridEvent.Id) is { } selected)
        {
            ApplySelectionToEditor(selected);
        }
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
        if (_activeTimelineInteraction is null || _activeTimelineCaptureElement is not UIElement captureElement)
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
            UpdateTimelineInteraction(verticalChange);
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
        BeginTimelineInteraction(sender, mode);

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
        SeedEditorDefaults(start.Date);
        StartDatePicker.SelectedDate = start.Date;
        EndDatePicker.SelectedDate = end.Date;
        StartTimeTextBox.Text = start.ToString("HH:mm");
        EndTimeTextBox.Text = end.ToString("HH:mm");
        UpdateEditorModeChrome(isEditing: false, start.Date, null);
        StatusTextBlock.Text = $"Черновик нового события: {start:HH:mm} - {end:HH:mm}.";
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
        SetWeekTimelineEventOpacity(_activeTimelineInteraction.EventId, 1d);
        _activeTimelineCaptureElement = null;
        _lastTimelinePointerPosition = null;

        if (releaseCapture && capturedElement?.IsMouseCaptured == true)
        {
            capturedElement.ReleaseMouseCapture();
        }

        HideWeekDragPreview();
        HideDayDragPreview();
        ScheduleTimelineInteractionCommit();
    }

    private EventEditorState ReadEditorState()
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            throw new InvalidOperationException("Укажите название события.");
        }

        if (StartDatePicker.SelectedDate is null || EndDatePicker.SelectedDate is null)
        {
            throw new InvalidOperationException("Укажите дату начала и окончания.");
        }

        if (!TimeSpan.TryParseExact(StartTimeTextBox.Text, "hh\\:mm", CultureInfo.InvariantCulture, out var startTime))
        {
            throw new InvalidOperationException("Время начала должно быть в формате ЧЧ:ММ.");
        }

        if (!TimeSpan.TryParseExact(EndTimeTextBox.Text, "hh\\:mm", CultureInfo.InvariantCulture, out var endTime))
        {
            throw new InvalidOperationException("Время окончания должно быть в формате ЧЧ:ММ.");
        }

        var startLocal = StartDatePicker.SelectedDate.Value.Date.Add(startTime);
        var endLocal = EndDatePicker.SelectedDate.Value.Date.Add(endTime);
        if (endLocal <= startLocal)
        {
            throw new InvalidOperationException("Окончание должно быть позже начала.");
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

        UpdateEditorModeChrome(isEditing: true, selected.StartsAtLocal.Date, selected.Title);
        StatusTextBlock.Text = $"Редактирование: «{selected.Title}».";
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
            PrepareQuickCreateForDate(date, $"Новый черновик подготовлен на {date.ToString("d MMMM", RussianCulture)}.");
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

        SelectedDateTitleTextBlock.Text = $"События на {selectedDate.ToString("d MMMM", RussianCulture)}";
        SelectedDateSubtitleTextBlock.Text = _selectedDayEvents.Count == 0
            ? "На выбранную дату событий нет"
            : $"{_selectedDayEvents.Count} событ. · {selectedDate.ToString("dddd", RussianCulture)}";
        QuickCreateSelectedDateButton.Content = _selectedEventId is null
            ? $"Создать на {selectedDate.ToString("d MMMM", RussianCulture)}"
            : $"Новое событие на {selectedDate.ToString("d MMMM", RussianCulture)}";

        DayAgendaTitleTextBlock.Text = selectedDate.ToString("dddd, d MMMM", RussianCulture);
        DayAgendaSubtitleTextBlock.Text = _selectedDayTimelineEvents.Count == 0
            ? "Нет событий по времени"
            : $"{_selectedDayTimelineEvents.Count} событ. на шкале дня";
        DayAgendaEmptyTextBlock.Text = _selectedDayEvents.Count == 0
            ? "На выбранный день событий нет."
            : "События есть, но для почасовой шкалы подходят только обычные, не all-day.";
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
                    ? "Почасовая повестка дня пуста"
                    : $"{_selectedDayTimelineEvents.Count} событ. на почасовой шкале";
                break;
            case CalendarViewMode.Week:
                CurrentRangeTextBlock.Text = $"{weekStart.ToString("d MMM", RussianCulture)} - {weekEnd.ToString("d MMM yyyy", RussianCulture)}";
                CurrentRangeSubtitleTextBlock.Text = "Неделя с переносом событий по времени и изменением длительности";
                break;
            default:
                CurrentRangeTextBlock.Text = MonthCalendar.DisplayDate.ToString("MMMM yyyy", RussianCulture);
                CurrentRangeSubtitleTextBlock.Text = "Обзор месяца и событий выбранного дня";
                break;

            case TimelineInteractionMode.Create:
                var previewEnd = _activeTimelineInteraction.OriginalStart.AddMinutes(Math.Max(MinimumDurationMinutes, minuteDelta));
                if (previewEnd > dayEnd)
                {
                    previewEnd = dayEnd;
                }

                _activeTimelineInteraction.PreviewStart = _activeTimelineInteraction.OriginalStart;
                _activeTimelineInteraction.PreviewEnd = previewEnd;
                StatusTextBlock.Text = $"Черновик нового события: {_activeTimelineInteraction.PreviewStart:HH:mm} - {previewEnd:HH:mm}.";
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

        SetSelectedDate(targetDate, prepareQuickCreate: true);
    }

    private void PrepareQuickCreateForDate(DateTime date, string statusMessage)
    {
        _isSynchronizingSelection = true;
        DailyEventsListBox.SelectedItem = null;
        _isSynchronizingSelection = false;

        _selectedEventId = null;
        DeleteButton.IsEnabled = false;
        SeedEditorDefaults(date);
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
        UpdateEditorModeChrome(isEditing: false, start.Date, null);
        StatusTextBlock.Text = statusMessage;
    }

    private void UpdateEditorModeChrome(bool isEditing, DateTime selectedDate, string? eventTitle)
    {
        if (isEditing)
        {
            EditorModeTitleTextBlock.Text = "Редактирование события";
            EditorModeSubtitleTextBlock.Text = string.IsNullOrWhiteSpace(eventTitle)
                ? $"Изменения сохранятся для {selectedDate.ToString("d MMMM", RussianCulture)}."
                : $"Сейчас открыто событие «{eventTitle}» на {selectedDate.ToString("d MMMM", RussianCulture)}.";
            QuickCreateSelectedDateButton.Content = $"Новое событие на {selectedDate.ToString("d MMMM", RussianCulture)}";
            return;
        }

        EditorModeTitleTextBlock.Text = "Новое событие";
        EditorModeSubtitleTextBlock.Text = $"Быстрый черновик уже подготовлен на {selectedDate.ToString("d MMMM", RussianCulture)}. Остается заполнить детали и сохранить.";
        QuickCreateSelectedDateButton.Content = $"Создать на {selectedDate.ToString("d MMMM", RussianCulture)}";
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
                    ? $"1 событие: {dayEvents[0].Title}"
                    : $"{dayEvents.Count} события";
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

    private void BeginTimelineInteraction(object sender, TimelineInteractionMode mode)
    {
        if (!TryGetTimelineEvent(sender, out var timeGridEvent))
        {
            return;
        }

        if (_allEvents.FirstOrDefault(item => item.Id == timeGridEvent.Id) is not { } selected)
        {
            return;
        }

        ApplySelectionToEditor(selected);

        _activeTimelineInteraction = new TimelineInteractionState(
            selected.Id,
            timeGridEvent.Day,
            selected.StartsAtLocal,
            selected.EndsAtLocal,
            mode);

        if (ShouldUseWeekDragPreview(_activeTimelineInteraction))
        {
            SetWeekTimelineEventOpacity(selected.Id, 0d);
        }

        UpdateTimelinePreview(_activeTimelineInteraction, dayChanged: false);
    }

    private void UpdateTimelineInteraction(double verticalChange)
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
                var shiftedStart = dayStart.Add(_activeTimelineInteraction.OriginalStart.TimeOfDay).AddMinutes(minuteDelta);
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
                StatusTextBlock.Text = $"Перенос: {shiftedStart:ddd, d MMM HH:mm} - {shiftedEnd:HH:mm}.";
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
                StatusTextBlock.Text = $"Новая длительность: {_activeTimelineInteraction.PreviewStart:HH:mm} - {resizedEnd:HH:mm}.";
                break;
        }

        StartDatePicker.SelectedDate = _activeTimelineInteraction.PreviewStart.Date;
        EndDatePicker.SelectedDate = _activeTimelineInteraction.PreviewEnd.Date;
        StartTimeTextBox.Text = _activeTimelineInteraction.PreviewStart.ToString("HH:mm");
        EndTimeTextBox.Text = _activeTimelineInteraction.PreviewEnd.ToString("HH:mm");

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
            ApplyDraftEditorState(
                interaction.PreviewStart,
                interaction.PreviewEnd,
                $"Черновик нового события подготовлен на {interaction.PreviewStart:dd MMMM} с {interaction.PreviewStart:HH:mm} до {interaction.PreviewEnd:HH:mm}.");
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
                ? $"Событие «{updated.Title}» перенесено на {interaction.PreviewStart:HH:mm}."
                : $"Длительность события «{updated.Title}» изменена до {interaction.PreviewEnd:HH:mm}.";
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

        foreach (var timelineEvent in EnumerateVisibleTimelineEvents(interaction.EventId, interaction.PreviewStart.Date))
        {
            var (clampedStart, clampedEnd, top, height) =
                CalculateTimelineBounds(interaction.PreviewStart.Date, interaction.PreviewStart, interaction.PreviewEnd);
            timelineEvent.UpdateLayout(clampedStart, clampedEnd, top, height);
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

        WeekDragPreviewCard.Width = Math.Max(80d, columnWidth - (timelineCardHorizontalMargin * 2d));
        WeekDragPreviewCard.Height = height;
        WeekDragPreviewCard.Background = eventItem.AccentSurfaceBrush;
        WeekDragPreviewCard.BorderBrush = eventItem.AccentBorderBrush;
        WeekDragPreviewTitleTextBlock.Text = eventItem.Title;
        WeekDragPreviewTimeTextBlock.Text =
            $"{clampedStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {clampedEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        WeekDragPreviewTimeTextBlock.Foreground = eventItem.AccentBrush;
        WeekDragPreviewDescriptionTextBlock.Text = eventItem.Description;

        Canvas.SetLeft(WeekDragPreviewCard, columnLeft + timelineCardHorizontalMargin);
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

        WeekDragPreviewCard.Width = Math.Max(80d, columnWidth - (timelineCardHorizontalMargin * 2d));
        WeekDragPreviewCard.Height = height;
        WeekDragPreviewCard.Background = _draftPreviewSurfaceBrush;
        WeekDragPreviewCard.BorderBrush = _draftPreviewBorderBrush;
        WeekDragPreviewTitleTextBlock.Text = "Новое событие";
        WeekDragPreviewTimeTextBlock.Text =
            $"{clampedStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {clampedEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        WeekDragPreviewTimeTextBlock.Foreground = _draftPreviewAccentBrush;
        WeekDragPreviewDescriptionTextBlock.Text = "Отпустите мышь, чтобы заполнить черновик справа.";

        Canvas.SetLeft(WeekDragPreviewCard, columnLeft + timelineCardHorizontalMargin);
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

        DayDragPreviewCard.Width = Math.Max(80d, DayTimelineItemsControl.ActualWidth - (timelineCardHorizontalMargin * 2d));
        DayDragPreviewCard.Height = height;
        DayDragPreviewCard.Background = _draftPreviewSurfaceBrush;
        DayDragPreviewCard.BorderBrush = _draftPreviewBorderBrush;
        DayDragPreviewTitleTextBlock.Text = "Новое событие";
        DayDragPreviewTimeTextBlock.Text =
            $"{clampedStart.ToString("HH:mm", CultureInfo.InvariantCulture)} - {clampedEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        DayDragPreviewTimeTextBlock.Foreground = _draftPreviewAccentBrush;
        DayDragPreviewDescriptionTextBlock.Text = "Отпустите мышь, чтобы заполнить черновик справа.";

        Canvas.SetLeft(DayDragPreviewCard, timelineCardHorizontalMargin);
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
    }
}
