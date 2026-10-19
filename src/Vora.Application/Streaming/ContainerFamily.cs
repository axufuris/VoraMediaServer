namespace Vora.Application.Streaming;

public static class ContainerFamily
{
    public static string Of(string? container) => container?.Trim().ToLowerInvariant() switch
    {
        "m4v" or "mov" => "mp4",
        { } other => other,
        null => string.Empty
    };

    public static bool PlaysIn(IEnumerable<string> clientContainers, string? container) =>
        clientContainers.Any(supported => Of(supported) == Of(container));
}
