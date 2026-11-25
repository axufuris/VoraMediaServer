namespace Vora.Application.Devices;

public static class DeviceNickname
{
    public const int MaxLength = 64;

    public static string Normalize(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return string.Empty;
        }

        var words = nickname.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var normalized = string.Join(' ', words);

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("A nickname cannot contain control characters.", nameof(nickname));
        }

        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"A nickname can be at most {MaxLength} characters.", nameof(nickname));
        }

        return normalized;
    }
}
