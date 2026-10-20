namespace Vora.Application.Devices;

public sealed record DeviceIdentity(string? ClientName, string? DeviceName, string? DeviceType, string? OperatingSystem)
{
    public const string UnknownClient = "Unknown Client";
    public const string UnknownDevice = "Unknown Device";
    public const string UnknownType = "Unknown Type";
    public const string UnknownOperatingSystem = "Unknown OS";
}
