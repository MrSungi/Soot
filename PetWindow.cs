using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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

    private bool pointerDown;
    private bool dragging;
    private bool panelWasOpenAtPress;
    private CursorPoint pressCursor;
    private CursorPoint lastCursor;
    private double pressLeft;
    private double pressTop;
    private double lastDragDirectionX;
    private DateTime lastTick = DateTime.UtcNow;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    public PetWindow()
    {
        var sheet = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Soot.png")));
        animator = new PetAnimator(sheet);
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

        var area = SystemParameters.WorkArea;
        Left = Clamp(settings.Left ?? area.Right - Width - 30, area.Left, area.Right - Width);
        Top = Clamp(settings.Top ?? area.Bottom - Height - 24, area.Top, area.Bottom - Height);

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
        pointerDown = true;
        dragging = false;
        panelWasOpenAtPress = panel.IsOpen;
        lastCursor = pressCursor;
        pressLeft = Left;
        pressTop = Top;
        lastDragDirectionX = 0;
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!pointerDown || e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var cursor)) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dx = cursor.X - pressCursor.X;
        var dy = cursor.Y - pressCursor.Y;
        var threshold = Math.Max(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance) * dpi;

        if (!dragging && Math.Sqrt((double)dx * dx + (double)dy * dy) >= threshold)
        {
            dragging = true;
            panel.Close();
        }

        if (!dragging) return;
        lastDragDirectionX = cursor.X - lastCursor.X;
        lastCursor = cursor;
        var area = SystemParameters.WorkArea;
        Left = Clamp(pressLeft + dx / dpi, area.Left, area.Right - Width);
        Top = Clamp(pressTop + dy / dpi, area.Top, area.Bottom - Height);
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !pointerDown) return;
        pointerDown = false;
        Mouse.Capture(null);
        if (dragging)
        {
            SaveSettings();
        }
        else
        {
            animator.React();
            if (panelWasOpenAtPress) panel.Close();
            else panel.Open(ShouldPlacePanelLeft());
        }
        dragging = false;
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!pointerDown) return;
        if (dragging) SaveSettings();
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
        if (settings.Following && !dragging && GetCursorPos(out var cursor))
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var centerX = (Left + Width / 2) * dpi;
            var centerY = (Top + Height / 2) * dpi;
            var dx = cursor.X - centerX;
            var dy = cursor.Y - centerY;
            var distance = Math.Sqrt(dx * dx + dy * dy) / dpi;
            if (distance >= 14 && distance <= FollowRadius)
            {
                var area = SystemParameters.WorkArea;
                var desiredX = cursor.X / dpi - Width / 2;
                var desiredY = cursor.Y / dpi - Height / 2;
                var amount = 0.035 * (1 - distance / FollowRadius) + 0.006;
                Left = Clamp(Left + (desiredX - Left) * amount, area.Left, area.Right - Width);
                Top = Clamp(Top + (desiredY - Top) * amount, area.Top, area.Bottom - Height);
                facingRight = Left >= beforeLeft;
            }
        }

        var movedX = Left - beforeLeft;
        var movedY = Top - beforeTop;
        var isWalking = dragging || Math.Abs(movedX) + Math.Abs(movedY) > 0.08;
        if (dragging && Math.Abs(lastDragDirectionX) > 0.1) facingRight = lastDragDirectionX > 0;
        var pose = animator.Update(elapsed, isWalking, facingRight, settings.AnimationsEnabled);
        sprite.Source = pose.Frame;
        scale.ScaleX = pose.ScaleX;
        scale.ScaleY = pose.ScaleY;
    }

    private bool ShouldPlacePanelLeft()
    {
        var area = SystemParameters.WorkArea;
        return Left + PetWidth + PetPanel.Width + 24 > area.Right;
    }

    private void ResetPosition()
    {
        var area = SystemParameters.WorkArea;
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

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
