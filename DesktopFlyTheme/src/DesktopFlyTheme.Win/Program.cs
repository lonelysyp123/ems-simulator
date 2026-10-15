using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopFlyTheme.Win;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        EnablePerMonitorV2();
        var host = new DesktopHost();
        return host.Run();
    }

    private static void EnablePerMonitorV2()
    {
        if (!NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4)))
            NativeMethods.SetProcessDPIAware();
    }
}

internal sealed class DesktopHost
{
    private readonly NativeMethods.WndProc _buttonProc;
    private readonly NativeMethods.WndProc _surfaceProc;
    private readonly ThemeLoop _world;
    private readonly HostPointer _pointer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IntPtr _buttonWindow;
    private IntPtr _surfaceWindow;
    private IntPtr _worker;
    private ButtonRects _buttons;
    private double _lastLayout = double.NegativeInfinity;
    private double _lastIconRead = double.NegativeInfinity;
    private double _lastDraw = double.NegativeInfinity;
    private double _lastCoverage = double.NegativeInfinity;
    private bool _desktopVisible = true;
    private bool _ragPainted;
    private bool _sceneStarted;
    private List<RectD> _icons = new();
    private int _armedButton = -1;

    public DesktopHost()
    {
        _buttonProc = ButtonProc;
        _surfaceProc = SurfaceProc;
        _world = new ThemeLoop(new RandomSource());
        _pointer.Host = this;
    }

