namespace Vora.Application.Templates;

public enum ActiveTemplateSource
{
    Default,
    Profile,
    Schedule,
    Override
}

public class ActiveTemplateVM
{
    public string TemplateId { get; init; } = string.Empty;
    public ActiveTemplateSource Source { get; init; }
    public TemplateScheduleVM? Schedule { get; init; }
}
