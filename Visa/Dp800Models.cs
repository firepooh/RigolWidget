namespace RigolWidget.Visa;

/// <summary>Channel ratings: upper clamp limits for setpoints/protection values.</summary>
public sealed record ChannelRating(double VMax, double IMax, double OvpMax, double OcpMax);

/// <summary>DP800 series model ratings (for the CH1..CH3 we control).</summary>
public sealed record Dp800Model(string Name, ChannelRating Ch1, ChannelRating? Ch2, ChannelRating? Ch3)
{
    /// <summary>Whether it has CH2 (false = single-channel model).</summary>
    public bool HasCh2 => Ch2 is not null;

    /// <summary>Whether it has CH3.</summary>
    public bool HasCh3 => Ch3 is not null;

    /// <summary>Number of channels this widget drives (1, 2, or 3).</summary>
    public int ChannelCount => HasCh3 ? 3 : HasCh2 ? 2 : 1;

    /// <summary>Whether the given channel number exists on this model.</summary>
    public bool HasChannel(int channel) =>
        channel == 1 || (channel == 2 && HasCh2) || (channel == 3 && HasCh3);

    /// <summary>Look up the rating by channel number (falls back to CH1 rating if absent).</summary>
    public ChannelRating RatingFor(int channel) =>
        channel == 3 ? (Ch3 ?? Ch1) : channel == 2 ? (Ch2 ?? Ch1) : Ch1;
}

/// <summary>DP800 series model rating table and *IDN? matching.</summary>
public static class Dp800Models
{
    // Source: RIGOL DP800 User's Guide Ch.5 Specifications (DC Output / OVP·OCP ranges).
    // The protection upper limits (OvpMax/OcpMax) are the top of the spec's settable OVP/OCP ranges.
    // Note: DP831 CH3 is a negative rail (0 to -30V); the positive-oriented UI does not support it,
    //       so DP831 is treated as a 2-channel model here (Ch3 = null).
    private static readonly Dp800Model[] Table =
    {
        // DP832 / DP832A: CH1 30V/3A, CH2 30V/3A, CH3 5V/3A
        new("DP832",  new(30, 3, 33, 3.3),  new(30, 3, 33, 3.3),  new(5, 3, 5.5, 3.3)),
        new("DP832A", new(30, 3, 33, 3.3),  new(30, 3, 33, 3.3),  new(5, 3, 5.5, 3.3)),
        // DP831 / DP831A: CH1 8V/5A, CH2 30V/2A (CH3 is a negative rail — not shown)
        new("DP831",  new(8, 5, 8.8, 5.5),  new(30, 2, 33, 2.2),  null),
        new("DP831A", new(8, 5, 8.8, 5.5),  new(30, 2, 33, 2.2),  null),
        // DP821 / DP821A: CH1 60V/1A, CH2 8V/10A
        new("DP821",  new(60, 1, 66, 1.1),  new(8, 10, 8.8, 11),  null),
        new("DP821A", new(60, 1, 66, 1.1),  new(8, 10, 8.8, 11),  null),
        // DP811 / DP811A: single channel (Range2 40V/5A)
        new("DP811",  new(40, 5, 44, 5.5),  null,  null),
        new("DP811A", new(40, 5, 44, 5.5),  null,  null),
    };

    /// <summary>Default (when the device is unidentified/not connected).</summary>
    public static readonly Dp800Model Default = Table[0]; // DP832

    /// <summary>
    /// Identifies the model from an *IDN? response (e.g. "RIGOL TECHNOLOGIES,DP832,DP8...,00.01.16").
    /// For an unknown model, uses the IDN model name but falls back to the DP832 ratings.
    /// </summary>
    public static Dp800Model FromIdn(string idn)
    {
        string model = ParseModel(idn);
        foreach (var m in Table)
            if (string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase))
                return m;

        // Unregistered model: keep the name but use the DP832 ratings for safety.
        if (!string.IsNullOrWhiteSpace(model))
            return Default with { Name = model };
        return Default;
    }

    /// <summary>Extract the second field (model name) from *IDN?.</summary>
    public static string ParseModel(string idn)
    {
        if (string.IsNullOrWhiteSpace(idn)) return "";
        var parts = idn.Split(',');
        return parts.Length >= 2 ? parts[1].Trim() : "";
    }
}
