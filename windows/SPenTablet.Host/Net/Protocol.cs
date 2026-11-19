using System.Buffers.Binary;

namespace SPenTablet.Host.Net;

public static class Protocol
{
    public const int Port = 47800;
    public const int Size = 24;
    public const uint Magic = 0x42545053; // "SPTB"

    public const byte THello = 0x01, TPen = 0x02, TPing = 0x04, THelloAck = 0x81;

    [Flags] public enum PenBits : byte { InRange = 1, Contact = 2, Barrel = 4, Eraser = 8 }

    public readonly record struct Hello(int Version, int Width, int Height);

    public readonly record struct Pen(PenBits Flags, ushort Seq, float X, float Y, float Pressure,
                                      sbyte TiltX, sbyte TiltY, uint TimeMs)
    {
        public bool InRange => Flags.HasFlag(PenBits.InRange);
        public bool InContact => Flags.HasFlag(PenBits.Contact);
        public bool Barrel => Flags.HasFlag(PenBits.Barrel);
        public bool Eraser => Flags.HasFlag(PenBits.Eraser);
    }

    public static Hello ParseHello(ReadOnlySpan<byte> b)
    {
        if (b[0] != THello || BinaryPrimitives.ReadUInt32LittleEndian(b[4..]) != Magic)
            throw new InvalidDataException("Not the S Pen pad app");
        return new Hello(b[1],
            BinaryPrimitives.ReadInt32LittleEndian(b[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(b[12..]));
    }

    public static Pen ParsePen(ReadOnlySpan<byte> b) => new(
        (PenBits)b[1],
        BinaryPrimitives.ReadUInt16LittleEndian(b[2..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[8..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[12..]),
        (sbyte)b[16],
        (sbyte)b[17],
        BinaryPrimitives.ReadUInt32LittleEndian(b[20..]));

    public static byte[] HelloAck()
    {
        var a = new byte[Size];
        a[0] = THelloAck;
        a[1] = 1;
        return a;
    }
}
