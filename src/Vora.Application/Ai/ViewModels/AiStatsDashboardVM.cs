namespace Vora.Application.Ai.ViewModels;

public class AiStatsDashboardVM
{
    public List<DailyAiStatVM> DailyStats { get; set; } = new();
    public List<AiUsageLogVM> Logs { get; set; } = new();
    public int TotalLogs { get; set; }
}
