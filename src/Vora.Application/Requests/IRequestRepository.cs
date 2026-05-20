using Vora.Application.Requests.ViewModels;
using Vora.Domain.Entities.Requests;

namespace Vora.Application.Requests;

public interface IRequestRepository
{
    Task<MediaRequest?> GetRequestAsync(string externalId, string type);
    Task<MediaRequest?> GetRequestByIdAsync(Guid id);
    Task AddRequestAsync(MediaRequest request);
    Task UpdateRequestAsync(MediaRequest request);
    Task DeleteRequestAsync(Guid id);
    Task<List<MediaRequestVM>> GetAllRequestsAsync();

    Task<RequestServerVM?> GetServerAsync(Guid? serverId, string mediaType);
    Task<List<RequestServerVM>> GetAllServersAsync();
    Task<RequestServer> AddServerAsync(RequestServer server);
    Task<RequestServer> UpdateServerAsync(RequestServer server);
    Task DeleteServerAsync(Guid id);
}
