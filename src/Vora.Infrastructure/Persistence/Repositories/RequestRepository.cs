using Microsoft.EntityFrameworkCore;
using Vora.Application.Requests;
using Vora.Application.Requests.ViewModels;
using Vora.Domain.Entities.Requests;

namespace Vora.Infrastructure.Persistence.Repositories;

public class RequestRepository(VoraDbContext context) : IRequestRepository
{
    public Task<MediaRequest?> GetRequestAsync(string externalId, string type) =>
        context.MediaRequests
            .Include(r => r.Requesters)
            .FirstOrDefaultAsync(r => r.ExternalId == externalId && r.Type == type);

    public async Task<Dictionary<string, MediaRequest>> GetRequestsAsync(IEnumerable<string> externalIds, string type)
    {
        var ids = externalIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, MediaRequest>();

        var requests = await context.MediaRequests
            .Include(r => r.Requesters)
            .Where(r => r.Type == type && ids.Contains(r.ExternalId))
            .ToListAsync();

        var map = new Dictionary<string, MediaRequest>();
        foreach (var request in requests)
        {
            map[request.ExternalId] = request;
        }
        return map;
    }

    public Task<MediaRequest?> GetRequestByIdAsync(Guid id) =>
        context.MediaRequests.FindAsync(id).AsTask();

    public async Task AddRequestAsync(MediaRequest request)
    {
        await context.MediaRequests.AddAsync(request);
        await context.SaveChangesAsync();
    }

    public async Task UpdateRequestAsync(MediaRequest request)
    {
        context.MediaRequests.Update(request);
        await context.SaveChangesAsync();
    }

    public Task DeleteRequestAsync(Guid id) =>
        context.MediaRequests
            .Where(r => r.Id == id)
            .ExecuteDeleteAsync();

    public Task SaveChangesAsync() => context.SaveChangesAsync();

    public Task<List<MediaRequestVM>> GetAllRequestsAsync() =>
        context.MediaRequests
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(MediaRequestVM.Projection)
            .ToListAsync();

    public Task<RequestServerVM?> GetServerAsync(Guid? serverId, string mediaType)
    {
        var query = context.RequestServers.AsNoTracking();
        return serverId.HasValue
            ? query.Where(s => s.Id == serverId.Value).Select(RequestServerVM.Projection).FirstOrDefaultAsync()
            : query.Where(s => s.MediaType == mediaType && s.IsDefault).Select(RequestServerVM.Projection).FirstOrDefaultAsync();
    }

    public Task<List<RequestServerVM>> GetAllServersAsync() =>
        context.RequestServers
            .AsNoTracking()
            .Select(RequestServerVM.Projection)
            .ToListAsync();

    public async Task<RequestServer> AddServerAsync(RequestServer server)
    {
        await context.RequestServers.AddAsync(server);
        await context.SaveChangesAsync();
        return server;
    }

    public async Task<RequestServer> UpdateServerAsync(RequestServer server)
    {
        context.RequestServers.Update(server);
        await context.SaveChangesAsync();
        return server;
    }

    public async Task DeleteServerAsync(Guid id)
    {
        var server = await context.RequestServers.FindAsync(id);
        if (server == null)
        {
            return;
        }
        context.RequestServers.Remove(server);
        await context.SaveChangesAsync();
    }
}
