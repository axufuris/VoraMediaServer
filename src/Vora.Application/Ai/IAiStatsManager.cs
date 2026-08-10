using Vora.Application.Ai.ViewModels;

namespace Vora.Application.Ai;

public interface IAiStatsManager
{
    Task<AiStatsDashboardVM> GetDashboardAsync(DateTime? startDate, DateTime? endDate, int page, int pageSize, string? pluginId = null);
}
