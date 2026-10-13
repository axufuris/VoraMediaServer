namespace Vora.Application.Ai.ViewModels;

public class AiModelsVM
{
    public List<string> ChatModels { get; set; } = new();
    public List<string> EmbeddingModels { get; set; } = new();
    public string? Error { get; set; }
}