    public int Run()
    {
        var instance = NativeMethods.GetModuleHandle(null);
        Register("DesktopFlyThemeButtons", _buttonProc, instance);
        Register("DesktopFlyThemeSurface", _surfaceProc, instance);
        _buttonWindow = NativeMethods.CreateWindowEx(
            NativeMethods.WsExNoActivate | NativeMethods.WsExTopmost | NativeMethods.WsExToolWindow | NativeMethods.WsExLayered,
            "DesktopFlyThemeButtons",
            string.Empty,
            NativeMethods.WsPopup | NativeMethods.WsVisible,
            0,
            0,
            10,
            10,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (_buttonWindow == IntPtr.Zero)
            return 1;

        TryAttachSurface(instance);
        ApplySpace();
        LayoutButtons(force: true);
        if (!StartSceneIfAttached())
        {
            // 找不到图标下层时不用全屏窗口代替。桌面保持原壁纸，只留按钮条。
            _world.SurfaceAvailable = false;
        }

        PaintButtons();
        NativeMethods.SetTimer(_buttonWindow, new IntPtr(1), 16, IntPtr.Zero);
        while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        return 0;
    }

    private void Tick()
    {
        if (_world.IsShutdown)
            return;

        var now = Now();
        if (now - _lastLayout >= 0.5)
            LayoutButtons(force: false);

        if (_surfaceWindow != IntPtr.Zero)
        {
            if (now - _lastIconRead >= 0.25)
            {
                _icons = ReadIcons();
                _lastIconRead = now;
            }

            _world.SubmitIcons(_icons);
            _pointer.Icons = _icons;
            _world.Step(now, _pointer);
            if (now - _lastCoverage >= 0.25)
            {
                _desktopVisible = QueryDesktopVisible();
                _lastCoverage = now;
            }

            var interval = _desktopVisible ? 1.0 / 60.0 : 0.1;
            if (now - _lastDraw >= interval)
            {
                PresentSurface();
                _lastDraw = now;
            }

            if (_ragPainted != _world.RagMode)
                PaintButtons();
        }
    }

    private bool StartSceneIfAttached()
    {
        if (_sceneStarted || _surfaceWindow == IntPtr.Zero)
            return _sceneStarted;

        _sceneStarted = true;
        _world.SurfaceAvailable = true;
        ApplySpace();
        _world.Reset(Now());
        PresentSurface();
        return true;
    }

    private void TryAttachSurface(IntPtr instance)
    {
        _worker = FindWorkerW();
        if (_worker == IntPtr.Zero || _surfaceWindow != IntPtr.Zero)
            return;

        var child = NativeMethods.CreateWindowEx(
            NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate,
            "DesktopFlyThemeSurface",
            string.Empty,
            NativeMethods.WsChild | NativeMethods.WsVisible,
            0,
            0,
            10,
            10,
            _worker,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (child == IntPtr.Zero)
        {
            // 创建失败也不改用全屏置顶窗口。
            _worker = IntPtr.Zero;
            return;
        }

        _surfaceWindow = child;
        PlaceSurface();
    }

    private void PlaceSurface()
    {
        if (_surfaceWindow == IntPtr.Zero || _worker == IntPtr.Zero)
            return;
        var vx = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var vy = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var vw = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var vh = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        var origin = new NativeMethods.Point { X = vx, Y = vy };
        NativeMethods.ScreenToClient(_worker, ref origin);
        NativeMethods.SetWindowPos(
            _surfaceWindow,
            IntPtr.Zero,
            origin.X,
            origin.Y,
            Math.Max(vw, 1),
            Math.Max(vh, 1),
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void PresentSurface()
    {
        if (_surfaceWindow == IntPtr.Zero)
            return;
        var vx = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var vy = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var vw = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var vh = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        if (vw <= 0 || vh <= 0 || vw > 20000 || vh > 20000)
            return;

        RasterFrame frame;
        try
        {
            frame = SoftwareRasterizer.Render(_world.Draw(), vw, vh, vx, vy);
        }
        catch (OutOfMemoryException)
        {
            return;
        }

        var hdc = NativeMethods.GetDC(_surfaceWindow);
        if (hdc == IntPtr.Zero)
            return;
        var info = new NativeMethods.BitmapInfo
        {
            Header = new NativeMethods.BitmapInfoHeader
            {
                BiSize = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                BiWidth = frame.Width,
                BiHeight = -frame.Height,
                BiPlanes = 1,
                BiBitCount = 32,
                BiCompression = 0,
            },
        };
        var pinned = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            NativeMethods.StretchDIBits(
                hdc,
                0,
                0,
                frame.Width,
                frame.Height,
                0,
                0,
                frame.Width,
                frame.Height,
                pinned.AddrOfPinnedObject(),
                ref info,
                0,
                NativeMethods.SrcCopy);
        }
        finally
        {
            pinned.Free();
            NativeMethods.ReleaseDC(_surfaceWindow, hdc);
        }
    }

    private void ApplySpace()
    {
        var works = new List<RectD>();
        var displays = new List<RectD>();
        var primary = new RectD(0, 0, 1920, 1080);
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfo { CbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;
            var display = ToRect(info.Monitor);
            var work = ToRect(info.Work);
            displays.Add(display);
            works.Add(work);
            if ((info.Flags & NativeMethods.MonitorInfoPrimary) != 0)
                primary = work;
            return true;
        }, IntPtr.Zero);

        if (works.Count == 0)
        {
            works.Add(primary);
            displays.Add(primary);
        }

        _world.ConfigureSpace(works, primary, displays);
    }

    private void LayoutButtons(bool force)
    {
        var now = Now();
        if (!force && now - _lastLayout < 0.5)
            return;
        _lastLayout = now;
        ApplySpace();
        var primary = PrimaryMonitor();
        var dpi = DpiOf(primary.Monitor);
        RectD? taskbar = null;
        var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect))
        {
            var trayBox = ToRect(trayRect);
            var monitor = ToRect(primary.Bounds);
            var onTop = trayBox.Top <= monitor.Top + 4
                && trayBox.Bottom < monitor.Top + monitor.Height * 0.5
                && trayBox.Right > monitor.Left
                && trayBox.Left < monitor.Right;
            if (onTop)
                taskbar = trayBox;
        }

        _buttons = ButtonLayout.Layout(ToRect(primary.Work), taskbar, dpi);
        var width = Math.Max(1, (int)Math.Ceiling(_buttons.Close.Right - _buttons.Initialize.Left));
        var height = Math.Max(1, (int)Math.Ceiling(_buttons.Initialize.Bottom - _buttons.Initialize.Top));
        NativeMethods.SetWindowPos(
            _buttonWindow,
            NativeMethods.HwndTopmost,
            (int)Math.Floor(_buttons.Initialize.Left),
            (int)Math.Floor(_buttons.Initialize.Top),
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _pointer.Buttons = new[] { _buttons.Initialize, _buttons.Rag, _buttons.Close };
        PaintButtons();
    }

    private void PaintButtons()
    {
        _ragPainted = _world.RagMode;
        var width = Math.Max(1, (int)Math.Ceiling(_buttons.Close.Right - _buttons.Initialize.Left));
        var height = Math.Max(1, (int)Math.Ceiling(_buttons.Initialize.Bottom - _buttons.Initialize.Top));
        var originX = _buttons.Initialize.Left;
        var originY = _buttons.Initialize.Top;
        var info = new NativeMethods.BitmapInfo
        {
            Header = new NativeMethods.BitmapInfoHeader
            {
                BiSize = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                BiWidth = width,
                BiHeight = -height,
                BiPlanes = 1,
                BiBitCount = 32,
                BiSizeImage = width * height * 4,
            },
        };
        var screen = NativeMethods.GetDC(IntPtr.Zero);
        var bits = IntPtr.Zero;
        var dib = NativeMethods.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
        var mem = NativeMethods.CreateCompatibleDC(screen);
        var old = NativeMethods.SelectObject(mem, dib);
        var font = NativeMethods.CreateFont(
            -Math.Max(1, (int)Math.Round(12.0 * DpiOf(PrimaryMonitor().Monitor) / 96.0)),
            0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Microsoft YaHei");
        var oldFont = NativeMethods.SelectObject(mem, font);
        NativeMethods.SetBkMode(mem, NativeMethods.Transparent);
        DrawButton(mem, _buttons.Initialize, originX, originY, "初始化", false);
        DrawButton(mem, _buttons.Rag, originX, originY, "抹布", _world.RagMode);
        DrawButton(mem, _buttons.Close, originX, originY, "关闭", false);
        ForceButtonAlpha(bits, width, height, originX, originY);

        var destination = new NativeMethods.Point
        {
            X = (int)Math.Floor(originX),
            Y = (int)Math.Floor(originY),
        };
        var size = new NativeMethods.Size { Cx = width, Cy = height };
        var source = new NativeMethods.Point();
        var blend = new NativeMethods.BlendFunction
        {
            BlendOp = NativeMethods.AcSrcOver,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AcSrcAlpha,
        };
        NativeMethods.UpdateLayeredWindow(_buttonWindow, screen, ref destination, ref size, mem, ref source, 0, ref blend, NativeMethods.UlwAlpha);
        NativeMethods.SelectObject(mem, oldFont);
        NativeMethods.SelectObject(mem, old);
        NativeMethods.DeleteObject(font);
        NativeMethods.DeleteObject(dib);
        NativeMethods.DeleteDC(mem);
        NativeMethods.ReleaseDC(IntPtr.Zero, screen);
    }

    private static void DrawButton(IntPtr hdc, RectD rect, double originX, double originY, string text, bool rag)
    {
        var box = new NativeMethods.Rect
        {
            Left = (int)Math.Round(rect.Left - originX),
            Top = (int)Math.Round(rect.Top - originY),
            Right = (int)Math.Round(rect.Right - originX),
            Bottom = (int)Math.Round(rect.Bottom - originY),
        };
        var color = rag ? ThemeRules.RagButtonFill : ThemeRules.ButtonFill;
        var brush = NativeMethods.CreateSolidBrush(NativeMethods.Rgb(color.R, color.G, color.B));
        NativeMethods.FillRect(hdc, ref box, brush);
        NativeMethods.DeleteObject(brush);
        var textColor = rag ? ThemeRules.RagButtonText : ThemeRules.ButtonText;
        NativeMethods.SetTextColor(hdc, NativeMethods.Rgb(textColor.R, textColor.G, textColor.B));
        NativeMethods.DrawText(hdc, text, -1, ref box, NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine);
    }

    private void ForceButtonAlpha(IntPtr bits, int width, int height, double originX, double originY)
    {
        if (bits == IntPtr.Zero)
            return;
        var count = width * height * 4;
        var buffer = new byte[count];
        Marshal.Copy(bits, buffer, 0, count);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                var point = new Vec2(originX + x + 0.5, originY + y + 0.5);
                if (_buttons.Hit(point) >= 0)
                {
                    buffer[index + 3] = 255;
                }
                else
                {
                    buffer[index] = 0;
                    buffer[index + 1] = 0;
                    buffer[index + 2] = 0;
                    buffer[index + 3] = 0;
                }
            }
        }

        Marshal.Copy(buffer, 0, bits, count);
    }

