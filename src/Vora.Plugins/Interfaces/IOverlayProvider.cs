using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IOverlayProvider : IVoraPlugin
{
    Task<string> GenerateOverlayAsync(OverlayMediaDto item, string originalArtworkPath, string templateJson, string outputDirectory, CancellationToken cancellationToken = default);

    // Bumped whenever the compositing layout/rendering changes. An item stamped
    // with an older version is re-generated once so a layout change rolls out to
    // every already-overlaid item, not just the ones that happen to be touched
    // again. Distinct from the template CONFIG, which callers version separately.
    int LayoutVersion => 1;
}
