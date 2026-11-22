using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinDock;

internal sealed class DockWindow
{
    private const uint AppBarCallback = 0x8000 + 20;
    private const uint StatsMessage = 0x8000 + 21;
    private const uint ExitCommand = 1;
    private const uint SettingsCommand = 2;
    private const uint ABM_NEW = 0;
    private const uint ABM_REMOVE = 1;
    private const uint ABM_QUERYPOS = 2;
    private const uint ABM_SETPOS = 3;
    private const uint ABE_TOP = 1;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_DPICHANGED = 0x02E0;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;


    private static readonly WNDPROC WindowProc = OnMessage;

    private readonly SystemSampler _sampler = new();
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private GCHandle _self;
    private HWND _owner;
    private HWND _hwnd;
    private NotifyIcon? _tray;
    private HICON _trayIcon;
    private DockSettings _settings = DockSettings.Load();
    private HFONT _font;
    private HFONT _iconFont;
    private HBRUSH _background;
    private HBRUSH _border;
    private RECT _launcher;
    private RECT _power;
    private bool _settingsOpen;
    private uint _dpi = 96;
    private StatsSnapshot _stats;
    private bool _hasStats;
    private bool _appBarRegistered;

    public static void Run()
    {
        var window = new DockWindow();
        window.Loop();
    }

    private unsafe void Loop()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        PInvoke.SetProcessDpiAwarenessContext(new DPI_AWARENESS_CONTEXT(-4));
        ApplyColors();
        var module = PInvoke.GetModuleHandle(default(PCWSTR));
        var instance = new HINSTANCE(module.Value);
        var cursor = PInvoke.LoadCursor(default, (char*)32512);
        _trayIcon = CreateTrayIcon();

