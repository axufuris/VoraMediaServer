namespace Vora.Application.Templates;

public class TemplateMetaVM
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? Preview { get; init; }
    public bool IsBuiltIn { get; init; }
}
