using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public static class ConnectionLimit
{
    public const string Source = "Measured Connection";
    public const double Headroom = 0.8;
    public const int TrustedBelowKbps = 300_000;

    public static int For(int measuredKbps, StreamingProfile profile) =>
        profile == StreamingProfile.DirectStreamPreference || measuredKbps <= 0 || measuredKbps >= TrustedBelowKbps
            ? 0
            : (int)Math.Round(measuredKbps * Headroom);
}
