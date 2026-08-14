using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace MouseFinder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new JiggleApp();
        Application.Run(app);
    }
}

internal sealed class JiggleApp : ApplicationContext
{
    private const double ShrinkBelowPixelsPerSecond = 500;
    private const int ActivationMilliseconds = 350;
    private const double ActivationScreenFraction = 0.5;
    private const int SpeedWindowMilliseconds = 50;
    private const int ShrinkCooldownMilliseconds = 500;
    private const int JiggleContinuationMilliseconds = 200;
    private readonly CursorScaler _cursors = new();
    private readonly Icon _appIcon;
    private readonly Queue<(long Time, double Distance)> _motion = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _poller = new() { Interval = 1 };
    private Point _last;
    private long _gestureStarted, _lastTurn;
    private long _cooldownUntil;
    private double _motionDistance;
    private int _gestureMinX, _gestureMaxX, _gestureMinY, _gestureMaxY;
    private int _turnCount, _direction;
    private bool _ready, _enabled = true;

    public JiggleApp()
    {
        TimeBeginPeriod(1);
        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information;
        var menu = new ContextMenuStrip();
        var enabled = new ToolStripMenuItem("Enabled") { Checked = true };
        enabled.Click += (_, _) =>
        {
            _enabled = !_enabled;
            enabled.Checked = _enabled;
            ResetGesture(true);
        };
        menu.Items.Add(enabled);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Mouse Finder",
            ContextMenuStrip = menu,
            Visible = true
        };
        _poller.Tick += (_, _) => PollMouse();
        _poller.Start();
    }

    private void PollMouse()
    {
        var now = Environment.TickCount64;
        _cursors.Update(now);
        if (!_enabled || !GetCursorPos(out var p)) return;
        Detect(new Point(p.X, p.Y), now);
    }

    private void Detect(Point point, long now)
    {
        if (!_ready) { _last = point; _ready = true; return; }
        var previous = _last;
        var dx = point.X - previous.X;
        var dy = point.Y - previous.Y;
        var delta = Math.Abs(dx) >= Math.Abs(dy) ? dx : dy;
        _last = point;

        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        _motion.Enqueue((now, distance));
        _motionDistance += distance;
        while (_motion.Count > 0 && now - _motion.Peek().Time > SpeedWindowMilliseconds)
            _motionDistance -= _motion.Dequeue().Distance;
        var speed = _motionDistance * 1000 / SpeedWindowMilliseconds;
        if (_gestureStarted != 0)
        {
            _gestureMinX = Math.Min(_gestureMinX, point.X);
            _gestureMaxX = Math.Max(_gestureMaxX, point.X);
            _gestureMinY = Math.Min(_gestureMinY, point.Y);
            _gestureMaxY = Math.Max(_gestureMaxY, point.Y);
        }

        if (_cursors.IsShrinking || now < _cooldownUntil)
            return;

        var stoppedJiggling = _lastTurn == 0 || now - _lastTurn > JiggleContinuationMilliseconds;
        if (_cursors.IsActive &&
            (speed < ShrinkBelowPixelsPerSecond || stoppedJiggling))
        {
            _cursors.BeginShrink(now);
            _cooldownUntil = now + ShrinkCooldownMilliseconds;
            ResetGesture(false);
            return;
        }

        // Reset an incomplete gesture after a pause, independently of cursor shrink.
        if (_lastTurn != 0 && now - _lastTurn > 300)
            ResetGesture(false);

        if (Math.Abs(delta) < 5) return;
        var direction = Math.Sign(delta);
        if (_direction != 0 && direction != _direction)
        {
            if (_gestureStarted == 0)
            {
                _gestureStarted = now;
                _gestureMinX = Math.Min(previous.X, point.X);
                _gestureMaxX = Math.Max(previous.X, point.X);
                _gestureMinY = Math.Min(previous.Y, point.Y);
                _gestureMaxY = Math.Max(previous.Y, point.Y);
            }
            _lastTurn = now;
            _turnCount++;
            var screen = Screen.FromPoint(point).Bounds;
            var spansHalfScreen =
                _gestureMaxX - _gestureMinX >= screen.Width * ActivationScreenFraction ||
                _gestureMaxY - _gestureMinY >= screen.Height * ActivationScreenFraction;
            if (_turnCount >= 6 &&
                now - _gestureStarted >= ActivationMilliseconds &&
                spansHalfScreen)
                _cursors.Enlarge(now);
        }
        _direction = direction;
    }

    private void ResetGesture(bool restoreCursor)
    {
        _gestureStarted = _lastTurn = 0;
        _turnCount = _direction = 0;
        _gestureMinX = _gestureMaxX = _gestureMinY = _gestureMaxY = 0;
        if (restoreCursor) _cursors.Restore();
    }

    protected override void ExitThreadCore()
    {
        _poller.Stop();
        _poller.Dispose();
        TimeEndPeriod(1);
        _tray.Dispose();
        _appIcon.Dispose();
        _cursors.Dispose();
        base.ExitThreadCore();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Pt point);
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint milliseconds);
}

internal sealed class CursorScaler : IDisposable
{
    private const uint SpiSetCursors = 0x0057;
    private const uint ImageCursor = 2;
    private const uint LoadFromFile = 0x0010;
    private const int GrowMilliseconds = 175;
    private const int ShrinkMilliseconds = 40;
    private static readonly CursorRole[] CursorRoles =
    {
        new(32512, "Arrow"), new(32513, "IBeam"), new(32514, "Wait"),
        new(32515, "Crosshair"), new(32516, "UpArrow"), new(32631, "NWPen"),
        new(32640, "SizeAll"), new(32641, "Arrow"), new(32642, "SizeNWSE"),
        new(32643, "SizeNESW"), new(32644, "SizeWE"), new(32645, "SizeNS"),
        new(32646, "SizeAll"), new(32648, "No"), new(32649, "Hand"),
        new(32650, "AppStarting"), new(32651, "Help"), new(32671, "Pin"),
        new(32672, "Person")
    };
    private readonly Dictionary<int, CursorAsset> _sources = new();
    private bool _large;
    private bool _growing;
    private bool _shrinking;
    private long _animationStarted, _lastFrame;
    private double _scale = 1;
    private double _animationFromScale = 1;

