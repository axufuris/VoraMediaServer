namespace Vora.Application.Settings;

public class CorsConfigOptions
{
    public const string SectionName = "Cors";

    public List<string> AllowedOrigins { get; set; } = new();
}
