using System.Net;
using System.Net.Sockets;
using SPenTablet.Host.Input;
using SPenTablet.Host.Mapping;

namespace SPenTablet.Host.Net;

public sealed class PenServer
{
    private readonly AreaMapper _mapper;
    private readonly Func<PressureCurve> _curve;
    private TcpListener? _listener;
    private volatile bool _running;

    public event Action<string>? Status;
    public bool IsConnected { get; private set; }

    public PenServer(AreaMapper mapper, Func<PressureCurve> curve)
    {
        _mapper = mapper;
        _curve = curve;
    }

    public void Start()
    {
        _running = true;
        new Thread(Run) { IsBackground = true, Name = "PenServer", Priority = ThreadPriority.Highest }.Start();
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
    }

    private void Run()
    {
        using var pen = new PenInjector();
        // Loopback only: nothing on your network can connect. adb reverse arrives as loopback.
        _listener = new TcpListener(IPAddress.Loopback, Protocol.Port);
        _listener.Start();
        var buf = new byte[Protocol.Size];

        while (_running)
        {
            Status?.Invoke("Waiting for phone…");
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (SocketException) { break; } // listener stopped

            try { HandleClient(client, pen, buf); }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException) { }
            finally
            {
                pen.ReleaseAll(); // never leave the pen stuck down
                IsConnected = false;
                client.Dispose();
            }
        }
    }

    private void HandleClient(TcpClient client, PenInjector pen, byte[] buf)
    {
        client.NoDelay = true;
        client.ReceiveTimeout = 3000; // phone pings every 0.5 s; 3 s of silence = cable gone
        var stream = client.GetStream();

        stream.ReadExactly(buf);
        var hello = Protocol.ParseHello(buf);
        _mapper.SetPad(hello.Width, hello.Height);
        stream.Write(Protocol.HelloAck());

        IsConnected = true;
        Status?.Invoke($"Connected ✓ (pad {hello.Width}×{hello.Height})");

        while (_running)
        {
            stream.ReadExactly(buf);
            if (buf[0] != Protocol.TPen) continue; // ping or unknown: ignore

            var p = Protocol.ParsePen(buf);
            var (x, y) = _mapper.Map(p.X, p.Y);
            uint pressure = p.InContact ? _curve().ToWindows(p.Pressure) : 0;
            pen.Update(x, y, p.InRange, p.InContact, pressure, p.TiltX, p.TiltY, p.Barrel, p.Eraser);
        }
    }
}