    private void CloseTheme()
    {
        _world.RequestClose();
        if (_surfaceWindow != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_surfaceWindow);
            _surfaceWindow = IntPtr.Zero;
        }

        if (_worker != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_worker, IntPtr.Zero, true);
            NativeMethods.RedrawWindow(
                _worker,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RdWInvalidate | NativeMethods.RdWErase | NativeMethods.RdWAllChildren);
            _worker = IntPtr.Zero;
        }

        NativeMethods.KillTimer(_buttonWindow, new IntPtr(1));
        NativeMethods.DestroyWindow(_buttonWindow);
    }

    private IntPtr ButtonProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmEraseBkgnd:
                return new IntPtr(1);
            case NativeMethods.WmTimer:
                Tick();
                return IntPtr.Zero;
            case NativeMethods.WmDisplayChange:
            case NativeMethods.WmDpiChanged:
                if (_surfaceWindow == IntPtr.Zero)
                    TryAttachSurface(NativeMethods.GetModuleHandle(null));
                PlaceSurface();
                StartSceneIfAttached();
                LayoutButtons(force: true);
                return IntPtr.Zero;
            case NativeMethods.WmMouseActivate:
                return new IntPtr(NativeMethods.MaNoActivate);
            case NativeMethods.WmNcHitTest:
                return new IntPtr(_buttons.Hit(new Vec2(NativeMethods.Lo(lParam), NativeMethods.Hi(lParam))) >= 0
                    ? NativeMethods.HtClient
                    : NativeMethods.HtTransparent);
            case NativeMethods.WmLButtonDown:
                _armedButton = HitClient(lParam);
                return IntPtr.Zero;
            case NativeMethods.WmLButtonUp:
                var up = HitClient(lParam);
                if (up >= 0 && up == _armedButton)
                    Activate(up);
                _armedButton = -1;
                return IntPtr.Zero;
            case NativeMethods.WmRButtonDown:
            case NativeMethods.WmRButtonUp:
                return IntPtr.Zero;
            case NativeMethods.WmDestroy:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private IntPtr SurfaceProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WmEraseBkgnd)
            return new IntPtr(1);
        if (msg == NativeMethods.WmPaint)
        {
            var paint = new NativeMethods.PaintStruct();
            NativeMethods.BeginPaint(hwnd, out paint);
            NativeMethods.EndPaint(hwnd, ref paint);
            PresentSurface();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Activate(int which)
    {
        var now = Now();
        if (which == 0)
            _world.RequestInitialize(now);
        else if (which == 1)
            _world.RequestToggleRag();
        else if (which == 2)
        {
            CloseTheme();
            return;
        }

        if (_surfaceWindow != IntPtr.Zero)
            PresentSurface();
        PaintButtons();
    }

    private int HitClient(IntPtr lParam)
    {
        var screen = new Vec2(_buttons.Initialize.Left + NativeMethods.Lo(lParam), _buttons.Initialize.Top + NativeMethods.Hi(lParam));
        return _buttons.Hit(screen);
    }

    private double Now() => _clock.Elapsed.TotalSeconds;

    private static void Register(string className, NativeMethods.WndProc proc, IntPtr instance)
    {
        var windowClass = new NativeMethods.WndClassEx
        {
            CbSize = Marshal.SizeOf<NativeMethods.WndClassEx>(),
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            HInstance = instance,
            HCursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            LpszClassName = className,
        };
        NativeMethods.RegisterClassEx(ref windowClass);
    }

    private static IntPtr FindWorkerW()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        NativeMethods.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        var worker = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var desktop = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (desktop != IntPtr.Zero)
                worker = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            return true;
        }, IntPtr.Zero);
        return worker;
    }

    private static List<RectD> ReadIcons()
    {
        var list = FindListView();
        var rects = new List<RectD>();
        if (list == IntPtr.Zero)
            return rects;

        var count = NativeMethods.SendMessage(list, NativeMethods.LvmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (count <= 0 || count > 4096)
            return rects;

        // 列表在 explorer 进程里。把 RECT 放到对方地址空间再读，避免把本进程指针交给 SysListView32。
        NativeMethods.GetWindowThreadProcessId(list, out var processId);
        var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessVmOperation | NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite,
            false,
            processId);
        if (process == IntPtr.Zero)
            return rects;

        var size = (uint)Marshal.SizeOf<NativeMethods.Rect>();
        var remote = NativeMethods.VirtualAllocEx(
            process,
            IntPtr.Zero,
            size,
            NativeMethods.MemCommit | NativeMethods.MemReserve,
            NativeMethods.PageReadWrite);
        if (remote == IntPtr.Zero)
        {
            NativeMethods.CloseHandle(process);
            return rects;
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                var rect = new NativeMethods.Rect { Left = 0 };
                if (!NativeMethods.WriteProcessMemory(process, remote, ref rect, size, out _))
                    continue;
                if (NativeMethods.SendMessage(list, NativeMethods.LvmGetItemRect, new IntPtr(i), remote) == IntPtr.Zero)
                    continue;
                if (!NativeMethods.ReadProcessMemory(process, remote, out rect, size, out _))
                    continue;

                var topLeft = new NativeMethods.Point { X = rect.Left, Y = rect.Top };
                var bottomRight = new NativeMethods.Point { X = rect.Right, Y = rect.Bottom };
                NativeMethods.ClientToScreen(list, ref topLeft);
                NativeMethods.ClientToScreen(list, ref bottomRight);
                rects.Add(new RectD(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y));
            }
        }
        finally
        {
            NativeMethods.VirtualFreeEx(process, remote, 0, NativeMethods.MemRelease);
            NativeMethods.CloseHandle(process);
        }

        return rects;
    }

    private static IntPtr FindListView()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        var defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var list = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        if (list != IntPtr.Zero)
            return list;

        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var shell = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell == IntPtr.Zero)
                return true;
            found = NativeMethods.FindWindowEx(shell, IntPtr.Zero, "SysListView32", null);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    private bool QueryDesktopVisible()
    {
        try
        {
            if (SessionLocked() || ScreenSaverRunning())
                return false;

            var monitors = new List<NativeMethods.Rect>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new NativeMethods.MonitorInfo { CbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
                if (NativeMethods.GetMonitorInfo(monitor, ref info))
                    monitors.Add(info.Monitor);
                return true;
            }, IntPtr.Zero);
            if (monitors.Count == 0)
                return true;

            foreach (var monitor in monitors)
            {
                if (!MonitorCovered(monitor))
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool MonitorCovered(NativeMethods.Rect monitor)
    {
        var covered = false;
        var decided = false;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || IsCloaked(hwnd))
                return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var rect) || !Contains(rect, monitor))
                return true;

            var name = NativeMethods.ClassName(hwnd);
            if (name is "Progman" or "WorkerW" || OwnsDesktopList(hwnd))
            {
                decided = true;
                covered = false;
                return false;
            }

            decided = true;
            covered = true;
            return false;
        }, IntPtr.Zero);
        return decided && covered;
    }

    private static bool OwnsDesktopList(IntPtr hwnd)
    {
        var shell = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
        return shell != IntPtr.Zero
            && NativeMethods.FindWindowEx(shell, IntPtr.Zero, "SysListView32", null) != IntPtr.Zero;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmWaCloaked, out var cloaked, sizeof(int)) == 0
            && cloaked != 0;
    }

    private static bool SessionLocked()
    {
        var desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DesktopReadObjects);
        if (desktop == IntPtr.Zero)
            return Marshal.GetLastWin32Error() == NativeMethods.ErrorAccessDenied;
        NativeMethods.CloseDesktop(desktop);
        return false;
    }

    private static bool ScreenSaverRunning()
    {
        var running = 0;
        return NativeMethods.SystemParametersInfo(NativeMethods.SpiGetScreenSaverRunning, 0, ref running, 0) && running != 0;
    }

    private static bool Contains(NativeMethods.Rect outer, NativeMethods.Rect inner) =>
        outer.Left <= inner.Left && outer.Top <= inner.Top && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

    private static (NativeMethods.Rect Bounds, NativeMethods.Rect Work, IntPtr Monitor) PrimaryMonitor()
    {
        var point = new NativeMethods.Point();
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { CbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info) && (info.Flags & NativeMethods.MonitorInfoPrimary) != 0)
            return (info.Monitor, info.Work, monitor);

        var found = (new NativeMethods.Rect(), new NativeMethods.Rect(), IntPtr.Zero);
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (candidate, _, _, _) =>
        {
            var current = new NativeMethods.MonitorInfo { CbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(candidate, ref current))
                return true;
            if ((current.Flags & NativeMethods.MonitorInfoPrimary) != 0)
            {
                found = (current.Monitor, current.Work, candidate);
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found.Item3 == IntPtr.Zero ? (info.Monitor, info.Work, monitor) : found;
    }

    private static double DpiOf(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0
            && dpiX >= 96)
            return dpiX;
        return 96;
    }

    private static RectD ToRect(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private sealed class HostPointer : IPointerSource
    {
        public DesktopHost Host { get; set; } = null!;

        public IReadOnlyList<RectD> Icons { get; set; } = Array.Empty<RectD>();

        public IReadOnlyList<RectD> Buttons { get; set; } = Array.Empty<RectD>();

        public PointerSample Poll(double now)
        {
            if (!NativeMethods.GetCursorPos(out var point))
                return new PointerSample(Vec2.Zero, false, HitClass.OtherWindow);

            var cursor = new Vec2(point.X, point.Y);
            var down = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;
            var kind = WindowKindOf(point);
            var hit = HitClassifier.Classify(cursor, Icons, Buttons, kind);
            return new PointerSample(cursor, down, hit);
        }

        private static WindowKind WindowKindOf(NativeMethods.Point point)
        {
            var hwnd = NativeMethods.WindowFromPoint(point);
            for (var current = hwnd; current != IntPtr.Zero; current = NativeMethods.GetParent(current))
            {
                var name = NativeMethods.ClassName(current);
                if (name is "SysListView32" or "Progman")
                    return WindowKind.Desktop;
            }

            return WindowKind.Other;
        }
    }
}
