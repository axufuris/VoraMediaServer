using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ILibrarySyncProvider : IVoraPlugin
{
    Task<LibrarySyncPinDto> CreatePinAsync(CancellationToken cancellationToken = default);
    Task<LibrarySyncPinStatusDto> PollPinAsync(string pinId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteServerDto>> ListServersAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteAccountDto>> ListAccountsAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<string> ResolveUserTokenAsync(string adminAccessToken, string accountId, string? pin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteLibraryDto>> ListLibrariesAsync(string connectionUri, string accessToken, CancellationToken cancellationToken = default);
    Task<RemoteUserDataDto> FetchUserDataAsync(string connectionUri, string userAccessToken, RemoteSyncScopeDto scope, CancellationToken cancellationToken = default);
}
