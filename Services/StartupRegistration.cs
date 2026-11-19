using System.IO;
using Microsoft.Win32;

namespace RigolWidget.Services;

/// <summary>
/// "Start with Windows": a per-user entry under HKCU\...\Run (no admin rights needed).
/// The entry launches the exe with <see cref="AutostartArg"/>, which tells the app not to stop at
/// the device-select window when the instrument is still powered off at sign-in.
/// </summary>
public static class StartupRegistration
{
    public const string AutostartArg = "--autostart";
    public const string ValueName = "RigolWidget";
    private const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Key under HKCU that holds the entry. Tests point this at a scratch key.</summary>
    public static string KeyPath { get; set; } = DefaultKeyPath;

    /// <summary>Command line stored in the registry for <paramref name="exePath"/>.</summary>
    public static string BuildCommand(string exePath) => $"\"{Path.GetFullPath(exePath)}\" {AutostartArg}";

    /// <summary>The registered command line, or null when there is no entry.</summary>
    public static string? GetCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>True when the entry exists and starts exactly this executable.</summary>
    public static bool IsEnabledFor(string exePath)
    {
        string? cmd = GetCommand();
        return cmd != null &&
               string.Equals(ExtractExe(cmd), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Register <paramref name="exePath"/> (replaces an entry left by another copy).</summary>
    public static void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, BuildCommand(exePath), RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Executable part of a command line: the quoted prefix, or everything up to the first space.</summary>
    public static string ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
