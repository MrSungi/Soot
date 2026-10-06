using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Soot;

public sealed class PetWindow : Window
{
    private const double PetWidth = 150;
    private const double PetHeight = 150;
    private const double FollowRadius = 220;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(30);

    private readonly DispatcherTimer timer = new() { Interval = FrameInterval };
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly System.Windows.Forms.ToolStripMenuItem visibilityItem;
    private readonly System.Windows.Forms.ToolStripMenuItem followItem;
    private readonly System.Windows.Forms.ToolStripMenuItem animationItem;
    private readonly ScaleTransform scale = new(1, 1);
    private readonly System.Windows.Controls.Image sprite = new() { Width = PetWidth, Height = PetHeight, Stretch = Stretch.Uniform };
    private readonly PetSettings settings;
    private readonly PetAnimator animator;
    private readonly PetPanel panel;
    private readonly Dictionary<BitmapSource, byte[]> alphaMaps = new();

    private bool pointerDown;
    private bool dragging;
    private bool panelWasOpenAtPress;
    private CursorPoint pressCursor;
    private CursorPoint lastCursor;
    private double pressLeft;
    private double pressTop;
    private NativeRect pressWindowRect;
    private double lastDragDirectionX;
    private DateTime lastTick = DateTime.UtcNow;
    private DateTime lastHoverReaction = DateTime.MinValue;
    private bool hoverActive;
    private bool pettingUsed;
    private bool pettingBlinkActive;
    private Point hoverAnchor;
    private double hoverDwell;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private IntPtr WindowHandle => new WindowInteropHelper(this).EnsureHandle();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public PetWindow()
    {
        var sheet = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Soot.png")));
        var stretchYawnSheet = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Soot-yawn.png")));
        animator = new PetAnimator(sheet, stretchYawnSheet);
        settings = PetSettingsStore.Load();

        Width = PetWidth;
        Height = PetHeight;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Content = BuildPet();

        var primary = Forms.Screen.PrimaryScreen!;
        var area = ToDip(primary.WorkingArea, GetDpiForScreen(primary));
        Left = settings.Left ?? area.Right - Width - 30;
        Top = settings.Top ?? area.Bottom - Height - 24;
        ClampToNearestWorkArea();

        panel = new PetPanel(sprite, new PetPanelContext
        {
            Settings = settings,
            SaveSettings = SaveSettings,
            ResetPosition = ResetPosition,
            HidePet = HidePet,
            SetStartup = SetStartup,
            OpenCodex = OpenCodex
        });

        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        LostMouseCapture += OnLostMouseCapture;
        timer.Tick += Tick;
        timer.Start();

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Soot — desktop pet",
            Visible = true
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        visibilityItem = new System.Windows.Forms.ToolStripMenuItem("Hide Soot");
        visibilityItem.Click += (_, _) => ToggleVisibility();
        followItem = new System.Windows.Forms.ToolStripMenuItem("Follow cursor")
        {
            CheckOnClick = true,
            Checked = settings.Following
        };
        followItem.CheckedChanged += (_, _) =>
        {
            settings.Following = followItem.Checked;
            SaveSettings();
        };
        animationItem = new System.Windows.Forms.ToolStripMenuItem("Animate Soot")
        {
            CheckOnClick = true,
            Checked = settings.AnimationsEnabled
        };
        animationItem.CheckedChanged += (_, _) =>
        {
            settings.AnimationsEnabled = animationItem.Checked;
            SaveSettings();
        };
        menu.Items.Add(visibilityItem);
        menu.Items.Add(followItem);
        menu.Items.Add(animationItem);
        menu.Items.Add("Reset position", null, (_, _) => ResetPosition());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit Soot", null, (_, _) => Close());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowPet();

        Closed += (_, _) =>
        {
            panel.Dispose();
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            SaveSettings();
        };
    }

