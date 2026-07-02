namespace Vora.Application.Iptv;

public class TunerLimitReachedException : Exception
{
    public TunerLimitReachedException()
        : base("No tuners are available for this playlist.")
    {
    }

    public TunerLimitReachedException(string message)
        : base(message)
    {
    }
}
