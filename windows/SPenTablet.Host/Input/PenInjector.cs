using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static SPenTablet.Host.Input.NativeMethods;

namespace SPenTablet.Host.Input;

/// <summary>A virtual pen. Create and use it on ONE thread only.</summary>
public sealed class PenInjector : IDisposable
{
    private readonly IntPtr _device;
    private readonly POINTER_TYPE_INFO[] _info = new POINTER_TYPE_INFO[1];
    private bool _inRange, _inContact;
    private int _lastX, _lastY;

    public PenInjector()
    {
        _device = CreateSyntheticPointerDevice(PT_PEN, 1, POINTER_FEEDBACK_DEFAULT);
        if (_device == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Update(int x, int y, bool inRange, bool inContact, uint pressure,
                       int tiltX, int tiltY, bool barrel, bool eraser)
    {
        if (!inRange) { ReleaseAll(); return; }

        PointerFlags flags;
        if (inContact && !_inContact)      flags = PointerFlags.InRange | PointerFlags.InContact | PointerFlags.FirstButton | PointerFlags.Down;
        else if (!inContact && _inContact) flags = PointerFlags.InRange | PointerFlags.Up;
        else if (inContact)                flags = PointerFlags.InRange | PointerFlags.InContact | PointerFlags.FirstButton | PointerFlags.Update;
        else                               flags = PointerFlags.InRange | PointerFlags.Update;

        Send(x, y, flags, inContact ? pressure : 0, tiltX, tiltY, barrel, eraser, inContact);
        _inRange = true;
        _inContact = inContact;
        _lastX = x;
        _lastY = y;
    }

    /// <summary>Lift the pen (if down) and take it out of range. Safe to call any time.</summary>
    public void ReleaseAll()
    {
        if (_inContact) Send(_lastX, _lastY, PointerFlags.InRange | PointerFlags.Up, 0, 0, 0, false, false, false);
        if (_inRange || _inContact) Send(_lastX, _lastY, PointerFlags.Update, 0, 0, 0, false, false, false);
        _inRange = _inContact = false;
    }

    private void Send(int x, int y, PointerFlags flags, uint pressure, int tiltX, int tiltY,
                      bool barrel, bool eraser, bool inContact)
    {
        ref var t = ref _info[0];
        t = default;
        t.type = PT_PEN;

        ref var pen = ref t.penInfo;
        pen.pointerInfo.pointerType = PT_PEN;
        pen.pointerInfo.pointerId = 0;
        pen.pointerInfo.pointerFlags = flags;
        pen.pointerInfo.ptPixelLocation = new POINT { X = x, Y = y };

        var pf = PenFlags.None;
        if (barrel) pf |= PenFlags.Barrel;
        if (eraser) pf |= PenFlags.Inverted;              // eraser end near the screen
        if (eraser && inContact) pf |= PenFlags.Eraser;   // eraser end touching
        pen.penFlags = pf;

        pen.penMask = PenMask.Pressure | PenMask.TiltX | PenMask.TiltY;
        pen.pressure = pressure;
        pen.tiltX = Math.Clamp(tiltX, -90, 90);
        pen.tiltY = Math.Clamp(tiltY, -90, 90);

        if (!InjectSyntheticPointerInput(_device, _info, 1))
            Debug.WriteLine($"InjectSyntheticPointerInput failed: error {Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        ReleaseAll();
        DestroySyntheticPointerDevice(_device);
    }
}
