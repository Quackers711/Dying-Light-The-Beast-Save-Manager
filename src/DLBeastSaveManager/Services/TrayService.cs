using System.Drawing;
using System.Windows.Forms;

namespace DLBeastSaveManager.Services;

public enum TrayState
{
    Idle,
    Watching,
    Attention
}

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _watchItem;
    private Icon? _currentIcon;
    private TrayState? _state;
    private string _tooltip = string.Empty;

    public TrayService()
    {
        _watchItem = new ToolStripMenuItem("Watching", null, (_, _) => ToggleWatchRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = false
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripMenuItem("Backup now", null, (_, _) => BackupRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(_watchItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new NotifyIcon
        {
            Text = "DL:TB Save Manager",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        SetState(TrayState.Idle, "Not watching");
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? BackupRequested;
    public event EventHandler? ToggleWatchRequested;
    public event EventHandler? ExitRequested;

    public void SetState(TrayState state, string tooltip)
    {
        if (_state != state)
        {
            var accent = state switch
            {
                TrayState.Watching => AppIcons.Watching,
                TrayState.Attention => AppIcons.Attention,
                _ => AppIcons.Idle
            };

            var next = AppIcons.CreateIcon(accent, 32);
            _icon.Icon = next;
            _currentIcon?.Dispose();
            _currentIcon = next;

            _watchItem.Text = state == TrayState.Watching ? "Stop watching" : "Start watching";
            _state = state;
        }

        var text = tooltip.Length <= 63 ? tooltip : tooltip[..60] + "...";
        if (text == _tooltip) return;

        _icon.Text = text;
        _tooltip = text;
    }

    public void Notify(string title, string message, bool warning = false)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
        _icon.ShowBalloonTip(warning ? 5000 : 2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
