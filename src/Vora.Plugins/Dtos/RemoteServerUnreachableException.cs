namespace Vora.Plugins.Dtos;

public class RemoteServerUnreachableException : Exception
{
    public RemoteServerUnreachableException(string message)
        : base(message)
    {
    }
}
