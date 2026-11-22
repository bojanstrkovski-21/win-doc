namespace WinDock;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--dump"))
        {
            return Dump();
        }

        using var mutex = new Mutex(true, "Local\\win-dock", out var created);
        if (!created)
        {
            return 0;
        }

        DockWindow.Run();
        return 0;
    }

    private static int Dump()
    {
        using var sampler = new SystemSampler();
        sampler.Sample();
        Thread.Sleep(1200);
        var line = StatText.FormatLine(sampler.Sample());
        Console.WriteLine(line);
        return 0;
    }
}
