namespace Vora.Application.Ai.ViewModels;

public class AiStatusVM
{
    public bool Configured { get; set; }
    public bool Embeddings { get; set; }
    public bool IsOpenAi { get; set; }
    public string Host { get; set; } = string.Empty;
}
