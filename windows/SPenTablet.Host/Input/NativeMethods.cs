using System.Runtime.InteropServices;

namespace SPenTablet.Host.Input;

internal static class NativeMethods
{
    public const uint PT_PEN = 3;
    public const int POINTER_FEEDBACK_DEFAULT = 1; // 3 = NONE (no ripple/visual feedback)

    [Flags]
    public enum PointerFlags : uint
    {
        None = 0,
        New = 0x1,
        InRange = 0x2,
        InContact = 0x4,
        FirstButton = 0x10,
        SecondButton = 0x20,
        Primary = 0x2000,
        Confidence = 0x4000,
        Canceled = 0x8000,
        Down = 0x10000,
        Update = 0x20000,
        Up = 0x40000,
    }

    [Flags] public enum PenFlags : uint { None = 0, Barrel = 0x1, Inverted = 0x2, Eraser = 0x4 }
    [Flags] public enum PenMask : uint { None = 0, Pressure = 0x1, Rotation = 0x2, TiltX = 0x4, TiltY = 0x8 }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public PointerFlags pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public PenFlags penFlags;
        public PenMask penMask;
        public uint pressure;   // 0..1024
        public uint rotation;   // 0..359
        public int tiltX;       // -90..90
        public int tiltY;       // -90..90
    }

    // In C this is { type; union { touchInfo; penInfo; } }. The union is 8-byte aligned,
    // and its size is that of the larger touch struct, so the whole thing is 152 bytes on x64.
    [StructLayout(LayoutKind.Explicit, Size = 152)]
    public struct POINTER_TYPE_INFO
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public POINTER_PEN_INFO penInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, int mode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [In] POINTER_TYPE_INFO[] pointerInfo, uint count);

    [DllImport("user32.dll")]
    public static extern void DestroySyntheticPointerDevice(IntPtr device);
}
