using Microsoft.Extensions.Logging;
using Vora.Application.LibraryMigration.Requests;
using Vora.Application.LibraryMigration.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Plugins.Interfaces;

namespace Vora.Application.LibraryMigration;

public interface ILibraryMigrationManager
{
    IEnumerable<LibrarySyncProviderVM> GetAvailableProviders();
    Task<LibrarySyncPinVM> CreatePinAsync(string providerId, CancellationToken cancellationToken = default);
    Task<LibrarySyncPinStatusVM> PollPinAsync(string providerId, string pinId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteServerVM>> ListServersAsync(string providerId, string accessToken, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteAccountVM>> ListAccountsAsync(string providerId, string accessToken, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteLibraryVM>> ListLibrariesAsync(string providerId, string accessToken, string serverClientIdentifier, CancellationToken cancellationToken = default);
    Task<LibraryMigrationJobVM> StartImportAsync(string providerId, StartLibraryImportRequest request, Guid accountId, Guid profileId, bool isServerAdmin);
}

public class LibraryMigrationManager(
    IEnumerable<ILibrarySyncProvider> providers,
    IUserRepository users,
    ILibraryMigrationJobRunner runner,
    ILogger<LibraryMigrationManager> logger) : ILibraryMigrationManager
{
    public IEnumerable<LibrarySyncProviderVM> GetAvailableProviders()
    {
        return providers
            .OrderBy(p => p.Name)
            .Select(p => new LibrarySyncProviderVM
            {
                Id = p.Id,
                Name = p.Name,
                ProviderName = p.ProviderName,
                Description = p.Description
            })
            .ToList();
    }

    public async Task<LibrarySyncPinVM> CreatePinAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerId);
        var pin = await provider.CreatePinAsync(cancellationToken);

        logger.LogInformation("Started library migration PIN flow for provider {ProviderId}.", providerId);

        return new LibrarySyncPinVM
        {
            PinId = pin.PinId,
            Code = pin.Code,
            VerificationUrl = pin.VerificationUrl,
            ExpiresAt = pin.ExpiresAt
        };
    }

    public async Task<LibrarySyncPinStatusVM> PollPinAsync(string providerId, string pinId, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerId);
        var status = await provider.PollPinAsync(pinId, cancellationToken);

        return new LibrarySyncPinStatusVM
        {
            PinId = status.PinId,
            Status = status.Status,
            Token = status.Token is null
                ? null
                : new LibrarySyncTokenVM
                {
                    AccessToken = status.Token.AccessToken,
                    Username = status.Token.Username
                }
        };
    }

    public async Task<IReadOnlyList<RemoteServerVM>> ListServersAsync(string providerId, string accessToken, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerId);
        var servers = await provider.ListServersAsync(accessToken, cancellationToken);

        logger.LogInformation("Fetched {Count} server(s) from {ProviderId}.", servers.Count, providerId);

