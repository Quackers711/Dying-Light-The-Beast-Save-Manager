using System.Threading;
using System.Windows;
using DLBeastSaveManager.Views;

namespace DLBeastSaveManager;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\DLBeastSaveManager.SingleInstance";

    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageWindow.Show(null, "Already running",
                "DL:TB Save Manager is already running - look for the shield icon in the system tray.");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageWindow.Show(MainWindow, "Unexpected error",
                $"Something went wrong:\n\n{args.Exception.Message}\n\nThe tool will keep running, " +
                "but check that backups are still being taken.");
            args.Handled = true;
        };

        var startMinimized = e.Args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-m", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow(startMinimized);
        MainWindow = window;
        window.Launch();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
