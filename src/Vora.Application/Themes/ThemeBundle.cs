namespace Vora.Application.Themes;

/// <summary>
/// In-memory representation of a theme bundle loaded from disk.
/// The full token/background/layout payload stays as opaque JSON so the
/// backend never needs to model the schema — that's the frontend's job.
/// </summary>
public class ThemeBundle
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    /// <summary>Relative path inside the bundle to the preview image, if any.</summary>
    public string? PreviewRelativePath { get; init; }
    /// <summary>Absolute filesystem path to the bundle directory.</summary>
    public required string BundlePath { get; init; }
    /// <summary>Raw manifest JSON as authored, served verbatim to the frontend.</summary>
    public required string RawManifestJson { get; init; }
}
