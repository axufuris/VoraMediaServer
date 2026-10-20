using Vora.Application.Devices;

namespace Vora.Api.Extensions;

public static class DeviceHeaderExtensions
{
    public const string DeviceIdHeader = "X-Vora-Device-Id";
    public const string ClientNameHeader = "X-Vora-Client";
    public const string DeviceNameHeader = "X-Vora-Device";
    public const string DeviceTypeHeader = "X-Vora-Device-Type";
    public const string OperatingSystemHeader = "X-Vora-OS";

    public static string? ReadDeviceId(this HttpRequest request) => Header(request, DeviceIdHeader);

    public static DeviceIdentity ReadDeviceIdentity(this HttpRequest request) => new(
        Header(request, ClientNameHeader),
        Header(request, DeviceNameHeader),
        Header(request, DeviceTypeHeader),
        Header(request, OperatingSystemHeader));

    private static string? Header(HttpRequest request, string name) =>
        request.Headers[name].FirstOrDefault() is { Length: > 0 } value ? value : null;
}
