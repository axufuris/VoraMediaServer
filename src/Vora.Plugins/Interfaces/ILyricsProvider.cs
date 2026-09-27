using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ILyricsProvider : IVoraPlugin
{
    Task<LyricsResult?> GetLyricsAsync(string artistName, string trackTitle, string? albumTitle, int? durationSeconds, CancellationToken cancellationToken);
}
