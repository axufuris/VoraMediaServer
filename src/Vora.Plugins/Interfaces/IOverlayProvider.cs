using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IOverlayProvider : IVoraPlugin
{
    Task<string> GenerateOverlayAsync(OverlayMediaDto item, string originalArtworkPath, string templateJson, string outputDirectory, CancellationToken cancellationToken = default);
}