        fixed (char* className = "WinDockBar")
        {
            var klass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                lpfnWndProc = WindowProc,
                hInstance = instance,
                hIcon = _trayIcon,
                hCursor = cursor,
                hbrBackground = _background,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(in klass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()}).");
            }

            _self = GCHandle.Alloc(this);
            fixed (char* ownerTitle = "win-dock-owner")
            {
                // A hidden tool window owns the dock so the shell does not give it a taskbar button.
                _owner = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                    className,
                    ownerTitle,
                    WINDOW_STYLE.WS_POPUP,
                    0,
                    0,
                    0,
                    0,
                    default,
                    default,
                    instance,
                    null);
            }

            fixed (char* title = "win-dock")
            {
                _hwnd = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOPMOST,
                    className,
                    title,
                    WINDOW_STYLE.WS_POPUP,
                    0,
                    0,
                    100,
                    34,
                    _owner,
                    default,
                    instance,
                    null);
            }
        }

        if (_hwnd == default)
        {
            _self.Free();
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
        }

        PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        RegisterAppBar();
        Position();
        BarChrome.Apply(_hwnd, _settings);
        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOW);
        AddTrayIcon();
        _timer = new System.Threading.Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        while (PInvoke.GetMessage(out var message, default, 0, 0))
        {
            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }

        Cleanup();
        fixed (char* className = "WinDockBar")
        {
            PInvoke.UnregisterClass(className, instance);
        }
    }

    private void OnTick(object? state)
    {
        var snapshot = _sampler.Sample();
        lock (_gate)
        {
            _stats = snapshot;
            _hasStats = true;
        }

        if (_hwnd != default)
        {
            PInvoke.PostMessage(_hwnd, StatsMessage, default, default);
        }
    }

    private void RegisterAppBar()
    {
        var data = NewAppBarData();
        PInvoke.SHAppBarMessage(ABM_NEW, ref data);
        _appBarRegistered = true;
    }

    private bool _positioning;
    private RECT _lastRequested;

    private void Position()
    {
        if (_positioning)
        {
            return;
        }

        _positioning = true;
        try
        {
            PlaceOnMonitor();
        }
        finally
        {
            _positioning = false;
        }
    }

    private void PlaceOnMonitor()
    {
        var monitor = PInvoke.MonitorFromWindow(_hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        _dpi = PInvoke.GetDpiForWindow(_hwnd);
        if (_dpi == 0)
        {
            _dpi = 96;
        }

        var vertical = _settings.Edge is DockEdge.Left or DockEdge.Right;
        var breadth = ContentBreadth(vertical);
        var gap = _settings.Floating ? Scale(_settings.Gap) : 0;
        var thickness = breadth + (gap * 2);
        var data = NewAppBarData();
        data.uEdge = _settings.Edge switch
        {
            DockEdge.Left => 0,
            DockEdge.Right => 2,
            DockEdge.Bottom => 3,
            _ => ABE_TOP,
        };
        data.rc = info.rcMonitor;
        switch (_settings.Edge)
        {
            case DockEdge.Left:
                data.rc.right = data.rc.left + thickness;
                break;
            case DockEdge.Right:
                data.rc.left = data.rc.right - thickness;
                break;
            case DockEdge.Bottom:
                data.rc.top = data.rc.bottom - thickness;
                break;
            default:
                data.rc.bottom = data.rc.top + thickness;
                break;
        }

        PInvoke.SHAppBarMessage(ABM_QUERYPOS, ref data);
        switch (_settings.Edge)
        {
            case DockEdge.Left:
                data.rc.right = data.rc.left + thickness;
                break;
            case DockEdge.Right:
                data.rc.left = data.rc.right - thickness;
                break;
            case DockEdge.Bottom:
                data.rc.top = data.rc.bottom - thickness;
                break;
            default:
                data.rc.bottom = data.rc.top + thickness;
                break;
        }

        if (data.rc.right <= data.rc.left || data.rc.bottom <= data.rc.top)
        {
            return;
        }

        if (data.rc.left == _lastRequested.left
            && data.rc.top == _lastRequested.top
            && data.rc.right == _lastRequested.right
            && data.rc.bottom == _lastRequested.bottom)
        {
            return;
        }

        _lastRequested = data.rc;
        PInvoke.SHAppBarMessage(ABM_SETPOS, ref data);
        var windowX = data.rc.left + (_settings.Edge == DockEdge.Right ? gap + (data.rc.right - data.rc.left - thickness) : gap);
        var windowY = data.rc.top + (_settings.Edge == DockEdge.Bottom ? gap + (data.rc.bottom - data.rc.top - thickness) : gap);
        var windowWidth = vertical
            ? breadth
            : Math.Max(1, data.rc.right - data.rc.left - (gap * 2));
        var windowHeight = vertical
            ? Math.Max(1, data.rc.bottom - data.rc.top - (gap * 2))
            : breadth;
        if (_settings.Edge == DockEdge.Right)
        {
            windowX = data.rc.right - gap - breadth;
        }

        if (_settings.Edge == DockEdge.Bottom)
        {
            windowY = data.rc.bottom - gap - breadth;
        }

        windowWidth = Math.Max(1, windowWidth);
        windowHeight = Math.Max(1, windowHeight);
        PInvoke.SetWindowPos(
            _hwnd,
            new HWND(new IntPtr(-1)),
            windowX,
            windowY,
            windowWidth,
            windowHeight,
            SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        // SetWindowPos can shrink the reserved strip to the window and eat the gap under it.
        PInvoke.SHAppBarMessage(ABM_SETPOS, ref data);
        UpdateFont();
        BarChrome.Apply(_hwnd, _settings);
        BarChrome.ApplyShape(_hwnd, windowWidth, windowHeight, _settings.Floating, Scale(12));
    }

    private APPBARDATA NewAppBarData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
        uCallbackMessage = AppBarCallback,
    };

    private unsafe void UpdateFont()
    {
        var clear = BarChrome.IsClearGlass(_settings);
        ReplaceFont(ref _font, _settings.FontFamily, _settings.FontSize, clear ? 600 : 400, clear ? 4 : 5);
        ReplaceFont(ref _iconFont, "Segoe MDL2 Assets", _settings.FontSize + 2, 600, clear ? 4 : 5);
    }

    private unsafe void ReplaceFont(ref HFONT font, string family, int pointSize, int weight, int quality)
    {
        if (font != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(font.Value));
        }

        var height = -(int)Math.Round(pointSize * _dpi / 72d);
        fixed (char* face = family)
        {
            font = PInvoke.CreateFont(height, 0, 0, 0, weight, 0, 0, 0, 0, 0, 0, (uint)quality, 0, face);
        }
    }

    private unsafe void ApplyColors()
    {
        if (_background != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_background.Value));
        }

        if (_border != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_border.Value));
        }

        var palette = _settings.Palette();
        _background = PInvoke.CreateSolidBrush((COLORREF)palette.Background);
        _border = PInvoke.CreateSolidBrush((COLORREF)palette.Border);
    }

    private int ContentBreadth(bool vertical)
    {
        var modules = CurrentModules();
        if (!vertical)
        {
            return Scale(_settings.FontSize + 22);
        }

        using var font = new Font(_settings.FontFamily, _settings.FontSize, FontStyle.Regular, GraphicsUnit.Point);
        var widest = Scale(160);
        foreach (var module in modules)
        {
            widest = Math.Max(widest, TextRenderer.MeasureText(module.Combined, font).Width + Scale(_settings.FontSize + 28));
        }

        return widest;
    }

    private int Scale(int dip) => (int)Math.Round(dip * (_dpi / 96d));

    private unsafe void Paint()
    {
        var screen = PInvoke.BeginPaint(_hwnd, out var paint);
        PInvoke.GetClientRect(_hwnd, out var client);
        var width = Math.Max(1, client.right - client.left);
        var height = Math.Max(1, client.bottom - client.top);
        var taskbarClear = BarChrome.IsClearGlass(_settings);
        var memory = PInvoke.CreateCompatibleDC(screen);
        void* bits = null;
        HGDIOBJ bitmap;
        if (taskbarClear)
        {
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = default;
            var dib = PInvoke.CreateDIBSection(default, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
            bitmap = new HGDIOBJ(dib.Value);
        }
        else
        {
            bitmap = PInvoke.CreateCompatibleBitmap(screen, width, height);
        }

        var previousBitmap = PInvoke.SelectObject(memory, bitmap);
        if (taskbarClear && bits != null)
        {
            FillTaskbarTint((uint*)bits, width * height);
        }
        else if (_settings.Blur == "None" && !_settings.CustomizeBlur)
        {
            var full = new RECT { left = 0, top = 0, right = width, bottom = height };
            PInvoke.FillRect(memory, &full, _background);
        }

        var previousFont = _font == default ? default : PInvoke.SelectObject(memory, _font);

        PInvoke.SetBkMode(memory, BACKGROUND_MODE.TRANSPARENT);
        var modules = CurrentModules();
        var vertical = _settings.Edge is DockEdge.Left or DockEdge.Right;
        var margin = Scale(8);
        var button = Math.Max(Scale(18), vertical ? Math.Min(Scale(22), width - (margin * 2)) : Math.Min(Scale(22), height - (margin * 2)));
        if (vertical)
        {
            DrawVertical(modules, width, height, button, margin);
        }
        else
        {
            DrawHorizontal(modules, width, height, button, margin);
        }

        if (taskbarClear && bits != null)
        {
            SealTaskbarTint((uint*)bits, width * height, BarChrome.TaskbarAlpha(_settings));
            BarChrome.PresentTint(_hwnd, (IntPtr)memory.Value, width, height);
        }
        else
        {
            PInvoke.BitBlt(screen, 0, 0, width, height, memory, 0, 0, ROP_CODE.SRCCOPY);
        }

        if (previousFont != default)
        {
            PInvoke.SelectObject(memory, previousFont);
        }

        PInvoke.SelectObject(memory, previousBitmap);
        PInvoke.DeleteObject(bitmap);
        PInvoke.DeleteDC(memory);
        PInvoke.EndPaint(_hwnd, paint);
        return;

        int Draw(string text, ref RECT rect, DRAW_TEXT_FORMAT format)
        {
            fixed (char* chars = text)
            {
                return PInvoke.DrawText(memory, chars, -1, ref rect, format);
            }
        }

        void DrawGlyph(RECT bounds, string glyph, uint color)
        {
            var previous = _iconFont == default ? default : PInvoke.SelectObject(memory, _iconFont);
            PInvoke.SetTextColor(memory, (COLORREF)Visible(color));
            Draw(glyph, ref bounds, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            if (previous != default)
            {
                PInvoke.SelectObject(memory, previous);
            }
        }

        void DrawHorizontal(IReadOnlyList<ModuleView> modules, int barWidth, int barHeight, int button, int margin)
        {
            var gap = Scale(_settings.Spacing);
            var buttonGap = Scale(12);
            var widths = modules.Select(module => Measure(module, barHeight)).ToArray();
            var modulesWidth = widths.Sum() + (gap * Math.Max(0, widths.Length - 1));
            var total = button + buttonGap + modulesWidth + buttonGap + button;
            var x = _settings.Align switch
            {
                ModuleAlign.Right => barWidth - margin - total,
                ModuleAlign.Center => Math.Max(margin, (barWidth - total) / 2),
                _ => margin,
            };
            var buttonTop = Math.Max(0, (barHeight - button) / 2);
            _launcher = new RECT { left = x, top = buttonTop, right = x + button, bottom = buttonTop + button };
            DrawGlyph(_launcher, StatText.IconLauncher, _settings.Palette().Accent);
            x += button + buttonGap;
            for (var index = 0; index < modules.Count; index++)
            {
                var rect = new RECT { left = x, top = 0, right = x + widths[index], bottom = barHeight };
                DrawModule(modules[index], rect, horizontal: true);
                x += widths[index] + gap;
            }

            x = x - gap + buttonGap;
            _power = new RECT { left = x, top = buttonTop, right = x + button, bottom = buttonTop + button };
            DrawGlyph(_power, StatText.IconPower, _settings.Palette().Accent);
        }

        void DrawVertical(IReadOnlyList<ModuleView> modules, int barWidth, int barHeight, int button, int margin)
        {
            var row = Scale(_settings.FontSize + 14);
            var gap = Scale(_settings.Spacing);
            var buttonGap = Scale(12);
            var modulesHeight = (row * modules.Count) + (gap * Math.Max(0, modules.Count - 1));
            var total = button + buttonGap + modulesHeight + buttonGap + button;
            var y = _settings.Align switch
            {
                ModuleAlign.Right => barHeight - margin - total,
                ModuleAlign.Center => Math.Max(margin, (barHeight - total) / 2),
                _ => margin,
            };
            var buttonLeft = Math.Max(0, (barWidth - button) / 2);
            _launcher = new RECT { left = buttonLeft, top = y, right = buttonLeft + button, bottom = y + button };
            DrawGlyph(_launcher, StatText.IconLauncher, _settings.Palette().Accent);
            y += button + buttonGap;
            foreach (var module in modules)
            {
                var rect = new RECT { left = Scale(8), top = y, right = barWidth - Scale(8), bottom = y + row };
                DrawModule(module, rect, horizontal: false);
                y += row + gap;
            }

            y = y - gap + buttonGap;
            _power = new RECT { left = buttonLeft, top = y, right = buttonLeft + button, bottom = y + button };
            DrawGlyph(_power, StatText.IconPower, _settings.Palette().Accent);
        }

        void DrawModule(ModuleView module, RECT bounds, bool horizontal)
        {
            var iconWidth = Scale(_settings.FontSize + 12);
            var iconGap = Scale(_settings.Spacing);
            var icon = new RECT { left = bounds.left, top = bounds.top, right = bounds.left + iconWidth, bottom = bounds.bottom };
            DrawGlyph(icon, module.Icon, Visible(_settings.Palette().Accent));
            var text = new RECT { left = icon.right + iconGap, top = bounds.top, right = bounds.right, bottom = bounds.bottom };
            var previous = _font == default ? default : PInvoke.SelectObject(memory, _font);
            PInvoke.SetTextColor(memory, (COLORREF)Visible(module.ColorRef));
            var format = DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_NOPREFIX | DRAW_TEXT_FORMAT.DT_END_ELLIPSIS;
            if (!horizontal)
            {
                format |= DRAW_TEXT_FORMAT.DT_LEFT;
            }

            Draw(module.Text, ref text, format);
            if (module.ExtraText.Length > 0)
            {
                var dateWidth = TextWidth(module.Text);
                var extraIcon = new RECT
                {
                    left = text.left + dateWidth + iconGap,
                    top = bounds.top,
                    right = text.left + dateWidth + iconGap + iconWidth,
                    bottom = bounds.bottom,
                };
                DrawGlyph(extraIcon, module.ExtraIcon, Visible(_settings.Palette().Accent));
                var timeWidth = TextWidth(module.ExtraText) + Scale(6);
                var extra = new RECT
                {
                    left = extraIcon.right + iconGap,
                    top = bounds.top,
                    right = extraIcon.right + iconGap + timeWidth,
                    bottom = bounds.bottom,
                };
                var extraFont = _font == default ? default : PInvoke.SelectObject(memory, _font);
                PInvoke.SetTextColor(memory, (COLORREF)Visible(module.ColorRef));
                var timeFormat = DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_NOPREFIX | DRAW_TEXT_FORMAT.DT_LEFT;
                Draw(module.ExtraText, ref extra, timeFormat);
                if (extraFont != default)
                {
                    PInvoke.SelectObject(memory, extraFont);
                }
            }

            if (previous != default)
            {
                PInvoke.SelectObject(memory, previous);
            }
        }

        int TextWidth(string text)
        {
            var rect = new RECT { left = 0, top = 0, right = 0, bottom = 0 };
            Draw(text, ref rect, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            return Math.Max(1, rect.right - rect.left);
        }

        uint Visible(uint color) => (color & 0x00FFFFFF) == 0 ? 0x00101010 : color;

        int Measure(ModuleView module, int barHeight)
        {
            var rect = new RECT { left = 0, top = 0, right = 0, bottom = barHeight };
            var previous = _font == default ? default : PInvoke.SelectObject(memory, _font);
            Draw(module.Text, ref rect, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            var extra = 0;
            if (module.ExtraText.Length > 0)
            {
                var extraRect = new RECT { left = 0, top = 0, right = 0, bottom = barHeight };
                Draw(module.ExtraText, ref extraRect, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);
                extra = Scale(_settings.Spacing) + Scale(_settings.FontSize + 12) + Math.Max(1, extraRect.right - extraRect.left) + Scale(6);
            }

            if (previous != default)
            {
                PInvoke.SelectObject(memory, previous);
            }

            return Math.Max(1, rect.right - rect.left) + Scale(_settings.FontSize + 12) + Scale(_settings.Spacing) + extra + Scale(4);
        }
    }

    private static unsafe void FillTaskbarTint(uint* pixels, int count)
    {
        // Straight #2D353B so glyph antialiasing blends against the tint, not black.
        var fill = 0x3Bu | (0x35u << 8) | (0x2Du << 16);
        for (var index = 0; index < count; index++)
        {
            pixels[index] = fill;
        }
    }

    private static unsafe void SealTaskbarTint(uint* pixels, int count, byte alpha)
    {
        var background = 0x3Bu | (0x35u << 8) | (0x2Du << 16);
        var blue = (uint)(0x3B * alpha / 255);
        var green = (uint)(0x35 * alpha / 255);
        var red = (uint)(0x2D * alpha / 255);
        var tint = blue | (green << 8) | (red << 16) | ((uint)alpha << 24);
        for (var index = 0; index < count; index++)
        {
            var pixel = pixels[index];
            if ((pixel & 0x00FFFFFFu) == background)
            {
                pixels[index] = tint;
                continue;
            }

            var blueByte = pixel & 0xFFu;
            var greenByte = (pixel >> 8) & 0xFFu;
            var redByte = (pixel >> 16) & 0xFFu;
            pixels[index] = blueByte | (greenByte << 8) | (redByte << 16) | 0xFF000000u;
        }
    }

    private List<ModuleView> CurrentModules()
    {
        StatsSnapshot stats;
        var ready = false;
        lock (_gate)
        {
            stats = _stats;
            ready = _hasStats;
        }

        if (!ready)
        {
            return [new ModuleView("wait", StatText.IconClock, "collecting…", _settings.Palette().Muted)];
        }

        return StatText.Build(stats, _settings, DateTime.Now).ToList();
    }

    private unsafe void ShowMenu()
    {
        var menu = PInvoke.CreatePopupMenu();
        fixed (char* settings = "Settings")
        fixed (char* label = "Exit")
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, SettingsCommand, settings);
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, ExitCommand, label);
        }
        PInvoke.GetCursorPos(out var point);
        PInvoke.SetForegroundWindow(_hwnd);
        var command = PInvoke.TrackPopupMenu(
            menu,
            TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD,
            point.X,
            point.Y,
            0,
            _hwnd,
            default);
        PInvoke.DestroyMenu(menu);
        if ((uint)command.Value == SettingsCommand)
        {
            OpenSettings();
        }
        else if ((uint)command.Value == ExitCommand)
        {
            PInvoke.PostMessage(_hwnd, WM_CLOSE, default, default);
        }
    }

    private void Click(LPARAM packed, bool right)
    {
        var value = (nint)packed;
        var point = new Point(unchecked((short)value), unchecked((short)(value >> 16)));
        var onLauncher = point.X >= _launcher.left && point.X < _launcher.right && point.Y >= _launcher.top && point.Y < _launcher.bottom;
        var onPower = point.X >= _power.left && point.X < _power.right && point.Y >= _power.top && point.Y < _power.bottom;
        if (right)
        {
            if (onLauncher)
            {
                OpenSettings();
            }
            else
            {
                ShowMenu();
            }

            return;
        }

        if (onLauncher)
        {
            BarChrome.OpenFlowLauncher();
        }
        else if (onPower)
        {
            try
            {
                BarChrome.ShowShutdown();
            }
            catch
            {
                // The shutdown dialog must not tear the dock down with it.
            }
        }
    }

    private void OpenSettings()
    {
        if (_settingsOpen)
        {
            return;
        }

        _settingsOpen = true;
        try
        {
            using var form = new SettingsForm(_settings);
            if (form.ShowDialog(new DockOwner(_hwnd)) != DialogResult.OK)
            {
                return;
            }

            _settings = form.Settings;
            _settings.Save();
            _lastRequested = default;
            ApplyColors();
            UpdateFont();
            Position();
            BarChrome.Apply(_hwnd, _settings);
            PInvoke.InvalidateRect(_hwnd, default, false);
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    private static LRESULT OnMessage(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var pointer = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
        if (pointer == 0)
        {
            return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }

        var window = (DockWindow)GCHandle.FromIntPtr(pointer).Target!;
        switch (message)
        {
            case WM_ERASEBKGND:
                return new LRESULT(1);
            case WM_PAINT:
                window.Paint();
                return default;
            case WM_LBUTTONUP:
                window.Click(lParam, false);
                return default;
            case WM_RBUTTONDOWN:
                return default;
            case WM_RBUTTONUP:
                window.Click(lParam, true);
                return default;
            case WM_CONTEXTMENU:
                return default;
            case StatsMessage:
            case WM_DISPLAYCHANGE:
            case WM_DPICHANGED:
            case WM_SETTINGCHANGE:
                if (message != StatsMessage)
                {
                    window.Position();
                }

                PInvoke.InvalidateRect(hwnd, default, false);
                return default;
            case AppBarCallback:
                if ((nuint)wParam == 1)
                {
                    window.Position();
                }

                return default;
            case WM_CLOSE:
                PInvoke.DestroyWindow(hwnd);
                return default;
            case WM_DESTROY:
                window.ReleaseAppBar();
                window.ReleaseTray();
                PInvoke.PostQuitMessage(0);
                return default;
            default:
                return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void ReleaseAppBar()
    {
        if (!_appBarRegistered)
        {
            return;
        }

        var data = NewAppBarData();
        PInvoke.SHAppBarMessage(ABM_REMOVE, ref data);
        _appBarRegistered = false;
    }

    private unsafe void AddTrayIcon()
    {
        _tray = new NotifyIcon
        {
            Icon = Icon.FromHandle((nint)_trayIcon.Value),
            Text = "win-dock",
            Visible = true,
        };
        _tray.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                ShowMenu();
            }
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    private void ReleaseTray()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        if (_trayIcon != default)
        {
            PInvoke.DestroyIcon(_trayIcon);
            _trayIcon = default;
        }
    }

    private static HICON CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(255, 0x2D, 0x35, 0x3B));
            using var edge = new Pen(Color.FromArgb(255, 0xD3, 0xC6, 0xAA), 1.6f);
            using var mark = new SolidBrush(Color.FromArgb(255, 0xE8, 0xC8, 0x7A));
            graphics.FillEllipse(fill, 1, 1, 30, 30);
            graphics.DrawEllipse(edge, 2, 2, 28, 28);
            graphics.FillRectangle(mark, 8, 16, 3, 8);
            graphics.FillRectangle(mark, 13, 12, 3, 12);
            graphics.FillRectangle(mark, 18, 14, 3, 10);
            graphics.FillRectangle(mark, 23, 9, 3, 15);
        }

        return new HICON(bitmap.GetHicon());
    }

    private unsafe void Cleanup()
    {
        ReleaseTray();
        if (_owner != default)
        {
            PInvoke.DestroyWindow(_owner);
            _owner = default;
        }

        _timer?.Dispose();
        _timer = null;
        _sampler.Dispose();
        if (_font != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_font.Value));
            _font = default;
        }

        if (_iconFont != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_iconFont.Value));
            _iconFont = default;
        }

        if (_background != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_background.Value));
            _background = default;
        }

        if (_border != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_border.Value));
            _border = default;
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }
}
