namespace Vora.Application.Streaming;

public static class ContainerFamily
{
    public static string Of(string? container) => container?.Trim().ToLowerInvariant() switch
    {
        "mp4" or "m4v" or "mov" or "3gp" => "mp4",
        "mpg" or "mpeg" or "vob" => "mpeg",
        "m2ts" or "mts" => "m2ts",
        { } other => other,
        null => string.Empty
    };

    public static bool PlaysIn(IEnumerable<string> clientContainers, string? container) =>
        clientContainers.Any(supported => Of(supported) == Of(container));
}
