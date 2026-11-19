namespace SPenTablet.Host.Mapping;

public sealed record PressureCurve(float Gamma = 1.0f, float MinPressure = 0.02f)
{
    public static readonly PressureCurve Soft = new(0.6f);
    public static readonly PressureCurve Normal = new(1.0f);
    public static readonly PressureCurve Firm = new(1.6f);

    /// <summary>Phone pressure 0..1 → Windows pressure 1..1024 (only called while touching).</summary>
    public uint ToWindows(float p)
    {
        float t = Math.Clamp((p - MinPressure) / (1f - MinPressure), 0f, 1f);
        float v = MathF.Pow(t, Gamma);
        return (uint)Math.Clamp(MathF.Round(v * 1024f), 1f, 1024f); // never 0 while touching
    }
}
