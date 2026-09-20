using Vora.Domain.Entities.Ai;

namespace Vora.Application.Ai;

public interface IAiUsageRepository
{
    Task LogAiUsageAsync(AiUsageLog log);
    Task<long> GetMonthlyTokenUsageAsync();
}
