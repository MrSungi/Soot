using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Soot;

internal sealed class ResourceUsageModule : IPetPanelModule
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock cpuValue = MakeValue();
    private readonly TextBlock memoryValue = MakeValue();
    private ulong previousIdle;
    private ulong previousKernel;
    private ulong previousUser;
    private bool hasCpuSample;

    public string Title => "Resources";

    public ResourceUsageModule()
    {
        timer.Tick += (_, _) => Refresh();
    }

    public FrameworkElement CreateView(PetPanelContext context)
    {
        var content = new StackPanel { Margin = new Thickness(10) };
        content.Children.Add(MakeLabel("CPU"));
        content.Children.Add(cpuValue);
        content.Children.Add(MakeLabel("Memory"));
        content.Children.Add(memoryValue);
        content.Children.Add(new TextBlock
        {
            Text = "Read-only local measurements",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 178, 188)),
            Margin = new Thickness(0, 12, 0, 0)
        });
        Refresh();
        return content;
    }

    public void SetPanelVisible(bool visible)
    {
        if (visible)
        {
            Refresh();
            timer.Start();
        }
        else timer.Stop();
    }

    private void Refresh()
    {
        cpuValue.Text = ReadCpuUsage();
        memoryValue.Text = ReadMemoryUsage();
    }

    private string ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return "Unavailable";
        var idleNow = ToUInt64(idle);
        var kernelNow = ToUInt64(kernel);
        var userNow = ToUInt64(user);
        var result = "Sampling…";
        if (hasCpuSample)
        {
            var idleDelta = idleNow - previousIdle;
            var totalDelta = (kernelNow - previousKernel) + (userNow - previousUser);
            var percent = totalDelta == 0 ? 0 : 100.0 * (totalDelta - idleDelta) / totalDelta;
            result = $"{Math.Clamp(percent, 0, 100):0}%";
        }
        previousIdle = idleNow;
        previousKernel = kernelNow;
        previousUser = userNow;
        hasCpuSample = true;
        return result;
    }

    private static string ReadMemoryUsage()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return "Unavailable";
        var used = status.TotalPhysical - status.AvailablePhysical;
        var usedGiB = used / 1024d / 1024 / 1024;
        var totalGiB = status.TotalPhysical / 1024d / 1024 / 1024;
        return $"{status.MemoryLoad}%  ({usedGiB:0.0} / {totalGiB:0.0} GB)";
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(174, 178, 188)),
        Margin = new Thickness(0, 9, 0, 1)
    };

    private static TextBlock MakeValue() => new()
    {
        Text = "Sampling…",
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White
    };

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
