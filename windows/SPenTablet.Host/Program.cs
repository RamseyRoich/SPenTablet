namespace SPenTablet.Host;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one copy may run (two would fight over port 47800).
        using var mutex = new Mutex(true, "SPenTablet.Host.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
