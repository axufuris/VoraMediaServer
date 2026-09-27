using Vora.Domain.Entities.Templates;

namespace Vora.Application.Templates;

public interface IClientTemplateScheduleRepository
{
    Task<List<ClientTemplateSchedule>> GetAllAsync();
    Task<ClientTemplateSchedule?> GetByIdAsync(Guid id);
    Task<ClientTemplateSchedule?> GetActiveAsync(DateTime nowUtc);
    Task AddAsync(ClientTemplateSchedule schedule);
    Task UpdateAsync(ClientTemplateSchedule schedule);
    Task DeleteAsync(Guid id);
}
