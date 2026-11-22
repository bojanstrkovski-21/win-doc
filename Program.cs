using Microsoft.Win32;

namespace WinDock;

internal static class Program
{
    private const string StartupValueName = "win-dock";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--dump"))
        {
            return Dump();
        }

        EnsureStartup();
        using var mutex = new Mutex(true, "Local\\win-dock", out var created);
        if (!created)
        {
            return 0;
        }

        DockWindow.Run();
        return 0;
    }

    private static void EnsureStartup()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var command = $"\"{exe}\"";
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null)
        {
            return;
        }

        if (key.GetValue(StartupValueName) is string current
            && string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        key.SetValue(StartupValueName, command);
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
