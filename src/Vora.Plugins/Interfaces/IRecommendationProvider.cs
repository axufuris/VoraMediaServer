using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IRecommendationProvider : IVoraPlugin
{
    Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, CancellationToken cancellationToken = default);

    // For You never waits on a provider: it shows the rows the provider last
    // built for the profile, and builds new ones in the background. A provider
    // that makes no call to another service, and so costs no more than a few
    // queries, says false here. Its rows are then built while the page waits
    // the first time, so a first visit isn't empty, and rebuilt as soon as the
    // profile watches or rates something. Anything else is only ever built in
    // the background and kept for twelve hours. Plugins that don't say are
    // taken to make outside calls.
    bool MakesOutsideCalls => true;
}
