namespace Vora.Application.Templates;

public class TemplateBundle
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? PreviewRelativePath { get; init; }
    public required string BundlePath { get; init; }
    public required string RawManifestJson { get; init; }
}
