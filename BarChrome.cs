using System.Runtime.InteropServices;

namespace WinDock;

internal static class BarChrome
{
    private const int AccentDisabled = 0;
    private const int AccentClear = 2;
    private const int AccentBlur = 3;
    private const int AccentAcrylic = 4;
    private const int AccentHostBackdrop = 5;
    private const int WcaAccentPolicy = 19;
    private const int DwmBackdrop = 38;
    private const int DwmCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmNcRenderingPolicy = 2;
    private const int CornerSquare = 1;
    private const int CornerRound = 2;
    private const uint LwaAlpha = 0x2;
    private const int GwlExstyle = -20;
    private const long WsExLayered = 0x00080000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static bool IsClearGlass(DockSettings settings)
    {
        if (settings.CustomizeBlur)
        {
            return settings.BlurBehind || settings.AcrylicTint;
        }

        return settings.Blur is "Blur" or "Glass" or "Acrylic";
    }

    /// <summary>0 is milky, 100 is the clearest glass. Windows has no blur-radius control, so this is the frost strength.</summary>
    public static int TintAlpha(int blurriness)
    {
        var amount = Math.Clamp(blurriness, 0, 100);
        return 230 - (int)Math.Round(amount / 100d * 200);
    }

    public static void Apply(IntPtr hwnd, DockSettings settings)
    {
        var blur = settings.CustomizeBlur ? "Custom" : settings.Blur;
        var taskbarClear = IsClearGlass(settings);
        var backdrop = taskbarClear || blur != "Mica" ? 1 : 2;
        DwmSetWindowAttribute(hwnd, DwmBackdrop, ref backdrop, sizeof(int));
        var corners = settings.Floating ? CornerRound : CornerSquare;
        DwmSetWindowAttribute(hwnd, DwmCornerPreference, ref corners, sizeof(int));
        var border = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(int));
        var noFrame = 1;
        DwmSetWindowAttribute(hwnd, DwmNcRenderingPolicy, ref noFrame, sizeof(int));

        // The clear look is drawn as premultiplied #2D353B66. An accent policy on a
        // per-pixel layered window replaces that bitmap, so the accent stays off here.
        var accent = taskbarClear ? AccentDisabled : blur switch
        {
            "Acrylic" => AccentAcrylic,
            "Mica" => AccentHostBackdrop,
            _ => AccentDisabled,
        };
        var gradient = (uint)settings.Palette().Background;
        var policy = new AccentPolicy
        {
            AccentState = accent,
            AccentFlags = 0,
            GradientColor = gradient,
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = pointer,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        var style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        // Per-pixel alpha carries the #2D353B66 tint. Color-key layering is not used:
        // those pixels are holes, so a right-click falls through to the desktop.
        var perPixel = taskbarClear;
        var uniform = !perPixel && blur == "None" && settings.Opacity < 255;
        style = perPixel || uniform ? style | WsExLayered : style & ~WsExLayered;
        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style));
        if (uniform)
        {
            SetLayeredWindowAttributes(hwnd, 0, (byte)settings.Opacity, LwaAlpha);
        }

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>TranslucentTB clear color #2D353B66. Blur amount 100 maps to alpha 0x66.</summary>
    public static byte TaskbarAlpha(DockSettings settings)
    {
        var scaled = (int)Math.Round(0x66 * (Math.Clamp(settings.BlurAmount, 0, 100) / 100d));
        return (byte)Math.Max(1, scaled);
    }

    public static void PresentTint(IntPtr hwnd, IntPtr source, int width, int height)
    {
        GetWindowRect(hwnd, out var rect);
        var origin = new Point();
        var dest = new Point(rect.Left, rect.Top);
        var size = new Size(width, height);
        var blend = new BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,
        };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dest, ref size, source, ref origin, 0, ref blend, 2);
    }

    public static void ApplyShape(IntPtr hwnd, int width, int height, bool floating, int radius)
    {
        if (!floating || radius <= 0 || width <= 0 || height <= 0)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        SetWindowRgn(hwnd, region, true);
    }

    public static void ShowShutdown()
    {
        // ExitWindowsDialog is not exported from user32 on this Windows build.
        // Calling it threw inside the dock window procedure and closed the process.
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return;
        }

        var shell = Activator.CreateInstance(shellType);
        shellType.InvokeMember("ShutdownWindows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
    }

    public static void OpenFlowLauncher()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowLauncher",
            "Flow.Launcher.exe");
        if (!File.Exists(path))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr destDc,
        ref Point dest,
        ref Size size,
        IntPtr sourceDc,
        ref Point origin,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width;
        public int Height;
        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
