using Vora.Domain.Enums;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Metadata.Local;

public sealed record LocalLibrarySettings(LocalMetadataMode Mode, bool PreferLocalArtwork, string? MetadataProviderId, string? ArtworkProviderId)
{
    public LocalMetadataMode DocumentMode =>
        MetadataProviderId == LocalMetadataProvider.ProviderId ? LocalMetadataMode.Prefer : Mode;

    public bool ReadsArtwork =>
        PreferLocalArtwork || ArtworkProviderId == LocalArtworkProvider.ProviderId;
}