    private UIElement BuildPet()
    {
        var canvas = new Canvas
        {
            Width = PetWidth,
            Height = PetHeight,
            RenderTransformOrigin = new Point(0.5, 0.56),
            RenderTransform = scale
        };
        Canvas.SetLeft(sprite, 0);
        Canvas.SetTop(sprite, 0);
        canvas.Children.Add(sprite);
        return canvas;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !GetCursorPos(out pressCursor)) return;
        CancelPetting();
        pointerDown = true;
        dragging = false;
        panelWasOpenAtPress = panel.IsOpen;
        lastCursor = pressCursor;
        pressLeft = Left;
        pressTop = Top;
        GetWindowRect(WindowHandle, out pressWindowRect);
        lastDragDirectionX = 0;
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - lastHoverReaction >= TimeSpan.FromSeconds(2))
        {
            lastHoverReaction = now;
            animator.React(PetReaction.Wave);
        }
        hoverActive = true;
        pettingUsed = false;
        pettingBlinkActive = false;
        hoverDwell = 0;
        hoverAnchor = Mouse.GetPosition(this);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        hoverActive = false;
        hoverDwell = 0;
        if (pettingBlinkActive) animator.CancelReaction(PetReaction.PettingBlink);
        pettingBlinkActive = false;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (pointerDown) CancelPetting();
        if (!pointerDown || e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var cursor)) return;
        var dx = cursor.X - pressCursor.X;
        var dy = cursor.Y - pressCursor.Y;
        var threshold = Math.Max(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance) * VisualTreeHelper.GetDpi(this).DpiScaleX;

        if (!dragging && Math.Sqrt((double)dx * dx + (double)dy * dy) >= threshold)
        {
            dragging = true;
            animator.React(PetReaction.Pickup);
            panel.Close();
        }

        if (!dragging) return;
        lastDragDirectionX = cursor.X - lastCursor.X;
        lastCursor = cursor;
        // Keep the drag continuous across the virtual desktop; constrain it on release.
        SetWindowPos(WindowHandle, IntPtr.Zero, pressWindowRect.Left + dx, pressWindowRect.Top + dy, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !pointerDown) return;
        pointerDown = false;
        Mouse.Capture(null);
        if (dragging)
        {
            ClampToNearestWorkArea();
            SaveSettings();
            animator.React(PetReaction.Release);
        }
        else
        {
            animator.React(PetReaction.Wave);
            if (panelWasOpenAtPress) panel.Close();
            else panel.Open(ShouldPlacePanelLeft());
        }
        dragging = false;
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!pointerDown) return;
        if (dragging)
        {
            ClampToNearestWorkArea();
            SaveSettings();
            animator.React(PetReaction.Release);
        }
        pointerDown = false;
        dragging = false;
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Min(0.1, Math.Max(0, (now - lastTick).TotalSeconds));
        lastTick = now;

        var beforeLeft = Left;
        var beforeTop = Top;
        var facingRight = true;
        if (settings.Following && !dragging && GetCursorPos(out var cursor) && GetWindowRect(WindowHandle, out var windowRect))
        {
            var centerX = (windowRect.Left + windowRect.Right) / 2;
            var centerY = (windowRect.Top + windowRect.Bottom) / 2;
            var dx = cursor.X - centerX;
            var dy = cursor.Y - centerY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var petMonitor = GetScreenAt(centerX, centerY);
            var cursorMonitor = GetScreenAt(cursor.X, cursor.Y);
            var monitorDpi = GetDpiForScreen(petMonitor);
            if (petMonitor == cursorMonitor && distance >= 14 * monitorDpi && distance <= FollowRadius * monitorDpi)
            {
                var area = petMonitor.WorkingArea;
                var windowWidth = windowRect.Right - windowRect.Left;
                var windowHeight = windowRect.Bottom - windowRect.Top;
                var desiredX = cursor.X - windowWidth / 2;
                var desiredY = cursor.Y - windowHeight / 2;
                var amount = 0.035 * (1 - distance / (FollowRadius * monitorDpi)) + 0.006;
                var nextX = Clamp(windowRect.Left + (desiredX - windowRect.Left) * amount, area.Left, area.Right - windowWidth);
                var nextY = Clamp(windowRect.Top + (desiredY - windowRect.Top) * amount, area.Top, area.Bottom - windowHeight);
                SetWindowPos(WindowHandle, IntPtr.Zero, (int)nextX, (int)nextY, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                facingRight = nextX >= windowRect.Left;
            }
        }

        var movedX = Left - beforeLeft;
        var movedY = Top - beforeTop;
        var isWalking = dragging || Math.Abs(movedX) + Math.Abs(movedY) > 0.08;
        if (dragging && Math.Abs(lastDragDirectionX) > 0.1) facingRight = lastDragDirectionX > 0;
        UpdatePetting(elapsed);
        var idleAnimationEligible = !hoverActive && !pointerDown && !dragging && !panel.IsOpen;
        var pose = animator.Update(elapsed, isWalking, facingRight, settings.AnimationsEnabled, idleAnimationEligible);
        sprite.Source = pose.Frame;
        sprite.Opacity = pose.Opacity;
        scale.ScaleX = pose.ScaleX;
        scale.ScaleY = pose.ScaleY;
    }

    private void UpdatePetting(double elapsed)
    {
        if (!hoverActive || pointerDown || dragging || pettingUsed || !settings.AnimationsEnabled || !IsMouseOver)
        {
            if (!settings.AnimationsEnabled) CancelPetting();
            return;
        }

        var point = Mouse.GetPosition(this);
        var dx = point.X - hoverAnchor.X;
        var dy = point.Y - hoverAnchor.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > 6)
        {
            hoverAnchor = point;
            hoverDwell = 0;
        }

        if (!IsArtworkAt(point))
        {
            hoverDwell = 0;
            hoverAnchor = point;
            return;
        }

        hoverDwell += elapsed;
        if (hoverDwell < 1) return;
        pettingUsed = true;
        pettingBlinkActive = true;
        animator.React(PetReaction.PettingBlink);
    }

    private bool IsArtworkAt(Point point)
    {
        if (sprite.Source is not BitmapSource source || sprite.ActualWidth <= 0 || sprite.ActualHeight <= 0) return false;
        var scaleFactor = Math.Min(sprite.ActualWidth / source.PixelWidth, sprite.ActualHeight / source.PixelHeight);
        var drawnWidth = source.PixelWidth * scaleFactor;
        var drawnHeight = source.PixelHeight * scaleFactor;
        var local = TranslatePoint(point, sprite);
        var x = (int)((local.X - (sprite.ActualWidth - drawnWidth) / 2) / scaleFactor);
        var y = (int)((local.Y - (sprite.ActualHeight - drawnHeight) / 2) / scaleFactor);
        if (x < 0 || y < 0 || x >= source.PixelWidth || y >= source.PixelHeight) return false;

        if (!alphaMaps.TryGetValue(source, out var alpha))
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            alpha = new byte[converted.PixelWidth * converted.PixelHeight];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
            alphaMaps[source] = alpha;
        }

        return alpha[y * source.PixelWidth + x] > 8;
    }

    private void CancelPetting()
    {
        pettingUsed = true;
        hoverDwell = 0;
        if (!pettingBlinkActive) return;
        animator.CancelReaction(PetReaction.PettingBlink);
        pettingBlinkActive = false;
    }

    private bool ShouldPlacePanelLeft()
    {
        var area = GetWorkAreaForWindow();
        return Left + PetWidth + PetPanel.Width + 24 > area.Right;
    }

    private void ResetPosition()
    {
        var area = GetWorkAreaForWindow();
        Left = area.Right - Width - 30;
        Top = area.Bottom - Height - 24;
        SaveSettings();
    }

    private void HidePet()
    {
        panel.Close();
        visibilityItem.Text = "Show Soot";
        Hide();
    }

    private void ShowPet()
    {
        visibilityItem.Text = "Hide Soot";
        Show();
        Topmost = true;
        Activate();
    }

    private void ToggleVisibility()
    {
        if (IsVisible) HidePet();
        else ShowPet();
    }

    private bool SetStartup(bool enabled)
    {
        try
        {
            StartupShortcut.SetEnabled(enabled);
            settings.StartWithWindows = enabled;
            SaveSettings();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not update the Windows startup shortcut.\n\n{ex.Message}", "Soot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OpenCodex()
    {
        try
        {
            Process.Start(new ProcessStartInfo("vscode://openai.chatgpt/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the VS Code Codex sidebar.\n\n{ex.Message}", "Soot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings() => PetSettingsStore.Save(settings, Left, Top);

    private void ClampToNearestWorkArea()
    {
        if (!GetWindowRect(WindowHandle, out var rect)) return;
        var windowBounds = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var area = Forms.Screen.FromRectangle(windowBounds).WorkingArea;
        var x = (int)Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - windowBounds.Width));
        var y = (int)Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - windowBounds.Height));
        SetWindowPos(WindowHandle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private Forms.Screen GetScreenAt(double physicalX, double physicalY) =>
        Forms.Screen.FromPoint(new System.Drawing.Point((int)physicalX, (int)physicalY));

    private Forms.Screen GetScreenAt(CursorPoint point) => GetScreenAt(point.X, point.Y);

    private Forms.Screen GetWorkScreenForWindow()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return GetScreenAt((Left + Width / 2) * dpi, (Top + Height / 2) * dpi);
    }

    private Rect GetWorkAreaForWindow()
    {
        var screen = GetWorkScreenForWindow();
        return ToDip(screen.WorkingArea, GetDpiForScreen(screen));
    }

    private static Rect ToDip(System.Drawing.Rectangle bounds, double dpi) =>
        new(bounds.Left / dpi, bounds.Top / dpi, bounds.Width / dpi, bounds.Height / dpi);

    private static double GetDpiForScreen(Forms.Screen screen)
    {
        try
        {
            var monitor = MonitorFromPoint(new NativePoint { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 }, 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var x, out _) == 0) return x / 96.0;
        }
        catch { }
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
