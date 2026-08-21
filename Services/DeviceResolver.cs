using RigolWidget.Visa;

namespace RigolWidget.Services;

/// <summary>
/// Matches a remembered VISA resource string against the devices currently present.
/// The USB prefix (USB0/USB1/...) can change when the instrument is replugged into another port,
/// so the serial number embedded in the resource string is used as the fallback key.
/// </summary>
public static class DeviceResolver
{
    /// <summary>Serial field of a VISA resource string (USB0::0xVID::0xPID::SERIAL::INSTR), or null.</summary>
    public static string? SerialOf(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) return null;
        var parts = resource.Split("::", StringSplitOptions.TrimEntries);
        return parts.Length >= 4 && parts[3].Length > 0 ? parts[3] : null;
    }

    /// <summary>
    /// Finds the saved device among <paramref name="found"/>.
    /// Returns the resource string to use (which may differ from <paramref name="saved"/> if only the
    /// USB index changed), or null when the saved device is not present — i.e. the instrument was swapped.
    /// </summary>
    public static string? Resolve(string? saved, IReadOnlyList<string> found)
    {
        if (string.IsNullOrWhiteSpace(saved) || found.Count == 0) return null;

        foreach (var d in found)
            if (string.Equals(d, saved, StringComparison.OrdinalIgnoreCase))
                return d;

        // Same instrument on a different USB index: match by serial (only when unambiguous).
        string? serial = SerialOf(saved);
        if (serial == null) return null;

        string? hit = null;
        foreach (var d in found)
        {
            if (!string.Equals(SerialOf(d), serial, StringComparison.OrdinalIgnoreCase)) continue;
            if (hit != null) return null;   // ambiguous — let the user choose
            hit = d;
        }
        return hit;
    }

    /// <summary>
    /// Lists USB instruments, retrying a few times because right after boot/login the USB enumeration
    /// may not be complete yet. Returns an empty list if the VISA runtime is unavailable.
    /// </summary>
    public static IReadOnlyList<string> Scan(int attempts = 3, int delayMs = 700)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                using var rm = new VisaResourceManager();
                var found = rm.FindUsbInstruments();
                if (found.Count > 0) return found;
            }
            catch (VisaException)
            {
                return Array.Empty<string>();   // no VISA runtime — the select window reports it
            }

            if (i < attempts - 1) Thread.Sleep(delayMs);
        }
        return Array.Empty<string>();
    }
}
