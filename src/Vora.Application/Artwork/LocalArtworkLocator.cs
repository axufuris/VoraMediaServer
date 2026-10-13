using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Metadata.Local;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Artwork;

public interface ILocalArtworkLocator
{
    Task<string?> ResolveAsync(Guid mediaItemId, string fileName);
    Task<string?> ResolveAsync(string? address);
}

public sealed class LocalArtworkLocator(IServiceScopeFactory scopes, ILogger<LocalArtworkLocator> logger) : ILocalArtworkLocator
{
    public async Task<string?> ResolveAsync(Guid mediaItemId, string fileName)
    {
        using var scope = scopes.CreateScope();
        var source = await scope.ServiceProvider.GetRequiredService<IMediaRepository>().GetLocalMediaSourceAsync(mediaItemId);
        if (source == null) return null;

        return await Task.Run(() =>
        {
            var folders = new LocalMediaFolders(logger);
            return LocalMediaPlace.From(source, folders)?.Resolve(fileName, folders);
        });
    }

    public Task<string?> ResolveAsync(string? address) =>
        LocalArtworkAddress.TryParse(address, out var mediaItemId, out var fileName)
            ? ResolveAsync(mediaItemId, fileName)
            : Task.FromResult<string?>(null);
}