        return servers
            .Select(s => new RemoteServerVM
            {
                ClientIdentifier = s.ClientIdentifier,
                Name = s.Name,
                IsOwned = s.IsOwned,
                OwnerName = s.OwnerName,
                Platform = s.Platform,
                ProductVersion = s.ProductVersion,
                IsOnline = s.IsOnline,
                Connections = s.Connections
                    .Select(c => new RemoteConnectionVM
                    {
                        Uri = c.Uri,
                        IsLocal = c.IsLocal,
                        IsHttps = c.IsHttps,
                        IsRelay = c.IsRelay
                    })
                    .ToList()
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RemoteAccountVM>> ListAccountsAsync(string providerId, string accessToken, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerId);
        var accounts = await provider.ListAccountsAsync(accessToken, cancellationToken);

        logger.LogInformation("Fetched {Count} account(s) from {ProviderId}.", accounts.Count, providerId);

        return accounts
            .Select(a => new RemoteAccountVM
            {
                Id = a.Id,
                DisplayName = a.DisplayName,
                Kind = a.Kind,
                HasPin = a.HasPin,
                IsSignedIn = a.IsSignedIn,
                AvatarUrl = a.AvatarUrl,
                Email = a.Email
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RemoteLibraryVM>> ListLibrariesAsync(string providerId, string accessToken, string serverClientIdentifier, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerId);
        var connectionUri = await provider.FindReachableConnectionAsync(serverClientIdentifier, accessToken, cancellationToken);
        var libraries = await provider.ListLibrariesAsync(connectionUri, accessToken, cancellationToken);

        logger.LogInformation("Fetched {Count} library section(s) from {ProviderId}.", libraries.Count, providerId);

        return libraries
            .Select(l => new RemoteLibraryVM
            {
                Key = l.Key,
                Name = l.Name,
                Kind = l.Kind
            })
            .ToList();
    }

    public async Task<LibraryMigrationJobVM> StartImportAsync(string providerId, StartLibraryImportRequest request, Guid accountId, Guid profileId, bool isServerAdmin)
    {
        ResolveProvider(providerId);
        if (string.IsNullOrWhiteSpace(request.AccessToken)) throw new ArgumentException("accessToken is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ServerClientIdentifier)) throw new ArgumentException("serverClientIdentifier is required.", nameof(request));
        if (!request.IncludeWatchState && !request.IncludeRatings) throw new ArgumentException("Pick the watch history, the ratings or both.", nameof(request));
        if (request.LibrarySectionKeys.Count == 0) throw new ArgumentException("Pick at least one library.", nameof(request));

        var target = await TargetProfileAsync(request.ProfileId ?? profileId, accountId, profileId);
        var plexAccountId = string.IsNullOrWhiteSpace(request.PlexAccountId) ? LibraryMigrationMappingInput.SignedInAccount : request.PlexAccountId;
        var ownHistoryIntoOwnProfile = target.Id == profileId && plexAccountId == LibraryMigrationMappingInput.SignedInAccount;

        logger.LogInformation("Starting a {ProviderId} import into profile {ProfileId}.", providerId, target.Id);

        return runner.StartJob(new LibraryMigrationJobInput
        {
            ProviderId = providerId,
            AccessToken = request.AccessToken,
            RequestedByAccountId = accountId,
            ServerClientIdentifier = request.ServerClientIdentifier,
            ServerName = request.ServerName,
            IncludeWatchState = request.IncludeWatchState,
            IncludeRatings = request.IncludeRatings,
            LibrarySectionKeys = request.LibrarySectionKeys,
            SetAdminRatings = request.SetAdminRatings && request.IncludeRatings && isServerAdmin && ownHistoryIntoOwnProfile,
            Mappings =
            [
                new LibraryMigrationMappingInput
                {
                    AccountId = plexAccountId,
                    AccountName = FirstNamed(request.PlexAccountName, request.PlexUsername) ?? "Your Plex account",
                    ProfileId = target.Id,
                    ProfileName = target.Name,
                    Pin = string.IsNullOrWhiteSpace(request.Pin) ? null : request.Pin
                }
            ]
        });
    }

    private static string? FirstNamed(params string?[] names) => names.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

    private async Task<UserProfile> TargetProfileAsync(Guid targetId, Guid accountId, Guid callerProfileId)
    {
        var target = await users.GetProfileByIdAsync(targetId);
        if (target is null || target.UserId != accountId)
        {
            throw new UnauthorizedAccessException("That profile isn't on your account.");
        }
        if (targetId == callerProfileId) return target;

        var caller = await users.GetProfileByIdAsync(callerProfileId);
        if (caller is null || !caller.IsAdmin)
        {
            throw new UnauthorizedAccessException("Only the account's main profile can import into another profile.");
        }
        return target;
    }

    private ILibrarySyncProvider ResolveProvider(string providerId)
    {
        var provider = providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            logger.LogWarning("Library migration provider {ProviderId} was requested but is not registered.", providerId);
            throw new KeyNotFoundException($"No library sync provider registered with id '{providerId}'.");
        }
        return provider;
    }
}
