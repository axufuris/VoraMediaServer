using System.Linq.Expressions;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Ai.ViewModels;

public class AiUsageLogVM
{
    public Guid Id { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }

    public static Expression<Func<AiUsageLog, AiUsageLogVM>> Projection =>
        l => new AiUsageLogVM
        {
            Id = l.Id,
            ProfileName = l.Profile != null ? l.Profile.Name : "Unknown",
            Timestamp = l.Timestamp,
            PluginId = l.PluginId,
            ModelUsed = l.ModelUsed,
            PromptTokens = l.PromptTokens,
            CompletionTokens = l.CompletionTokens,
            TotalTokens = l.TotalTokens
        };
}
