using System.Linq.Expressions;

namespace Vora.Domain.Entities.Media;

// What a media item can DO, named once, instead of each call site naming the
// classes that happen to be able to do it today.
//
// The guards these replace were all of the form `m is Movie || m is TvShow`, and
// the problem with that shape is not verbosity: it is that the call site never
// says WHICH of the several unrelated things that set has in common it actually
// depends on. Adding a media type then means finding every such guard and
// working out, one by one, whether the new type belongs in it. That is how a
// music track came to look permanently un-enriched — `!(m is Track)` was
// standing in for "a metadata provider will answer for this", and nothing said so.
//
// Each capability is an Expression, not a method, because most of these guards
// live inside IQueryable predicates that EF Core translates to SQL. A bool
// extension method cannot be translated and fails at RUN time, not build time.
// The compiled Func beside each one serves callers holding an entity rather than
// building a query, so both readings come from a single definition.
//
// Two capabilities with the same membership today are still two capabilities.
// HasProviderIdentity and IsBrowsableTitle are both Movie and TvShow, and the
// claim that they would gain a member together is one worth not making: albums
// becoming browsable would move one and not the other.
public static class MediaCapabilities
{
    // Carries its own identity at a metadata provider — TMDB, IMDb, TVDB ids —
    // so it can be searched for, matched, re-matched and scored. A season or an
    // episode is enriched THROUGH its show rather than looked up on its own.
    public static readonly Expression<Func<MediaItem, bool>> HasProviderIdentity =
        m => m is Movie || m is TvShow;

    // The generic metadata pipeline can do something with this. Broader than
    // HasProviderIdentity: a season gets its poster and an episode its title from
    // the show's mapping, so both belong, while a track's metadata comes from its
    // tags and its artwork from MusicManager, so it does not.
    public static readonly Expression<Func<MediaItem, bool>> SupportsMetadataEnrichment =
        m => !(m is Track);

    // Appears as a top-level entry in browse surfaces — genre rows, smart lists,
    // recommendations. An episode is reached through its show; a track through
    // its album.
    public static readonly Expression<Func<MediaItem, bool>> IsBrowsableTitle =
        m => m is Movie || m is TvShow;

    // Owns MediaParts: a file on disk, a runtime, something to stream. The
    // containers above it own none.
    public static readonly Expression<Func<MediaItem, bool>> HasPlayableParts =
        m => m is Movie || m is Episode || m is Track;

    // A vanished file trashes this rather than deleting it, so its ratings and
    // watch state survive until the retention window closes. Music is the
    // exception: a Track goes immediately, which is why Media Trash is
    // video-only.
    public static readonly Expression<Func<MediaItem, bool>> IsSoftDeletable =
        m => !(m is Track);

    // A playable leaf whose file is VIDEO. Narrower than HasPlayableParts, which
    // includes music: scrub-bar thumbnails, black-frame and silence analysis all
    // need frames, and a track has none.
    public static readonly Expression<Func<MediaItem, bool>> IsPlayableVideo =
        m => m is Movie || m is Episode;

    // Sits somewhere in a show's tree — the show, one of its seasons, or one of
    // its episodes. A classification rather than an ability, and the only one
    // here, but it fails the same way the others do when written inline: history
    // filtering spelled it as three ORs and history projection spelled it as the
    // same three in a different order with Movie as the else, so the two could
    // disagree about a type neither had been updated for.
    public static readonly Expression<Func<MediaItem, bool>> IsPartOfATvShow =
        m => m is TvShow || m is Season || m is Episode;

    // Asks a capability of a MediaItem reached through a navigation property, by
    // rebinding the parameter rather than invoking the expression — EF cannot
    // translate an Invoke, so `e => IsBrowsableTitle.Compile()(e.MediaItem)`
    // throws at run time. Without this, any query that filters something OWNING
    // a MediaItem has to inline the guard, and an inlined guard is one the
    // vocabulary no longer covers.
    public static Expression<Func<T, bool>> On<T>(
        this Expression<Func<MediaItem, bool>> capability,
        Expression<Func<T, MediaItem>> path)
    {
        var rebound = new ParameterRebinder(capability.Parameters[0], path.Body).Visit(capability.Body);
        return Expression.Lambda<Func<T, bool>>(rebound, path.Parameters[0]);
    }

    private sealed class ParameterRebinder(ParameterExpression target, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == target ? replacement : base.VisitParameter(node);
    }

    private static readonly Func<MediaItem, bool> HasProviderIdentityFunc = HasProviderIdentity.Compile();
    private static readonly Func<MediaItem, bool> SupportsMetadataEnrichmentFunc = SupportsMetadataEnrichment.Compile();
    private static readonly Func<MediaItem, bool> IsBrowsableTitleFunc = IsBrowsableTitle.Compile();
    private static readonly Func<MediaItem, bool> HasPlayablePartsFunc = HasPlayableParts.Compile();
    private static readonly Func<MediaItem, bool> IsSoftDeletableFunc = IsSoftDeletable.Compile();
    private static readonly Func<MediaItem, bool> IsPlayableVideoFunc = IsPlayableVideo.Compile();
    private static readonly Func<MediaItem, bool> IsPartOfATvShowFunc = IsPartOfATvShow.Compile();

    public static bool CanBeMatchedToAProvider(this MediaItem item) => HasProviderIdentityFunc(item);

    public static bool CanBeEnriched(this MediaItem item) => SupportsMetadataEnrichmentFunc(item);

    public static bool CanBeBrowsedAsATitle(this MediaItem item) => IsBrowsableTitleFunc(item);

    public static bool CanHavePlayableParts(this MediaItem item) => HasPlayablePartsFunc(item);

    public static bool CanBeSoftDeleted(this MediaItem item) => IsSoftDeletableFunc(item);

    public static bool IsPlayableVideoItem(this MediaItem item) => IsPlayableVideoFunc(item);

    public static bool BelongsToATvShow(this MediaItem item) => IsPartOfATvShowFunc(item);
}
