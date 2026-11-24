using Vora.Application.Users.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Application.Users;

public sealed record PlaybackBehaviour(
    bool AutoSkipIntro,
    bool AutoSkipCredits,
    bool AutoPlayNext,
    UpNextOrder UpNextOrder,
    EpisodeLayout EpisodeLayout,
    int MinimumCreditsSceneSeconds)
{
    public PlaybackBehaviour Updated(PlaybackPreferencesVM prefs)
    {
        if (prefs.UpNextOrder is { } unknownOrder && !Enum.IsDefined(unknownOrder))
        {
            throw new ArgumentException($"'{unknownOrder}' is not an up-next order.", nameof(prefs));
        }
        if (prefs.EpisodeLayout is { } unknownLayout && !Enum.IsDefined(unknownLayout))
        {
            throw new ArgumentException($"'{unknownLayout}' is not an episode layout.", nameof(prefs));
        }

        return new PlaybackBehaviour(
            prefs.AutoSkipIntro,
            prefs.AutoSkipCredits,
            prefs.AutoPlayNext ?? AutoPlayNext,
            prefs.UpNextOrder ?? UpNextOrder,
            prefs.EpisodeLayout ?? EpisodeLayout,
            Math.Clamp(prefs.MinimumCreditsSceneSeconds, 0, 600));
    }

    public PlaybackPreferencesVM ToViewModel() => new()
    {
        AutoSkipIntro = AutoSkipIntro,
        AutoSkipCredits = AutoSkipCredits,
        AutoPlayNext = AutoPlayNext,
        UpNextOrder = UpNextOrder,
        EpisodeLayout = EpisodeLayout,
        MinimumCreditsSceneSeconds = MinimumCreditsSceneSeconds
    };
}

public sealed record LibrarySortChoice(LibrarySortField? Sort, bool Descending)
{
    public static LibrarySortChoice From(LibrarySortVM sort)
    {
        if (sort.Sort is { } field && !Enum.IsDefined(field))
        {
            throw new ArgumentException($"'{field}' is not a library sort.", nameof(sort));
        }

        return new LibrarySortChoice(sort.Sort, sort.Sort.HasValue && sort.Descending);
    }

    public LibrarySortVM ToViewModel() => new()
    {
        Sort = Sort,
        Descending = Descending
    };
}
