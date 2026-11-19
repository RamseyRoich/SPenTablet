using SPenTablet.Host.Adb;
using SPenTablet.Host.Mapping;
using SPenTablet.Host.Net;

namespace SPenTablet.Host;

internal sealed class TrayApp : ApplicationContext
{
    private readonly SynchronizationContext _ui = new WindowsFormsSynchronizationContext();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new("Starting…") { Enabled = false };
    private readonly AreaMapper _mapper;
    private readonly PenServer _server;
    private readonly System.Windows.Forms.Timer _adbTimer = new() { Interval = 3000 };

    public TrayApp()
    {
        var screens = Screen.AllScreens;
        var screen = _settings.MonitorIndex < screens.Length ? screens[_settings.MonitorIndex] : Screen.PrimaryScreen!;
        _mapper = new AreaMapper(screen.Bounds, _settings.KeepAspect);

        _server = new PenServer(_mapper, () => _settings.Curve);
        _server.Status += text => _ui.Post(_ => SetStatus(text), null); // hop to the UI thread

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildMonitorMenu());
        menu.Items.Add(BuildPressureMenu());
        var keep = new ToolStripMenuItem("Keep aspect ratio") { Checked = _settings.KeepAspect, CheckOnClick = true };
        keep.CheckedChanged += (_, _) => { _settings.KeepAspect = keep.Checked; _mapper.SetKeepAspect(keep.Checked); _settings.Save(); };
        menu.Items.Add(keep);
        var diag = new ToolStripMenuItem("Show diagnostics") { CheckOnClick = true };
        diag.CheckedChanged += (_, _) => ToggleDiagnostics(diag.Checked);
        menu.Items.Add(diag);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // replace with your own .ico later
            Text = "S Pen Tablet",
            ContextMenuStrip = menu,
            Visible = true,
        };

        // Keep the USB tunnel alive while no phone is connected (run adb off the UI thread).
        _adbTimer.Tick += (_, _) =>
        {
            if (!_server.IsConnected) Task.Run(() => AdbHelper.TryReverse(_settings.AdbPath, Protocol.Port));
        };
        _adbTimer.Start();
        Task.Run(() => AdbHelper.TryReverse(_settings.AdbPath, Protocol.Port));

        _server.Start();
    }

    private Form? _diagForm;
    private Label? _diagLabel;
    private readonly System.Windows.Forms.Timer _diagTimer = new() { Interval = 100 };

    /// <summary>Small always-on-top box: where the app sent the pen vs where Windows put the pointer.</summary>
    private void ToggleDiagnostics(bool on)
    {
        if (!on)
        {
            _diagTimer.Stop();
            _diagForm?.Close();
            _diagForm = null;
            return;
        }
        _diagLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.Lime, Font = new Font("Consolas", 14f) };
        _diagForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            TopMost = true,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.Black,
            Size = new Size(620, 190),
            Location = new Point(Screen.PrimaryScreen!.Bounds.Left + 40, Screen.PrimaryScreen!.Bounds.Top + 40),
        };
        _diagForm.Controls.Add(_diagLabel);
        _diagForm.Show();
        _diagTimer.Tick += (_, _) =>
        {
            var m = Screen.AllScreens[Math.Min(_settings.MonitorIndex, Screen.AllScreens.Length - 1)].Bounds;
            _diagLabel.Text =
                $"phone  x={_server.LastNx:0.000} y={_server.LastNy:0.000}\n" +
                $"sent   px {_server.LastX},{_server.LastY}\n" +
                $"cursor px {Cursor.Position.X},{Cursor.Position.Y}\n" +
                $"monitor {m.Left},{m.Top} {m.Width}x{m.Height}";
        };
        _diagTimer.Start();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _icon.Text = ("S Pen Tablet: " + text)[..Math.Min(63, ("S Pen Tablet: " + text).Length)]; // tooltip max 63 chars
    }

    private ToolStripMenuItem BuildMonitorMenu()
    {
        var root = new ToolStripMenuItem("Monitor");
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            int index = i;
            var s = screens[i];
            var item = new ToolStripMenuItem($"{i + 1}: {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " (main)" : "")}")
            {
                Checked = index == _settings.MonitorIndex,
            };
            item.Click += (_, _) =>
            {
                _settings.MonitorIndex = index;
                _settings.Save();
                _mapper.SetMonitor(s.Bounds);
                foreach (ToolStripMenuItem other in root.DropDownItems) other.Checked = other == item;
            };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    private ToolStripMenuItem BuildPressureMenu()
    {
        var root = new ToolStripMenuItem("Pressure feel");
        foreach (var (name, curve) in new[] { ("Soft", PressureCurve.Soft), ("Normal", PressureCurve.Normal), ("Firm", PressureCurve.Firm) })
        {
            var item = new ToolStripMenuItem(name) { Checked = Math.Abs(_settings.Gamma - curve.Gamma) < 0.01f };
            item.Click += (_, _) =>
            {
                _settings.Gamma = curve.Gamma;
                _settings.Save();
                foreach (ToolStripMenuItem other in root.DropDownItems) other.Checked = other == item;
            };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    protected override void ExitThreadCore()
    {
        _server.Stop();
        _adbTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
