namespace Vora.Application.Templates;

public record SetActiveTemplateRequest(string TemplateId);
public record SetDefaultTemplateRequest(string TemplateId);
public record CreateTemplateScheduleRequest(
    string TemplateId,
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int Priority,
    bool Enabled);
public record UpdateTemplateScheduleRequest(
    string TemplateId,
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int Priority,
    bool Enabled);
public record SetActiveTemplateResponse(string TemplateId, ActiveTemplateSource Source);
public record TemplateRescanResponse(int BundleCount);
