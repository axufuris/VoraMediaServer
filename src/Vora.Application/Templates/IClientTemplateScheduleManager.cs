using Vora.Application.Analysis;
using Vora.Domain.Entities.Templates;

namespace Vora.Application.Templates;

public interface IClientTemplateScheduleManager
{
    Task<List<TemplateScheduleVM>> GetAllAsync();
    Task<TemplateScheduleVM?> GetByIdAsync(Guid id);
    Task<ClientTemplateSchedule?> GetActiveScheduleAsync(DateTime nowUtc);
    Task<TemplateScheduleVM> CreateAsync(CreateTemplateScheduleRequest request);
    Task<TemplateScheduleVM?> UpdateAsync(Guid id, UpdateTemplateScheduleRequest request);
    Task<bool> DeleteAsync(Guid id);
}

public class ClientTemplateScheduleManager : IClientTemplateScheduleManager
{
    private readonly IClientTemplateScheduleRepository _repository;
    private readonly IClientTemplateRegistry _registry;
    private readonly IClientNotifier _notifier;

    public ClientTemplateScheduleManager(
        IClientTemplateScheduleRepository repository,
        IClientTemplateRegistry registry,
        IClientNotifier notifier)
    {
        _repository = repository;
        _registry = registry;
        _notifier = notifier;
    }

    public async Task<List<TemplateScheduleVM>> GetAllAsync()
    {
        var all = await _repository.GetAllAsync();
        return all.Select(ToVM).ToList();
    }

    public async Task<TemplateScheduleVM?> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity == null ? null : ToVM(entity);
    }

    public async Task<ClientTemplateSchedule?> GetActiveScheduleAsync(DateTime nowUtc)
    {
        var candidate = await _repository.GetActiveAsync(nowUtc);
        if (candidate == null) return null;
        if (!_registry.Exists(candidate.TemplateId)) return null;
        return candidate;
    }

    public async Task<TemplateScheduleVM> CreateAsync(CreateTemplateScheduleRequest request)
    {
        Validate(request.TemplateId, request.Name, request.StartsAtUtc, request.EndsAtUtc);

        var entity = new ClientTemplateSchedule
        {
            TemplateId = request.TemplateId,
            Name = request.Name,
            StartsAtUtc = DateTime.SpecifyKind(request.StartsAtUtc, DateTimeKind.Utc),
            EndsAtUtc = DateTime.SpecifyKind(request.EndsAtUtc, DateTimeKind.Utc),
            Priority = request.Priority,
            Enabled = request.Enabled,
        };
        await _repository.AddAsync(entity);
        await _notifier.NotifyClientTemplateConfigurationChangedAsync();
        return ToVM(entity);
    }

    public async Task<TemplateScheduleVM?> UpdateAsync(Guid id, UpdateTemplateScheduleRequest request)
    {
        Validate(request.TemplateId, request.Name, request.StartsAtUtc, request.EndsAtUtc);

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return null;

        entity.TemplateId = request.TemplateId;
        entity.Name = request.Name;
        entity.StartsAtUtc = DateTime.SpecifyKind(request.StartsAtUtc, DateTimeKind.Utc);
        entity.EndsAtUtc = DateTime.SpecifyKind(request.EndsAtUtc, DateTimeKind.Utc);
        entity.Priority = request.Priority;
        entity.Enabled = request.Enabled;

        await _repository.UpdateAsync(entity);
        await _notifier.NotifyClientTemplateConfigurationChangedAsync();
        return ToVM(entity);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return false;
        await _repository.DeleteAsync(id);
        await _notifier.NotifyClientTemplateConfigurationChangedAsync();
        return true;
    }

    private void Validate(string templateId, string name, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentException("TemplateId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        if (endsAtUtc <= startsAtUtc) throw new ArgumentException("EndsAtUtc must be after StartsAtUtc.");
    }

    private TemplateScheduleVM ToVM(ClientTemplateSchedule entity) => new()
    {
        Id = entity.Id,
        TemplateId = entity.TemplateId,
        Name = entity.Name,
        StartsAtUtc = entity.StartsAtUtc,
        EndsAtUtc = entity.EndsAtUtc,
        Priority = entity.Priority,
        Enabled = entity.Enabled,
        TemplateMissing = !_registry.Exists(entity.TemplateId),
    };
}
