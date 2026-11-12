namespace Vora.Application.Iptv;

public class IptvSourceException : Exception
{
    public IptvSourceException(string message)
        : base(message)
    {
    }

    public IptvSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
