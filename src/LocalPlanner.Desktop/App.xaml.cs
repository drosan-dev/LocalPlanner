using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using LocalPlanner.Desktop.Services;

namespace LocalPlanner.Desktop;

public partial class App : Application
{
    public EventRepository? EventRepository { get; private set; }

    public ProjectRepository? ProjectRepository { get; private set; }

    public PlanningRepository? PlanningRepository { get; private set; }

    public TaskRepository? TaskRepository { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = new CultureInfo("ru-RU");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalPlanner");
        Directory.CreateDirectory(appDirectory);

        var databasePath = Path.Combine(appDirectory, "localplanner.db");
        var initializer = new DatabaseInitializer(databasePath);
        initializer.Initialize();
        EventRepository = new EventRepository(initializer.ConnectionString);
        ProjectRepository = new ProjectRepository(initializer.ConnectionString);
        PlanningRepository = new PlanningRepository(initializer.ConnectionString);
        TaskRepository = new TaskRepository(initializer.ConnectionString);
    }
}
