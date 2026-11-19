using System.Globalization;
using RigolWidget.Visa;

namespace RigolWidget.Services;

/// <summary>
/// User-defined per-channel setpoint limits, stricter than the model rating (e.g. 5 V on a channel
/// wired to a 3.3 V board). Shared by the widget UI and the MCP tools; changed only from the widget.
/// Thread-safe: readers see an immutable snapshot that is swapped as a whole.
/// </summary>
public sealed class OutputLimits
{
    private const double Epsilon = 1e-6;   // tolerate read-back rounding (5.000 vs 5)

    private sealed record Snapshot(double?[] Volts, double?[] Amps);

    private volatile Snapshot _snap = new(new double?[3], new double?[3]);

    public OutputLimits(IReadOnlyList<AppSettings.ChannelLimit> limits) => Load(limits);

    /// <summary>Replace all limits (index 0..2 = CH1..CH3).</summary>
    public void Load(IReadOnlyList<AppSettings.ChannelLimit> limits)
    {
        var norm = AppSettings.NormalizeLimits(limits.ToArray());
        _snap = new Snapshot(norm.Select(l => l.MaxVolts).ToArray(), norm.Select(l => l.MaxAmps).ToArray());
    }

    /// <summary>Current limits in settings form (always three entries).</summary>
    public AppSettings.ChannelLimit[] ToSettings()
    {
        var s = _snap;
        return Enumerable.Range(0, 3)
            .Select(i => new AppSettings.ChannelLimit { MaxVolts = s.Volts[i], MaxAmps = s.Amps[i] })
            .ToArray();
    }

    public double? UserMaxVolts(int channel) => Get(_snap.Volts, channel);
    public double? UserMaxAmps(int channel) => Get(_snap.Amps, channel);

    public bool HasUserLimit(int channel) => UserMaxVolts(channel) is not null || UserMaxAmps(channel) is not null;

    /// <summary>Highest voltage setpoint allowed: the user limit if set, never above the rating.</summary>
    public double EffectiveMaxVolts(int channel, ChannelRating rating) =>
        UserMaxVolts(channel) is double v ? Math.Min(v, rating.VMax) : rating.VMax;

    /// <summary>Highest current setpoint allowed: the user limit if set, never above the rating.</summary>
    public double EffectiveMaxAmps(int channel, ChannelRating rating) =>
        UserMaxAmps(channel) is double a ? Math.Min(a, rating.IMax) : rating.IMax;

    /// <summary>True when <paramref name="volts"/> is above this channel's user voltage limit.</summary>
    public bool ExceedsVolts(int channel, double volts, out double limit)
    {
        limit = UserMaxVolts(channel) ?? double.PositiveInfinity;
        return volts > limit + Epsilon;
    }

    /// <summary>True when <paramref name="amps"/> is above this channel's user current limit.</summary>
    public bool ExceedsAmps(int channel, double amps, out double limit)
    {
        limit = UserMaxAmps(channel) ?? double.PositiveInfinity;
        return amps > limit + Epsilon;
    }

    /// <summary>
    /// Reason why the output must not be turned on with these setpoints, or null when it is fine.
    /// Pass the setpoints read back from the instrument (the front panel may have changed them).
    /// </summary>
    public string? CheckOutputOn(int channel, double setVolts, double setAmps)
    {
        if (ExceedsVolts(channel, setVolts, out double lv))
            return $"CH{channel} voltage setpoint {F(setVolts)} V is above the {F(lv)} V limit.";
        if (ExceedsAmps(channel, setAmps, out double la))
            return $"CH{channel} current setpoint {F(setAmps)} A is above the {F(la)} A limit.";
        return null;
    }

    /// <summary>Short text for logs, e.g. "CH1 5V/0.5A, CH2 -, CH3 -".</summary>
    public string Describe()
    {
        var s = _snap;
        return string.Join(", ", Enumerable.Range(0, 3).Select(i =>
            s.Volts[i] is null && s.Amps[i] is null
                ? $"CH{i + 1} -"
                : $"CH{i + 1} {(s.Volts[i] is double v ? F(v) + "V" : "-")}/{(s.Amps[i] is double a ? F(a) + "A" : "-")}"));
    }

    private static double? Get(double?[] arr, int channel) =>
        channel is >= 1 and <= 3 ? arr[channel - 1] : null;

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
