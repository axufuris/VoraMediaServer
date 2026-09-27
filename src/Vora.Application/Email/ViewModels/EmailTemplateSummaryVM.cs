using Vora.Domain.Enums;

namespace Vora.Application.Email.ViewModels;

public class EmailTemplateSummaryVM
{
    public EmailTemplateKey Key { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public bool HasOverride { get; set; }
    public DateTime? OverrideUpdatedAt { get; set; }
}
