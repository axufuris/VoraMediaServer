namespace Vora.Application.Auth;

public sealed class ProfileTokenResult
{
    public string? Token { get; private init; }
    public bool ProfileExists { get; private init; }
    public bool PinRequired { get; private init; }
    public bool PinIncorrect { get; private init; }

    public bool Succeeded => Token != null;

    public static ProfileTokenResult Issued(string token) => new() { Token = token, ProfileExists = true };

    public static ProfileTokenResult NotFound() => new();

    public static ProfileTokenResult PinRejected(bool missing) => new()
    {
        ProfileExists = true,
        PinRequired = missing,
        PinIncorrect = !missing
    };
}
