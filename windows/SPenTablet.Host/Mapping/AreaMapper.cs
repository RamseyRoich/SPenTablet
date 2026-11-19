using System.Drawing;

namespace SPenTablet.Host.Mapping;

public sealed class AreaMapper
{
    private sealed record State(Rectangle Monitor, float Ax, float Ay, float Aw, float Ah);

    private readonly object _lock = new();
    private volatile State _state;
    private Rectangle _monitor;
    private int _padW = 19, _padH = 9;
    private bool _keepAspect;

    public AreaMapper(Rectangle monitor, bool keepAspect)
    {
        _monitor = monitor;
        _keepAspect = keepAspect;
        _state = Compute();
    }

    public void SetMonitor(Rectangle m)    { lock (_lock) { _monitor = m; _state = Compute(); } }
    public void SetKeepAspect(bool keep)   { lock (_lock) { _keepAspect = keep; _state = Compute(); } }
    public void SetPad(int w, int h)
    {
        lock (_lock)
        {
            if (w > 0 && h > 0) { _padW = w; _padH = h; }
            _state = Compute();
        }
    }

    /// <summary>The active area of the phone, as fractions (0..1) of the pad.</summary>
    public RectangleF ActiveArea { get { var s = _state; return new(s.Ax, s.Ay, s.Aw, s.Ah); } }

    private State Compute()
    {
        float ax = 0, ay = 0, aw = 1, ah = 1;
        if (_keepAspect)
        {
            float pad = _padW / (float)_padH;
            float mon = _monitor.Width / (float)_monitor.Height;
            if (pad > mon) { aw = mon / pad; ax = (1 - aw) / 2; } // phone wider → trim left/right
            else           { ah = pad / mon; ay = (1 - ah) / 2; } // phone taller → trim top/bottom
        }
        return new State(_monitor, ax, ay, aw, ah);
    }

    /// <summary>Convert phone coords (0..1) to screen pixels.</summary>
    public (int X, int Y) Map(float nx, float ny)
    {
        var s = _state; // one consistent snapshot, even if settings change mid-stroke
        float u = Math.Clamp((nx - s.Ax) / s.Aw, 0f, 1f);
        float v = Math.Clamp((ny - s.Ay) / s.Ah, 0f, 1f);
        return (s.Monitor.Left + (int)MathF.Round(u * (s.Monitor.Width - 1)),
                s.Monitor.Top  + (int)MathF.Round(v * (s.Monitor.Height - 1)));
    }
}
