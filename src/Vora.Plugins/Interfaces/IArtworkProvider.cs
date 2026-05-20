using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IArtworkProvider : IVoraPlugin
{

    Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null);
}
