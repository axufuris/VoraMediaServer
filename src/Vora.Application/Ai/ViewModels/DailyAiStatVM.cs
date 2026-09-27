namespace Vora.Application.Ai.ViewModels;

public class DailyAiStatVM
{
    public DateTime Date { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}
