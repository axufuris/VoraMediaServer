using Vora.Domain.Entities.Tracking;

namespace Vora.Application.Analysis;

public interface ISystemMetricRepository
{
    Task AddMetricAsync(SystemMetric metric);
    Task<SystemMetric?> GetLatestMetricAsync();
}