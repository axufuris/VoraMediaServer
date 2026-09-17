using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Ai;

public class AiUsageLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PluginId { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Guid? ProfileId { get; set; }
    public virtual UserProfile? Profile { get; set; }
}
