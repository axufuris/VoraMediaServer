using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ISubtitleSearchProvider : IVoraPlugin
{
    // False until the admin has entered whatever the source needs. The Find
    // Subtitles UI is hidden entirely when no provider reports available, so an
    // unconfigured provider must say so rather than surfacing a feature that can
    // only fail.
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubtitleSearchResultDto>> SearchAsync(SubtitleSearchQuery query, CancellationToken cancellationToken = default);

    Task<SubtitleDownloadDto?> DownloadAsync(string providerFileId, CancellationToken cancellationToken = default);
}
