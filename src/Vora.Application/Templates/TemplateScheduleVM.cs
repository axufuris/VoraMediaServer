namespace Vora.Application.Templates;

public class TemplateScheduleVM
{
    public Guid Id { get; init; }
    public string TemplateId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public int Priority { get; init; }
    public bool Enabled { get; init; }
    public bool TemplateMissing { get; init; }
}
