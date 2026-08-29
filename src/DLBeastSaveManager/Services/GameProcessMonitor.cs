using System.Diagnostics;
using System.Security.Principal;
using System.Timers;
using Timer = System.Timers.Timer;

namespace DLBeastSaveManager.Services;

public sealed class GameProcessMonitor : IDisposable
{
    public const string PrimaryProcessName = "DyingLightGame_TheBeast_x64_rwdi";

    private static readonly string[] ProcessNamePrefixes =
    {
        "DyingLightGame_TheBeast",
        "DyingLightTheBeast"
    };

    private readonly Timer _timer;
    private bool _lastSeen;

    public GameProcessMonitor(int pollSeconds = 5)
    {
        _timer = new Timer(Math.Max(1, pollSeconds) * 1000) { AutoReset = true };
        _timer.Elapsed += OnTick;
    }

    public event EventHandler? GameStarted;

    public event EventHandler? GameExited;

    public bool IsGameRunning { get; private set; }

    public void Start()
    {
        _lastSeen = IsGameRunning = FindGameProcess() is not null;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        var running = FindGameProcess() is not null;
        IsGameRunning = running;

        if (running == _lastSeen) return;
        _lastSeen = running;

        if (running) GameStarted?.Invoke(this, EventArgs.Empty);
        else GameExited?.Invoke(this, EventArgs.Empty);
    }

    public bool CheckNow()
    {
        IsGameRunning = FindGameProcess() is not null;
        return IsGameRunning;
    }

    public static Process? FindGameProcess()
    {
        try
        {
            var exact = Process.GetProcessesByName(PrimaryProcessName);
            if (exact.Length > 0) return exact[0];

            return Process.GetProcesses().FirstOrDefault(p =>
                ProcessNamePrefixes.Any(prefix =>
                    p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsThisProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Elapsed -= OnTick;
        _timer.Dispose();
    }
}
