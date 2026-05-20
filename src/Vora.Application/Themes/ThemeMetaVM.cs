namespace Vora.Application.Themes;

/// <summary>
/// Display metadata for an admin theme. The full manifest (token values,
/// background images, layout flags) lives in the frontend bundle for built-in
/// themes; the backend's job is to enumerate which themes exist + which one
/// is active per server.
///
/// When plugin-shipped themes arrive in a future phase, this VM grows an
/// optional manifest payload so frontend can render themes it doesn't know
/// about at build time.
/// </summary>
public class ThemeMetaVM
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? Preview { get; init; }
    public bool IsBuiltIn { get; init; }
}
