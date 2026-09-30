namespace Vora.Application.Logging;

public static class LogValue
{
    public static string SingleLine(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
