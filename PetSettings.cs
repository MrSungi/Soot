using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Soot;

internal sealed class PetSettings
{
    public int Version { get; set; } = 1;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Following { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
}

internal static class PetSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SootPet", "settings.json");

    internal static PetSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(SettingsPath)) ?? new PetSettings();
            if (settings.Version == 0)
            {
                settings.Following = false;
                settings.Version = 1;
            }
            return settings;
        }
        catch
        {
            return new PetSettings();
        }
    }

    internal static void Save(PetSettings settings, double left, double top)
    {
        try
        {
            settings.Left = left;
            settings.Top = top;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // A read-only pet should remain usable if settings cannot be written.
        }
    }
}

internal static class StartupShortcut
{
    private const string ShortcutName = "Soot.lnk";

    internal static void SetEnabled(bool enabled)
    {
        var startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupDirectory, ShortcutName);
        if (!enabled)
        {
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new InvalidOperationException("Could not find the running Soot executable.");

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) throw new InvalidOperationException("Windows Script Host is unavailable.");

        var shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create the Windows shortcut helper.");
        object? shortcutObject = null;
        try
        {
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
            shortcut.Description = "Start Soot desktop pet";
            shortcut.Save();
        }
        finally
        {
            if (shortcutObject is not null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
            if (Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }
    }
}
