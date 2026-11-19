namespace RigolWidget.Visa;

/// <summary>
/// One full read of a channel (see <see cref="Dp832.ReadChannel"/>). Every value has its own
/// success flag because the queries are sent one by one and any of them can fail.
/// Plain fields so the query methods can fill them through <c>out</c> arguments.
/// </summary>
public sealed class ChannelReading
{
    public bool OkMeas;
    public double MeasVolts;
    public double MeasAmps;
    public double MeasWatts;

    public bool OkMode;
    public string Mode = "";

    public bool OkSet;
    public double SetVolts;
    public double SetAmps;

    public bool OkOutput;
    public bool OutputOn;

    public bool OkOcp;
    public bool OcpOn;
    public bool OkOcpValue;
    public double OcpValue;
    public bool OkOcpTrip;
    public bool OcpTripped;

    public bool OkOvp;
    public bool OvpOn;
    public bool OkOvpValue;
    public double OvpValue;
    public bool OkOvpTrip;
    public bool OvpTripped;

    /// <summary>True when every query succeeded, i.e. the reading is a consistent snapshot.</summary>
    public bool Complete =>
        OkMeas && OkMode && OkSet && OkOutput &&
        OkOcp && OkOcpValue && OkOcpTrip &&
        OkOvp && OkOvpValue && OkOvpTrip;
}
