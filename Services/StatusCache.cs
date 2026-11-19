using RigolWidget.Visa;

namespace RigolWidget.Services;

/// <summary>
/// Latest per-channel state collected by the widget's poll loop. The MCP tools answer from here
/// instead of sending about ten queries per channel on every call, so an AI client (or the MCP
/// panel's auto refresh) adds no load on the instrument while the data is fresh.
///
/// Freshness rules: a measurement is fresh for <c>maxMeasAge</c>; settings are fresh for
/// <c>maxSettingsAge</c> and only while no write command has been sent since they were read
/// (tracked with <see cref="RigolConnection.WriteCount"/>). Anything stale makes the reader
/// fall back to querying the instrument.
/// </summary>
public sealed class StatusCache
{
    public const int MaxChannels = 3;

    /// <summary>Last known state of one channel. Immutable, so a reader never sees half an update.</summary>
    public sealed record Sample(
        double MeasVolts, double MeasAmps, double MeasWatts, string Mode, DateTime MeasuredUtc,
        bool HasSettings, bool OutputOn, double SetVolts, double SetAmps,
        bool OcpOn, double OcpValue, bool OcpTripped,
        bool OvpOn, double OvpValue, bool OvpTripped,
        DateTime SettingsUtc, long SettingsWriteCount)
    {
        /// <summary>Build a complete sample from a full channel read.</summary>
        public static Sample FromReading(ChannelReading r, long writeCountBefore, DateTime nowUtc) => new(
            r.MeasVolts, r.MeasAmps, r.MeasWatts, r.Mode, nowUtc,
            true, r.OutputOn, r.SetVolts, r.SetAmps,
            r.OcpOn, r.OcpValue, r.OcpTripped,
            r.OvpOn, r.OvpValue, r.OvpTripped,
            nowUtc, writeCountBefore);
    }

    private readonly object _lock = new();
    private readonly Sample?[] _samples = new Sample?[MaxChannels];

    /// <summary>Record a fast-poll measurement. <paramref name="mode"/> null keeps the previous mode.</summary>
    public void UpdateMeasurement(int channel, double volts, double amps, double watts, string? mode, DateTime nowUtc)
    {
        if (channel is < 1 or > MaxChannels) return;
        lock (_lock)
        {
            var old = _samples[channel - 1];
            _samples[channel - 1] = old is null
                ? new Sample(volts, amps, watts, mode ?? "", nowUtc,
                             false, false, 0, 0, false, 0, false, false, 0, false, DateTime.MinValue, -1)
                : old with
                {
                    MeasVolts = volts, MeasAmps = amps, MeasWatts = watts,
                    Mode = mode ?? old.Mode, MeasuredUtc = nowUtc,
                };
        }
    }

    /// <summary>
    /// Record a complete channel read. <paramref name="writeCountBefore"/> must be read before the
    /// first query of that read; a write that lands in between then marks the result stale.
    /// Incomplete reads are ignored except for their measurement part.
    /// </summary>
    public void UpdateFull(int channel, ChannelReading r, long writeCountBefore, DateTime nowUtc)
    {
        if (channel is < 1 or > MaxChannels) return;
        if (!r.Complete)
        {
            if (r.OkMeas)
                UpdateMeasurement(channel, r.MeasVolts, r.MeasAmps, r.MeasWatts, r.OkMode ? r.Mode : null, nowUtc);
            return;
        }
        lock (_lock) _samples[channel - 1] = Sample.FromReading(r, writeCountBefore, nowUtc);
    }

    /// <summary>Forget everything (disconnect, device or model change).</summary>
    public void Clear()
    {
        lock (_lock) Array.Clear(_samples);
    }

    /// <summary>
    /// Returns samples for CH1..<paramref name="channelCount"/> when all of them are fresh.
    /// The ages are the oldest measurement and the oldest settings among those channels.
    /// </summary>
    public bool TryGetFresh(int channelCount, long currentWriteCount, DateTime nowUtc,
        TimeSpan maxMeasAge, TimeSpan maxSettingsAge,
        out Sample[] samples, out TimeSpan measAge, out TimeSpan settingsAge)
    {
        samples = Array.Empty<Sample>();
        measAge = settingsAge = TimeSpan.Zero;
        if (channelCount is < 1 or > MaxChannels) return false;

        var result = new Sample[channelCount];
        lock (_lock)
        {
            for (int i = 0; i < channelCount; i++)
            {
                var s = _samples[i];
                if (s is null || !s.HasSettings) return false;
                if (s.SettingsWriteCount != currentWriteCount) return false;   // a write may have changed it

                var ma = nowUtc - s.MeasuredUtc;
                var sa = nowUtc - s.SettingsUtc;
                if (ma > maxMeasAge || sa > maxSettingsAge) return false;

                if (ma > measAge) measAge = ma;
                if (sa > settingsAge) settingsAge = sa;
                result[i] = s;
            }
        }
        samples = result;
        return true;
    }
}
