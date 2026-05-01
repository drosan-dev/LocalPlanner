using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private const double ExpandedSidebarWidth = 220d;
    private const double CollapsedSidebarWidth = 58d;
    private const double ExpandedSidebarSpacerWidth = 18d;
    private const double CollapsedSidebarSpacerWidth = 10d;
    private const string NewEventDefaultTitle = "\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435";
    private static readonly CultureInfo RussianCulture = new("ru-RU");
    private static readonly SolidColorBrush MarkerFallbackBrush = CreateFrozenBrush(Color.FromRgb(0x1A, 0x73, 0xE8));

    private readonly EventRepository _eventRepository;
    private readonly ObservableCollection<EventListItemViewModel> _allEvents = new();
    private readonly ObservableCollection<EventListItemViewModel> _selectedDayEvents = new();
    private readonly ObservableCollection<WeekDayColumnViewModel> _selectedWeekDays = new();
    private readonly ObservableCollection<MonthHeaderViewModel> _monthHeaders = new();
    private readonly ObservableCollection<MonthDayCellViewModel> _monthCells = new();
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
    private Guid? _pendingMonthDragEventId;
    private Point? _pendingMonthDragStartPoint;
    private bool _suppressMonthEventClick;
    private DateTime? _activeMonthRangeStart;
    private DateTime? _activeMonthRangeEnd;
    private Brush? _draftPreviewAccentBrush;
    private Brush? _draftPreviewSurfaceBrush;
    private Brush? _draftPreviewBorderBrush;
    private PlannerPage _currentPage = PlannerPage.Calendar;
    private bool _isSidebarOpen = true;

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
        MonthHeadersItemsControl.ItemsSource = _monthHeaders;
        MonthCellsItemsControl.ItemsSource = _monthCells;
        WeekHeadersItemsControl.ItemsSource = _selectedWeekDays;
        WeekBodiesItemsControl.ItemsSource = _selectedWeekDays;
        WeekHoursItemsControl.ItemsSource = _timeSlots;
        DayHoursItemsControl.ItemsSource = _timeSlots;
        DaySlotLinesItemsControl.ItemsSource = _timeSlots;
        DayTimelineItemsControl.ItemsSource = _selectedDayTimelineEvents;
        TimezoneComboBox.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        TimezoneComboBox.SelectedItem = TimeZoneInfo.Local;
        MonthViewRadioButton.IsChecked = true;
        CalendarNavRadioButton.IsChecked = true;
        ApplySidebarState();
        SeedMonthHeaders();

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
        RefreshMonthGrid();
        RefreshCalendarChrome();
        RefreshCalendarMarkers();

        StatusTextBlock.Text = _allEvents.Count == 0
            ? "\u041A\u0430\u043B\u0435\u043D\u0434\u0430\u0440\u044C \u043F\u0443\u0441\u0442. \u0421\u043E\u0437\u0434\u0430\u0439\u0442\u0435 \u043F\u0435\u0440\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435."
            : $"\u0412\u0441\u0435\u0433\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u0439: {_allEvents.Count}.";
    }

    private void NavigationRadioButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, out PlannerPage page))
        {
            return;
        }

        NavigateToPage(page);
    }

    private void NavigateToPage(PlannerPage page)
    {
        _currentPage = page;
        CalendarPageContent.Visibility = page == PlannerPage.Calendar ? Visibility.Visible : Visibility.Collapsed;
        EmptyPageContent.Visibility = page == PlannerPage.Calendar ? Visibility.Collapsed : Visibility.Visible;

        if (page == PlannerPage.Calendar)
        {
            Title = "\u041B\u043E\u043A\u0430\u043B\u044C\u043D\u044B\u0439 \u043F\u043B\u0430\u043D\u0435\u0440 - \u041A\u0430\u043B\u0435\u043D\u0434\u0430\u0440\u044C";
            return;
        }

        var pageText = GetEmptyPageText(page);
        Title = $"\u041B\u043E\u043A\u0430\u043B\u044C\u043D\u044B\u0439 \u043F\u043B\u0430\u043D\u0435\u0440 - {pageText.Title}";
        EmptyPageEyebrowTextBlock.Text = pageText.Eyebrow;
        EmptyPageTitleTextBlock.Text = pageText.Title;
        EmptyPageDescriptionTextBlock.Text = pageText.Description;
    }

    private static (string Eyebrow, string Title, string Description) GetEmptyPageText(PlannerPage page)
    {
        return page switch
        {
            PlannerPage.Today => (
                "\u0414\u043D\u0435\u0432\u043D\u043E\u0439 \u0444\u043E\u043A\u0443\u0441",
                "\u0421\u0435\u0433\u043E\u0434\u043D\u044F",
                "\u0417\u0434\u0435\u0441\u044C \u043F\u043E\u044F\u0432\u0438\u0442\u0441\u044F \u043E\u0431\u0437\u043E\u0440 \u0434\u043D\u044F: \u0432\u0430\u0436\u043D\u043E\u0435, \u0444\u043E\u043A\u0443\u0441, \u0437\u0430\u0434\u0430\u0447\u0438 \u0438 \u0432\u043E\u0437\u043C\u043E\u0436\u043D\u043E\u0441\u0442\u0438. \u041F\u043E\u043A\u0430 \u0441\u043E\u0437\u0434\u0430\u043D\u0430 \u0442\u043E\u0447\u043A\u0430 \u0432\u0445\u043E\u0434\u0430 \u0434\u043B\u044F \u0431\u0443\u0434\u0443\u0449\u0435\u0433\u043E Today-\u044D\u043A\u0440\u0430\u043D\u0430."),
            PlannerPage.Planning => (
                "\u041F\u043B\u0430\u043D\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435",
                "\u041F\u043B\u0430\u043D\u0438\u043D\u0433",
                "\u0411\u0443\u0434\u0443\u0449\u0438\u0439 \u0446\u0435\u043D\u0442\u0440 \u043F\u0435\u0440\u0435\u043D\u043E\u0441\u0430 \u0438\u0434\u0435\u0439 \u0432 \u0440\u0430\u0441\u043F\u0438\u0441\u0430\u043D\u0438\u0435. \u041D\u0430 \u044D\u0442\u043E\u043C \u044D\u0442\u0430\u043F\u0435 \u043E\u043D \u043E\u0441\u0442\u0430\u0451\u0442\u0441\u044F \u043F\u0443\u0441\u0442\u044B\u043C, \u0447\u0442\u043E\u0431\u044B \u043D\u0435 \u0432\u0432\u043E\u0434\u0438\u0442\u044C \u043D\u043E\u0432\u044B\u0435 \u0441\u0443\u0449\u043D\u043E\u0441\u0442\u0438."),
            PlannerPage.Tasks => (
                "\u0421\u043F\u0438\u0441\u043A\u0438",
                "\u0417\u0430\u0434\u0430\u0447\u0438",
                "\u0417\u0434\u0435\u0441\u044C \u0431\u0443\u0434\u0443\u0442 \u0438\u043D\u0431\u043E\u043A\u0441, \u0437\u0430\u0434\u0430\u0447\u0438 \u043D\u0430 \u0441\u0435\u0433\u043E\u0434\u043D\u044F, \u0434\u0435\u0434\u043B\u0430\u0439\u043D\u044B \u0438 \u0430\u0440\u0445\u0438\u0432. \u0412 Stage 1 \u0434\u043E\u0431\u0430\u0432\u043B\u0435\u043D\u0430 \u0442\u043E\u043B\u044C\u043A\u043E \u043D\u0430\u0432\u0438\u0433\u0430\u0446\u0438\u043E\u043D\u043D\u0430\u044F \u0437\u0430\u0433\u043B\u0443\u0448\u043A\u0430."),
            PlannerPage.Projects => (
                "\u041A\u043E\u043D\u0442\u0435\u043A\u0441\u0442",
                "\u041F\u0440\u043E\u0435\u043A\u0442\u044B",
                "\u041C\u0435\u0441\u0442\u043E \u0434\u043B\u044F \u0431\u0443\u0434\u0443\u0449\u0438\u0445 \u0440\u0430\u0431\u043E\u0447\u0438\u0445 \u043E\u0431\u043B\u0430\u0441\u0442\u0435\u0439, \u0441\u0447\u0451\u0442\u0447\u0438\u043A\u043E\u0432 \u0438 \u0441\u0432\u044F\u0437\u0430\u043D\u043D\u044B\u0445 \u043F\u043B\u0430\u043D\u043E\u0432. \u041F\u0435\u0440\u0441\u0438\u0441\u0442\u0435\u043D\u0442\u043D\u044B\u0435 \u043F\u0440\u043E\u0435\u043A\u0442\u044B \u0431\u0443\u0434\u0443\u0442 \u0432 \u0441\u043B\u0435\u0434\u0443\u044E\u0449\u0438\u0445 \u044D\u0442\u0430\u043F\u0430\u0445."),
            PlannerPage.Routines => (
                "\u041F\u043E\u0432\u0442\u043E\u0440\u044B",
                "\u0420\u0443\u0442\u0438\u043D\u044B",
                "\u0417\u0434\u0435\u0441\u044C \u043F\u043E\u0437\u0436\u0435 \u043F\u043E\u044F\u0432\u044F\u0442\u0441\u044F \u043F\u0440\u0430\u0432\u0438\u043B\u0430 \u043F\u043E\u0432\u0442\u043E\u0440\u0435\u043D\u0438\u044F \u0438 \u0433\u0435\u043D\u0435\u0440\u0430\u0446\u0438\u044F \u0437\u0430\u0434\u0430\u0447. \u041D\u043E\u0432\u044B\u0435 \u0440\u0443\u0442\u0438\u043D\u044B \u043F\u043E\u043A\u0430 \u043D\u0435 \u0441\u043E\u0437\u0434\u0430\u044E\u0442\u0441\u044F."),
            PlannerPage.Trackers => (
                "\u0414\u043E\u043B\u0433\u0438\u0435 \u0434\u0435\u043B\u0430",
                "\u0422\u0440\u0435\u043A\u0435\u0440\u044B",
                "\u0411\u0443\u0434\u0443\u0449\u0438\u0439 \u0434\u043E\u043C \u0434\u043B\u044F \u043A\u043D\u0438\u0433, \u043A\u0443\u0440\u0441\u043E\u0432, \u0438\u0433\u0440 \u0438 \u0434\u0440\u0443\u0433\u0438\u0445 \u0434\u043B\u0438\u043D\u043D\u044B\u0445 \u0430\u043A\u0442\u0438\u0432\u043D\u043E\u0441\u0442\u0435\u0439. \u042D\u0442\u0430 \u0437\u0430\u0433\u043B\u0443\u0448\u043A\u0430 \u043D\u0435 \u043C\u0435\u043D\u044F\u0435\u0442 \u0434\u0430\u043D\u043D\u044B\u0435."),
            PlannerPage.Archive => (
                "\u0418\u0441\u0442\u043E\u0440\u0438\u044F",
                "\u0410\u0440\u0445\u0438\u0432",
                "\u0417\u0434\u0435\u0441\u044C \u0431\u0443\u0434\u0443\u0442 \u0437\u0430\u0432\u0435\u0440\u0448\u0451\u043D\u043D\u044B\u0435, \u043E\u0442\u043C\u0435\u043D\u0451\u043D\u043D\u044B\u0435 \u0438 \u0441\u043A\u0440\u044B\u0442\u044B\u0435 \u044D\u043B\u0435\u043C\u0435\u043D\u0442\u044B \u043F\u043B\u0430\u043D\u0435\u0440\u0430. \u041F\u043E\u043A\u0430 \u0430\u0440\u0445\u0438\u0432 \u043D\u0435 \u0432\u043B\u0438\u044F\u0435\u0442 \u043D\u0430 \u0441\u043E\u0431\u044B\u0442\u0438\u044F."),
            PlannerPage.Settings => (
                "\u041A\u043E\u043D\u0444\u0438\u0433\u0443\u0440\u0430\u0446\u0438\u044F",
                "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438",
                "\u0411\u0443\u0434\u0443\u0449\u0435\u0435 \u043C\u0435\u0441\u0442\u043E \u0434\u043B\u044F \u043D\u0430\u0447\u0430\u043B\u0430 \u0434\u043D\u044F, \u0434\u043B\u0438\u0442\u0435\u043B\u044C\u043D\u043E\u0441\u0442\u0438 \u0437\u0430\u0434\u0430\u0447 \u0438 \u043D\u0430\u043F\u043E\u043C\u0438\u043D\u0430\u043D\u0438\u0439. \u0412 Stage 1 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u0435\u0449\u0451 \u043D\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u044F\u044E\u0442\u0441\u044F."),
            _ => (
                "\u0421\u043A\u043E\u0440\u043E",
                "\u0420\u0430\u0437\u0434\u0435\u043B \u0432 \u0440\u0430\u0437\u0440\u0430\u0431\u043E\u0442\u043A\u0435",
                "\u0417\u0434\u0435\u0441\u044C \u043F\u043E\u044F\u0432\u0438\u0442\u0441\u044F \u0441\u043E\u0434\u0435\u0440\u0436\u0438\u043C\u043E\u0435 \u0431\u0443\u0434\u0443\u0449\u0435\u0433\u043E \u0440\u0430\u0437\u0434\u0435\u043B\u0430.")
        };
    }

    private void SidebarToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isSidebarOpen = !_isSidebarOpen;
        ApplySidebarState();
    }

    private void ApplySidebarState()
    {
        SidebarColumn.Width = new GridLength(_isSidebarOpen ? ExpandedSidebarWidth : CollapsedSidebarWidth);
        SidebarSpacerColumn.Width = new GridLength(_isSidebarOpen ? ExpandedSidebarSpacerWidth : CollapsedSidebarSpacerWidth);
        SidebarBorder.Padding = _isSidebarOpen ? new Thickness(18) : new Thickness(12);
        SidebarHeaderContent.Visibility = _isSidebarOpen ? Visibility.Visible : Visibility.Collapsed;
        SidebarNavigationContent.Visibility = Visibility.Visible;
        SettingsNavRadioButton.Visibility = Visibility.Visible;
        SidebarToggleButton.Content = _isSidebarOpen ? "\u2630" : "\u2630";
        SidebarToggleButton.ToolTip = _isSidebarOpen ? "\u0421\u0432\u0435\u0440\u043D\u0443\u0442\u044C \u043D\u0430\u0432\u0438\u0433\u0430\u0446\u0438\u044E" : "\u041E\u0442\u043A\u0440\u044B\u0442\u044C \u043D\u0430\u0432\u0438\u0433\u0430\u0446\u0438\u044E";
        ApplyNavigationButtonState(TodayNavRadioButton, "\u25C9", "\u0421\u0435\u0433\u043E\u0434\u043D\u044F");
        ApplyNavigationButtonState(PlanningNavRadioButton, "\u2726", "\u041F\u043B\u0430\u043D\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435");
        ApplyNavigationButtonState(TasksNavRadioButton, "\u2713", "\u0417\u0430\u0434\u0430\u0447\u0438");
        ApplyNavigationButtonState(ProjectsNavRadioButton, "\u25A3", "\u041F\u0440\u043E\u0435\u043A\u0442\u044B");
        ApplyNavigationButtonState(RoutinesNavRadioButton, "\u21BB", "\u0420\u0443\u0442\u0438\u043D\u044B");
        ApplyNavigationButtonState(TrackersNavRadioButton, "\u25CE", "\u0422\u0440\u0435\u043A\u0435\u0440\u044B");
        ApplyNavigationButtonState(CalendarNavRadioButton, "\u25A6", "\u041A\u0430\u043B\u0435\u043D\u0434\u0430\u0440\u044C");
        ApplyNavigationButtonState(ArchiveNavRadioButton, "\u25F7", "\u0410\u0440\u0445\u0438\u0432");
        ApplyNavigationButtonState(SettingsNavRadioButton, "\u2699", "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438");
    }

    private void ApplyNavigationButtonState(RadioButton button, string icon, string label)
    {
        button.Content = _isSidebarOpen ? $"{icon}  {label}" : icon;
        button.ToolTip = label;
        button.HorizontalContentAlignment = _isSidebarOpen ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        button.Padding = _isSidebarOpen ? new Thickness(12, 10, 12, 10) : new Thickness(0, 10, 0, 10);
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
        RefreshMonthGrid();
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

    private void MonthCell_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null)
        {
            return;
        }

        if (sender is not Border { DataContext: MonthDayCellViewModel cell })
        {
            return;
        }

        _activeMonthRangeStart = cell.Date;
        _activeMonthRangeEnd = cell.Date;
        SetSelectedDate(cell.Date, prepareQuickCreate: false);

        if (e.ClickCount >= 2)
        {
            _activeMonthRangeStart = null;
            _activeMonthRangeEnd = null;
            SetViewMode(CalendarViewMode.Day);
            DayViewRadioButton.IsChecked = true;
        }

        e.Handled = true;
    }

    private void MonthCell_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_activeMonthRangeStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is Border { DataContext: MonthDayCellViewModel cell })
        {
            _activeMonthRangeEnd = cell.Date;
            StatusTextBlock.Text = BuildMonthRangeDraftStatus(_activeMonthRangeStart.Value, cell.Date);
        }
    }

    private void MonthCell_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeMonthRangeStart is null)
        {
            return;
        }

        var start = _activeMonthRangeStart.Value;
        var end = _activeMonthRangeEnd ?? start;
        _activeMonthRangeStart = null;
        _activeMonthRangeEnd = null;

        if (start == end)
        {
            return;
        }

        CreateAllDayMonthRange(start, end);
        e.Handled = true;
    }

    private void MonthCell_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDraggedMonthEventId(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void MonthCell_OnDrop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedMonthEventId(e, out var eventId) ||
            sender is not Border { DataContext: MonthDayCellViewModel cell })
        {
            return;
        }

        MoveMonthEventToDate(eventId, cell.Date);
        e.Handled = true;
    }

    private void MonthOverflowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.Tag is not DateTime date)
        {
            var fallback = (button.DataContext as MonthDayCellViewModel)?.Date;
            if (fallback is null)
            {
                return;
            }

            date = fallback.Value;
        }

        SetSelectedDate(date, prepareQuickCreate: false);
        SetViewMode(CalendarViewMode.Day);
        DayViewRadioButton.IsChecked = true;
        e.Handled = true;
    }

    private void MonthEventChip_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressMonthEventClick)
        {
            _suppressMonthEventClick = false;
            e.Handled = true;
            return;
        }

        if (sender is not Button button)
        {
            return;
        }

        if (button.Tag is Guid eventId)
        {
            SelectEvent(eventId);
            e.Handled = true;
            return;
        }

        if (button.DataContext is MonthEventChipViewModel chip)
        {
            SelectEvent(chip.Id);
            e.Handled = true;
            return;
        }

        if (button.DataContext is MonthEventBarViewModel bar)
        {
            SelectEvent(bar.Id);
            e.Handled = true;
        }
    }

    private void MonthEvent_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: Guid eventId })
        {
            _pendingMonthDragEventId = eventId;
            _pendingMonthDragStartPoint = e.GetPosition(this);
        }
    }

    private void MonthEvent_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingMonthDragEventId is not { } eventId ||
            _pendingMonthDragStartPoint is not { } startPoint ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        var distanceX = Math.Abs(current.X - startPoint.X);
        var distanceY = Math.Abs(current.Y - startPoint.Y);
        if (distanceX < SystemParameters.MinimumHorizontalDragDistance &&
            distanceY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _activeMonthRangeStart = null;
        _activeMonthRangeEnd = null;
        _pendingMonthDragEventId = null;
        _pendingMonthDragStartPoint = null;
        _suppressMonthEventClick = true;
        DragDrop.DoDragDrop((DependencyObject)sender, eventId.ToString(), DragDropEffects.Move);
        Dispatcher.BeginInvoke(() => _suppressMonthEventClick = false, DispatcherPriority.ContextIdle);
        e.Handled = true;
    }

    private static bool TryGetDraggedMonthEventId(DragEventArgs e, out Guid eventId)
    {
        eventId = Guid.Empty;
        return e.Data.GetData(DataFormats.StringFormat) is string value && Guid.TryParse(value, out eventId);
    }

    private void MoveMonthEventToDate(Guid eventId, DateTime targetDate)
    {
        if (_allEvents.FirstOrDefault(item => item.Id == eventId) is not { } existing)
        {
            return;
        }

        var duration = existing.EndsAtLocal - existing.StartsAtLocal;
        if (duration <= TimeSpan.Zero)
        {
            duration = existing.IsAllDay ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(MinimumDurationMinutes);
        }

        var nextStart = existing.IsAllDay
            ? targetDate.Date
            : targetDate.Date.Add(existing.StartsAtLocal.TimeOfDay);
        var nextEnd = nextStart.Add(duration);

        try
        {
            var updated = _eventRepository.Save(new EventEditorState
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                StartLocal = nextStart,
                EndLocal = nextEnd,
                TimezoneId = existing.TimezoneId,
                IsAllDay = existing.IsAllDay,
                RRuleText = existing.RRuleText
            });

            LoadEvents();
            SelectEvent(updated.Id);
            StatusTextBlock.Text = $"\u0421\u043E\u0431\u044B\u0442\u0438\u0435 \u00AB{updated.Title}\u00BB \u043F\u0435\u0440\u0435\u043D\u0435\u0441\u0435\u043D\u043E \u043D\u0430 {targetDate.ToString("d MMMM", RussianCulture)}.";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = exception.Message;
            LoadEvents();
        }
    }

    private void CreateAllDayMonthRange(DateTime firstDate, DateTime secondDate)
    {
        var start = firstDate <= secondDate ? firstDate.Date : secondDate.Date;
        var inclusiveEnd = firstDate <= secondDate ? secondDate.Date : firstDate.Date;
        var end = inclusiveEnd.AddDays(1);

        try
        {
            var timezone = TimezoneComboBox.SelectedItem as TimeZoneInfo ?? TimeZoneInfo.Local;
            var created = _eventRepository.Save(new EventEditorState
            {
                Id = null,
                Title = NewEventDefaultTitle,
                Description = string.Empty,
                StartLocal = start,
                EndLocal = end,
                TimezoneId = timezone.Id,
                IsAllDay = true,
                RRuleText = string.Empty
            });

            LoadEvents();
            SelectEvent(created.Id);
            StatusTextBlock.Text = $"\u0421\u043E\u0437\u0434\u0430\u043D\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u0435 \u043D\u0430 {BuildMonthRangeLabel(start, inclusiveEnd)}.";
        }
        catch (Exception exception)
        {
            ApplyAllDayRangeDraft(start, end, exception.Message);
        }
    }

    private void ApplyAllDayRangeDraft(DateTime start, DateTime end, string statusMessage)
    {
        ApplyDraftEditorState(start, end, statusMessage);
        StartTimeTextBox.Text = "00:00";
        EndTimeTextBox.Text = "00:00";
        AllDayCheckBox.IsChecked = true;
    }

    private static string BuildMonthRangeDraftStatus(DateTime firstDate, DateTime secondDate)
    {
        var start = firstDate <= secondDate ? firstDate.Date : secondDate.Date;
        var end = firstDate <= secondDate ? secondDate.Date : firstDate.Date;
        return $"\u041D\u043E\u0432\u043E\u0435 \u0441\u043E\u0431\u044B\u0442\u0438\u0435: {BuildMonthRangeLabel(start, end)}.";
    }

    private static string BuildMonthRangeLabel(DateTime start, DateTime inclusiveEnd)
    {
        return start == inclusiveEnd
            ? start.ToString("d MMMM", RussianCulture)
            : $"{start.ToString("d MMM", RussianCulture)} - {inclusiveEnd.ToString("d MMMM", RussianCulture)}";
    }

    private void TodayButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetSelectedDate(DateTime.Today, prepareQuickCreate: false);
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_currentPage != PlannerPage.Calendar)
        {
            return;
        }

        if (Keyboard.FocusedElement is DependencyObject focused &&
            (FindVisualAncestor<TextBoxBase>(focused) is not null ||
             FindVisualAncestor<ComboBox>(focused) is not null ||
             FindVisualAncestor<DatePicker>(focused) is not null))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1:
                    DayViewRadioButton.IsChecked = true;
                    e.Handled = true;
                    return;
                case Key.D2:
                    WeekViewRadioButton.IsChecked = true;
                    e.Handled = true;
                    return;
                case Key.D3:
                    MonthViewRadioButton.IsChecked = true;
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.T)
        {
            SetSelectedDate(DateTime.Today, prepareQuickCreate: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N)
        {
            var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
            PrepareQuickCreateForDate(selectedDate, $"\u0417\u0430\u043F\u043E\u043B\u043D\u0438\u0442\u0435 \u0444\u043E\u0440\u043C\u0443 \u043D\u043E\u0432\u043E\u0433\u043E \u0441\u043E\u0431\u044B\u0442\u0438\u044F \u043D\u0430 {selectedDate.ToString("d MMMM", RussianCulture)}.");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && EditorPanelBorder.Visibility == Visibility.Visible)
        {
            CloseEditor();
            e.Handled = true;
            return;
        }

        if (_viewMode != CalendarViewMode.Month)
        {
            return;
        }

        var date = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        var moved = false;

        switch (e.Key)
        {
            case Key.Left:
                date = date.AddDays(-1);
                moved = true;
                break;
            case Key.Right:
                date = date.AddDays(1);
                moved = true;
                break;
            case Key.Up:
                date = date.AddDays(-7);
                moved = true;
                break;
            case Key.Down:
                date = date.AddDays(7);
                moved = true;
                break;
            case Key.PageUp:
                NavigateRange(-1);
                e.Handled = true;
                return;
            case Key.PageDown:
                NavigateRange(1);
                e.Handled = true;
                return;
            case Key.Enter:
                DayViewRadioButton.IsChecked = true;
                e.Handled = true;
                return;
        }

        if (moved)
        {
            SetSelectedDate(date, prepareQuickCreate: false);
            e.Handled = true;
        }
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
        RefreshMonthGrid();
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
                     .Where(item => EventOccursOnDay(item, selectedDate))
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
                .Where(item => EventOccursOnDay(item, day))
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
    }

    private void SeedMonthHeaders()
    {
        _monthHeaders.Clear();

        var week = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };

        foreach (var dayOfWeek in week)
        {
            _monthHeaders.Add(new MonthHeaderViewModel(dayOfWeek));
        }
    }

    private void RefreshMonthGrid()
    {
        var selectedDate = MonthCalendar.SelectedDate?.Date ?? DateTime.Today;
        var displayDate = MonthCalendar.DisplayDate.Date;
        var firstOfMonth = new DateTime(displayDate.Year, displayDate.Month, 1);
        var gridStart = StartOfWeek(firstOfMonth, DayOfWeek.Monday);
        var eventBarsByDate = BuildMonthEventBarsByDate(gridStart);

        _monthCells.Clear();

        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index).Date;
            var dayEvents = _allEvents
                .Where(item => EventOccursOnDay(item, date))
                .OrderBy(item => item.IsAllDay ? 0 : 1)
                .ThenBy(GetDisplayStart)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var eventBars = eventBarsByDate.TryGetValue(date, out var bars)
                ? bars
                : new List<MonthEventBarViewModel>();
            var visibleChips = dayEvents
                .Where(item => !IsMultiDayMonthEvent(item))
                .Take(3)
                .Select(item => new MonthEventChipViewModel(item, GetDisplayStart(item)))
                .ToList();

            var overflow = Math.Max(0, dayEvents.Count - eventBars.Count - visibleChips.Count);
            var accessibilityLabel = BuildMonthCellAccessibilityLabel(date, dayEvents);

            _monthCells.Add(new MonthDayCellViewModel(
                date,
                displayDate,
                selectedDate,
                isToday: date == DateTime.Today,
                eventBars,
                visibleChips,
                overflow,
                accessibilityLabel));
        }
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek weekStartsAt)
    {
        var offset = ((int)date.DayOfWeek - (int)weekStartsAt + 7) % 7;
        return date.AddDays(-offset).Date;
    }

    private Dictionary<DateTime, List<MonthEventBarViewModel>> BuildMonthEventBarsByDate(DateTime gridStart)
    {
        var result = new Dictionary<DateTime, List<MonthEventBarViewModel>>();
        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index).Date;
            result[date] = new List<MonthEventBarViewModel>();
        }

        var multiDayEvents = _allEvents
            .Where(IsMultiDayMonthEvent)
            .OrderBy(GetDisplayStart)
            .ThenByDescending(item => (GetDisplayEnd(item) - GetDisplayStart(item)).TotalDays)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var week = 0; week < 6; week++)
        {
            var weekStart = gridStart.AddDays(week * 7).Date;
            var weekEnd = weekStart.AddDays(6).Date;
            var laneOccupancy = new List<bool[]>();
            var laneEvents = new Dictionary<(int Lane, DateTime Date), MonthEventBarViewModel>();

            foreach (var item in multiDayEvents)
            {
                var start = GetDisplayStart(item).Date;
                var end = ResolveInclusiveMonthEnd(item);
                if (end < weekStart || start > weekEnd)
                {
                    continue;
                }

                var segmentStart = start > weekStart ? start : weekStart;
                var segmentEnd = end < weekEnd ? end : weekEnd;
                var startIndex = (int)(segmentStart - weekStart).TotalDays;
                var endIndex = (int)(segmentEnd - weekStart).TotalDays;
                var lane = FindAvailableMonthLane(laneOccupancy, startIndex, endIndex);

                for (var dayIndex = startIndex; dayIndex <= endIndex; dayIndex++)
                {
                    laneOccupancy[lane][dayIndex] = true;
                    var date = weekStart.AddDays(dayIndex).Date;
                    laneEvents[(lane, date)] = new MonthEventBarViewModel(item, date, segmentStart, segmentEnd);
                }
            }

            for (var dayIndex = 0; dayIndex < 7; dayIndex++)
            {
                var date = weekStart.AddDays(dayIndex).Date;
                if (!result.ContainsKey(date))
                {
                    continue;
                }

                for (var lane = 0; lane < laneOccupancy.Count; lane++)
                {
                    result[date].Add(laneEvents.TryGetValue((lane, date), out var bar)
                        ? bar
                        : MonthEventBarViewModel.Placeholder(date));
                }
            }
        }

        return result;
    }

    private static int FindAvailableMonthLane(List<bool[]> laneOccupancy, int startIndex, int endIndex)
    {
        for (var lane = 0; lane < laneOccupancy.Count; lane++)
        {
            var isAvailable = true;
            for (var dayIndex = startIndex; dayIndex <= endIndex; dayIndex++)
            {
                if (!laneOccupancy[lane][dayIndex])
                {
                    continue;
                }

                isAvailable = false;
                break;
            }

            if (isAvailable)
            {
                return lane;
            }
        }

        laneOccupancy.Add(new bool[7]);
        return laneOccupancy.Count - 1;
    }

    private DateTime ResolveInclusiveMonthEnd(EventListItemViewModel item)
    {
        var end = GetDisplayEnd(item);
        if (item.IsAllDay && end.TimeOfDay == TimeSpan.Zero)
        {
            end = end.Date.AddDays(-1);
        }

        var start = GetDisplayStart(item).Date;
        return end.Date < start ? start : end.Date;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static string BuildMonthCellAccessibilityLabel(DateTime date, IReadOnlyList<EventListItemViewModel> events)
    {
        var caption = date.ToString("d MMMM", RussianCulture);
        if (events.Count == 0)
        {
            return $"{caption}: событий нет.";
        }

        var builder = new StringBuilder();
        builder.Append(caption);
        builder.Append(": ");

        var limit = Math.Min(events.Count, 6);
        for (var i = 0; i < limit; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(events[i].Title);
        }

        if (events.Count > limit)
        {
            builder.Append("…");
        }

        builder.Append('.');
        return builder.ToString();
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

        var eventsByDate = BuildEventsByVisibleDate(_allEvents);
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

    private Dictionary<DateTime, List<EventListItemViewModel>> BuildEventsByVisibleDate(IEnumerable<EventListItemViewModel> events)
    {
        var eventsByDate = new Dictionary<DateTime, List<EventListItemViewModel>>();
        foreach (var item in events)
        {
            var start = GetDisplayStart(item).Date;
            var end = GetDisplayEnd(item);
            var inclusiveEnd = item.IsAllDay && end.TimeOfDay == TimeSpan.Zero
                ? end.Date.AddDays(-1)
                : end.Date;

            if (inclusiveEnd < start)
            {
                inclusiveEnd = start;
            }

            for (var day = start; day <= inclusiveEnd; day = day.AddDays(1))
            {
                if (!eventsByDate.TryGetValue(day, out var dayEvents))
                {
                    dayEvents = new List<EventListItemViewModel>();
                    eventsByDate[day] = dayEvents;
                }

                dayEvents.Add(item);
            }
        }

        return eventsByDate;
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

    private bool EventOccursOnDay(EventListItemViewModel item, DateTime day)
    {
        var start = GetDisplayStart(item);
        var end = GetDisplayEnd(item);
        if (end <= start)
        {
            end = start.AddMinutes(MinimumDurationMinutes);
        }

        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        return start < dayEnd && end > dayStart;
    }

    private bool IsMultiDayMonthEvent(EventListItemViewModel item)
    {
        var start = GetDisplayStart(item);
        var end = GetDisplayEnd(item);
        if (end <= start)
        {
            return false;
        }

        var inclusiveEnd = item.IsAllDay && end.TimeOfDay == TimeSpan.Zero
            ? end.Date.AddDays(-1)
            : end.Date;

        return inclusiveEnd > start.Date;
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

    private enum PlannerPage
    {
        Today,
        Planning,
        Tasks,
        Projects,
        Routines,
        Trackers,
        Calendar,
        Archive,
        Settings
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
