using Vora.Application.Ai.ViewModels;
using Vora.Application.Recommendations;

namespace Vora.Application.Ai;

public class AiStatsManager : IAiStatsManager
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IOpenAiRecommendationRepository _repository;

    public AiStatsManager(IOpenAiRecommendationRepository repository)
    {
        _repository = repository;
    }

    public Task<AiStatsDashboardVM> GetDashboardAsync(DateTime? startDate, DateTime? endDate, int page, int pageSize)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        return _repository.GetAiStatsDashboardAsync(startDate, endDate, normalizedPage, normalizedPageSize);
    }
}