    public bool IsActive => _large;
    public bool IsShrinking => _shrinking;

    public CursorScaler() => RestoreSystemCursors();

    public void Enlarge(long now)
    {
        if (!_large)
        {
            if (_sources.Count == 0) CacheSources();
            _large = true;
            StartGrowth(now);
        }
        else if (_shrinking)
        {
            StartGrowth(now);
        }
    }

    public void BeginShrink(long now)
    {
        if (!_large || _shrinking) return;
        _shrinking = true;
        _growing = false;
        _animationFromScale = _scale;
        _animationStarted = _lastFrame = now;
    }

    public void Update(long now)
    {
        if ((!_growing && !_shrinking) || now - _lastFrame < 16) return;
        _lastFrame = now;
        var duration = _growing ? GrowMilliseconds : ShrinkMilliseconds;
        var progress = Math.Clamp((now - _animationStarted) / (double)duration, 0, 1);
        if (progress >= 1)
        {
            if (_shrinking) Restore();
            else
            {
                _scale = 4;
                ApplyScale(_scale);
                _growing = false;
            }
            return;
        }
        var smooth = progress * progress * (3 - (2 * progress));
        _scale = _growing
            ? _animationFromScale + ((4 - _animationFromScale) * smooth)
            : 1 + ((_animationFromScale - 1) * (1 - smooth));
        ApplyScale(_scale);
    }

    private void StartGrowth(long now)
    {
        _growing = true;
        _shrinking = false;
        _animationFromScale = _scale;
        _animationStarted = _lastFrame = now;
    }

    public void Restore()
    {
        if (!_large) return;
        RestoreSystemCursors();
        _large = false;
        _growing = false;
        _shrinking = false;
        _scale = 1;
        ReleaseSources();
    }

    public void Dispose()
    {
        Restore();
        ReleaseSources();
    }

    private void CacheSources()
    {
        var size = SystemInformation.CursorSize;
        using var cursorKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
        foreach (var role in CursorRoles)
        {
            var configuredPath = cursorKey?.GetValue(role.RegistryName) as string;
            if (!string.IsNullOrWhiteSpace(configuredPath))
                configuredPath = Environment.ExpandEnvironmentVariables(configuredPath);
            var loadedFromFile = !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath);
            var original = loadedFromFile
                ? LoadImage(0, configuredPath!, ImageCursor, size.Width, size.Height, LoadFromFile)
                : LoadCursor(0, role.Id);
            if (original == 0) continue;
            if (!GetIconInfo(original, out var info))
            {
                if (loadedFromFile) DestroyCursor(original);
                continue;
            }

            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                var dc = graphics.GetHdc();
                try { DrawIconEx(dc, 0, 0, original, size.Width, size.Height, 0, 0, 3); }
                finally { graphics.ReleaseHdc(dc); }
            }
            _sources[role.Id] = new CursorAsset(bitmap, info.HotspotX, info.HotspotY);
            if (info.Mask != 0) DeleteObject(info.Mask);
            if (info.Color != 0) DeleteObject(info.Color);
            if (loadedFromFile) DestroyCursor(original);
        }
    }

    private void ApplyScale(double scale)
    {
        var size = SystemInformation.CursorSize;
        foreach (var (id, source) in _sources)
        {
            var width = Math.Max(1, (int)Math.Round(size.Width * scale));
            var height = Math.Max(1, (int)Math.Round(size.Height * scale));
            var resized = CreatePixelPerfectCursor(source, width, height);
            if (resized != 0) SetSystemCursor(resized, id);
        }
    }

    private static nint CreatePixelPerfectCursor(CursorAsset source, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(source.Bitmap, new Rectangle(0, 0, width, height),
                0, 0, source.Bitmap.Width, source.Bitmap.Height, GraphicsUnit.Pixel);
        }

        var iconHandle = bitmap.GetHicon();
        if (!GetIconInfo(iconHandle, out var info))
        {
            DestroyIcon(iconHandle);
            return 0;
        }

        info.IsIcon = false;
        info.HotspotX = (uint)Math.Round(source.HotspotX * width / (double)source.Bitmap.Width);
        info.HotspotY = (uint)Math.Round(source.HotspotY * height / (double)source.Bitmap.Height);
        var cursor = CreateIconIndirect(ref info);
        if (info.Mask != 0) DeleteObject(info.Mask);
        if (info.Color != 0) DeleteObject(info.Color);
        DestroyIcon(iconHandle);
        return cursor;
    }

    private void ReleaseSources()
    {
        foreach (var source in _sources.Values) source.Dispose();
        _sources.Clear();
    }

    private sealed record CursorAsset(Bitmap Bitmap, uint HotspotX, uint HotspotY) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }

    private sealed record CursorRole(int Id, string RegistryName);

    private static void RestoreSystemCursors() => SystemParametersInfo(SpiSetCursors, 0, 0, 0);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern nint LoadCursor(nint instance, nint cursorName);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetSystemCursor(nint cursor, int id);
    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotspotX, HotspotY;
        public nint Mask, Color;
    }
    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")]
    private static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]
    private static extern bool DestroyCursor(nint cursor);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);
}
