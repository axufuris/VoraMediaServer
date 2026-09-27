namespace Vora.Application.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string? SecretKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
}
