using Microsoft.Extensions.Options;
using System.Text.Json;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins;
using Vora.Plugins.Dtos;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;

namespace Vora.Application.Metadata;

public interface IMetadataMappingService
{
    Task ApplyTextMetadataAsync(MediaItem item, MetadataResult metadata, bool forceOverride, string providerId, string providerName);
    Task<bool> ApplySecondaryDataAsync(MediaItem item, (decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) ratings, List<MediaArtwork> artwork, bool forceOverride);
    Task<bool> ApplyArtworkAsync(MediaItem item, List<MediaArtwork> artworkEntities, bool forceOverride);
    Task<bool> ApplyRatingsAsync(MediaItem item, (decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) ratingsData, bool forceOverride);
}

public class MetadataMappingService : IMetadataMappingService
{
    private readonly IMediaRepository _repository;
    private readonly IMediaArtworkRepository _artworkRepository;
    private readonly IActorRepository _actorRepository;
    private readonly ICollectionRepository _collectionRepository;
    private readonly IReferenceRepository _referenceRepository;
    private readonly ReferenceWriteGate _referenceGate;
    private readonly StoragePathsOptions _storagePaths;

    public MetadataMappingService(
        IMediaRepository repository,
        IMediaArtworkRepository artworkRepository,
        IActorRepository actorRepository,
        ICollectionRepository collectionRepository,
        IReferenceRepository referenceRepository,
        ReferenceWriteGate referenceGate,
        IOptions<StoragePathsOptions> storagePaths)
    {
        _repository = repository;
        _artworkRepository = artworkRepository;
        _actorRepository = actorRepository;
        _collectionRepository = collectionRepository;
        _referenceRepository = referenceRepository;
        _referenceGate = referenceGate;
        _storagePaths = storagePaths.Value;
    }

    public async Task ApplyTextMetadataAsync(MediaItem item, MetadataResult metadata, bool forceOverride, string providerId, string providerName)
    {
        ApplyCoreMetadata(item, metadata, forceOverride, providerName);

        // Shared reference rows (collections, companies, countries, genres,
        // cast/actors, networks) can be created by two parallel workers at once,
        // so serialize their read-create-commit through the gate. Committing
        // inside the gate makes the rows visible to the next worker before it
        // reads, preventing duplicate inserts / unique-constraint collisions.
        await _referenceGate.RunAsync(async () =>
        {
            await ProcessCollectionsAsync(item, metadata);
            await ProcessProductionCompaniesExplicitAsync(item, metadata);
            await ProcessOriginCountriesExplicitAsync(item, metadata);
            await ProcessGenresExplicitAsync(item, metadata, forceOverride);
            await ProcessCastExplicitAsync(item, metadata);
            if (item is TvShow tvNetworks)
            {
                await ProcessTvNetworksExplicitAsync(tvNetworks, metadata);
            }
            await _repository.SaveChangesAsync();
        });

        await ProcessVideosExplicitAsync(item, metadata);

        if (item is TvShow tvShow)
        {
            await ProcessTvSeasonsAsync(tvShow, metadata, forceOverride, providerId);

            if (metadata.UpcomingEpisodes.Any())
            {
                var dtos = metadata.UpcomingEpisodes.Select(e => new Vora.Application.Media.Dtos.UpcomingEpisodeDto
                {
                    SeasonNumber = e.SeasonNumber,
                    EpisodeNumber = e.EpisodeNumber,
                    Title = e.Title,
                    AirDate = e.AirDate
                }).ToList();

                tvShow.UpcomingEpisodesJson = JsonSerializer.Serialize(dtos);
            }
            else
            {
                tvShow.UpcomingEpisodesJson = "[]";
            }
        }
    }

    public async Task<bool> ApplySecondaryDataAsync(MediaItem item, (decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) ratings, List<MediaArtwork> artwork, bool forceOverride)
    {
        bool updatedRatings = await ApplyRatingsAsync(item, ratings, forceOverride);
        bool updatedArtwork = await ApplyArtworkAsync(item, artwork, forceOverride);

        return updatedRatings || updatedArtwork;
    }

    public async Task<bool> ApplyArtworkAsync(MediaItem item, List<MediaArtwork> artworkEntities, bool forceOverride)
    {
        bool updated = false;

        if (artworkEntities.Any())
        {
            var topPoster = artworkEntities.OrderByDescending(a => a.VoteAverage).FirstOrDefault(a => a.Kind == ArtworkKind.Poster);
            var topBackdrop = artworkEntities.OrderByDescending(a => a.VoteAverage).FirstOrDefault(a => a.Kind == ArtworkKind.Backdrop);

            if (topPoster != null && (!item.IsLocked(nameof(item.PosterUrl)) || forceOverride))
            {
                if (item.OriginalPosterUrl != topPoster.Url)
                {
                    CleanupOrphanedOverlay(item);
                    item.OriginalPosterUrl = topPoster.Url;
                    item.PosterUrl = topPoster.Url;
                    updated = true;
                }
            }

            if (topBackdrop != null && (!item.IsLocked(nameof(item.BackgroundUrl)) || forceOverride))
            {
                if (item is Episode)
                {
                    if (item.OriginalPosterUrl != topBackdrop.Url)
                    {
                        CleanupOrphanedOverlay(item);
                        item.OriginalPosterUrl = topBackdrop.Url;
                        item.PosterUrl = topBackdrop.Url;
                        updated = true;
                    }
                }
                else if (item.BackgroundUrl != topBackdrop.Url)
                {
                    item.BackgroundUrl = topBackdrop.Url;
                    updated = true;
                }
            }

            await _artworkRepository.ReplaceMediaArtworkAsync(item.Id, artworkEntities);
        }

        return updated;
    }

    public Task<bool> ApplyRatingsAsync(MediaItem item, (decimal? Rating1, string? Name1, decimal? Rating2, string? Name2) ratingsData, bool forceOverride)
    {
        bool updated = false;

        // A non-null Name means the library has a provider configured for that
        // slot. When configured, this slot is owned by that provider: use its
        // value, or clear any stale value (e.g. the TMDB rating seeded during
        // core-metadata application) when the provider returned nothing — so the
        // poster never shows the wrong rating source.
        if (!item.IsLocked(nameof(item.ThirdPartyRating1)) || forceOverride)
        {
            if (ratingsData.Rating1.HasValue)
            {
                item.ThirdPartyRating1 = ratingsData.Rating1.Value;
                item.ThirdPartyRating1Name = ratingsData.Name1;
                updated = true;
            }
            else if (ratingsData.Name1 != null && (item.ThirdPartyRating1 != null || item.ThirdPartyRating1Name != null))
            {
                item.ThirdPartyRating1 = null;
                item.ThirdPartyRating1Name = null;
                updated = true;
            }
        }
        if (!item.IsLocked(nameof(item.ThirdPartyRating2)) || forceOverride)
        {
            if (ratingsData.Rating2.HasValue)
            {
                item.ThirdPartyRating2 = ratingsData.Rating2.Value;
                item.ThirdPartyRating2Name = ratingsData.Name2;
                updated = true;
            }
            else if (ratingsData.Name2 != null && (item.ThirdPartyRating2 != null || item.ThirdPartyRating2Name != null))
            {
                item.ThirdPartyRating2 = null;
                item.ThirdPartyRating2Name = null;
                updated = true;
            }
        }

        return Task.FromResult(updated);
    }

    private void ApplyCoreMetadata(MediaItem item, MetadataResult metadata, bool forceOverride, string providerName)
    {
        item.LastMetadataRefresh = DateTime.UtcNow;

        if (!item.IsLocked(nameof(item.Title)) || forceOverride) item.Title = metadata.Title ?? item.Title;
        if (!item.IsLocked(nameof(item.OriginalTitle)) || forceOverride) item.OriginalTitle = metadata.OriginalTitle ?? item.OriginalTitle;
        if (!item.IsLocked(nameof(item.OriginalLanguage)) || forceOverride) item.OriginalLanguage = metadata.OriginalLanguage ?? item.OriginalLanguage;
        if (!item.IsLocked(nameof(item.SortTitle)) || forceOverride) item.SortTitle = metadata.Title ?? item.OriginalTitle;
        if (!item.IsLocked(nameof(item.Status)) || forceOverride) item.Status = metadata.Status ?? item.Status;
        if (!item.IsLocked(nameof(item.Tagline)) || forceOverride) item.Tagline = metadata.Tagline ?? item.Tagline;
        if (!item.IsLocked(nameof(item.Overview)) || forceOverride) item.Overview = metadata.Overview ?? item.Overview;
        if (!item.IsLocked(nameof(item.ReleaseDate)) || forceOverride) item.ReleaseDate = metadata.ReleaseDate ?? item.ReleaseDate;
        if (!item.IsLocked(nameof(item.HomePage)) || forceOverride) item.HomePage = metadata.HomePage ?? item.HomePage;
        if (!item.IsLocked(nameof(item.TmdbId)) || forceOverride) item.TmdbId = metadata.TmdbId ?? item.TmdbId;
        if (!item.IsLocked(nameof(item.ImdbId)) || forceOverride) item.ImdbId = metadata.ImdbId ?? item.ImdbId;
        if (!item.IsLocked(nameof(item.TvdbId)) || forceOverride) item.TvdbId = metadata.TvdbId ?? item.TvdbId;
        if (!item.IsLocked(nameof(item.IsAdult)) || forceOverride) item.IsAdult = metadata.IsAdult;

        item.HasMidCreditsStinger = metadata.HasMidCreditsStinger;
        item.HasPostCreditsStinger = metadata.HasPostCreditsStinger;

        if (!item.IsLocked(nameof(item.PosterUrl)) || forceOverride)
        {
            if (metadata.PosterUrl != null && metadata.PosterUrl != item.OriginalPosterUrl)
            {
                CleanupOrphanedOverlay(item);
                item.OriginalPosterUrl = metadata.PosterUrl;
                item.PosterUrl = metadata.PosterUrl;
            }
        }

        if (!item.IsLocked(nameof(item.BackgroundUrl)) || forceOverride)
        {
            if (metadata.BackgroundUrl != null)
            {
                if (item is Episode)
                {
                    if (metadata.BackgroundUrl != item.OriginalPosterUrl)
                    {
                        CleanupOrphanedOverlay(item);
                        item.OriginalPosterUrl = metadata.BackgroundUrl;
                        item.PosterUrl = metadata.BackgroundUrl;
                    }
                }
                else
                {
                    item.BackgroundUrl = metadata.BackgroundUrl;
                }
            }
        }

        if (!item.IsLocked("Duration") || forceOverride)
        {
            if (metadata.RuntimeMinutes.HasValue)
            {
                item.Analysis ??= new MediaItemAnalysis { MediaItemId = item.Id };
                item.Analysis.Duration = TimeSpan.FromMinutes(metadata.RuntimeMinutes.Value);
            }
        }

        if (!item.IsLocked(nameof(item.ContentRating)) || forceOverride)
        {
            item.ContentRating = metadata.ContentRating ?? item.ContentRating;
            if (item is Episode ep && ep.Season?.TvShow != null && string.IsNullOrEmpty(item.ContentRating))
            {
                item.ContentRating = ep.Season.TvShow.ContentRating;
            }
        }

        // Ratings are NOT seeded from the metadata provider (TMDB/TVDB). Both
        // rating slots are owned entirely by the library's configured Third Party
        // Rating providers, so movies and shows behave identically — pick "TMDB
        // Ratings" in the dropdown to use TMDB's score as a real rating source.

        if (item is Movie movieItem)
        {
            if (!movieItem.IsLocked(nameof(movieItem.Budget)) || forceOverride) movieItem.Budget = metadata.Budget ?? movieItem.Budget;
            if (!movieItem.IsLocked(nameof(movieItem.Revenue)) || forceOverride) movieItem.Revenue = metadata.Revenue ?? movieItem.Revenue;
        }

        if (item is TvShow tvItem)
        {
            if (!tvItem.IsLocked(nameof(tvItem.InProduction)) || forceOverride) tvItem.InProduction = metadata.InProduction ?? tvItem.InProduction;
            if (!tvItem.IsLocked(nameof(tvItem.TvType)) || forceOverride) tvItem.TvType = metadata.TvType ?? tvItem.TvType;
            if (!tvItem.IsLocked(nameof(tvItem.NumberOfEpisodes)) || forceOverride) tvItem.NumberOfEpisodes = metadata.NumberOfEpisodes ?? tvItem.NumberOfEpisodes;
            if (!tvItem.IsLocked(nameof(tvItem.NumberOfSeasons)) || forceOverride) tvItem.NumberOfSeasons = metadata.NumberOfSeasons ?? tvItem.NumberOfSeasons;
            if (!tvItem.IsLocked(nameof(tvItem.LastAirDate)) || forceOverride) tvItem.LastAirDate = metadata.LastAirDate ?? tvItem.LastAirDate;
            if (!tvItem.IsLocked(nameof(tvItem.NextAirDate)) || forceOverride) tvItem.NextAirDate = metadata.NextAirDate ?? tvItem.NextAirDate;
            if (!tvItem.IsLocked(nameof(tvItem.LastEpisodeToAirName)) || forceOverride) tvItem.LastEpisodeToAirName = metadata.LastEpisodeToAirName ?? tvItem.LastEpisodeToAirName;
            if (!tvItem.IsLocked(nameof(tvItem.NextEpisodeToAirName)) || forceOverride) tvItem.NextEpisodeToAirName = metadata.NextEpisodeToAirName ?? tvItem.NextEpisodeToAirName;
        }
    }

    private async Task ProcessCollectionsAsync(MediaItem item, MetadataResult metadata)
    {
        if (metadata.Collection == null) return;

        var existingCollection = await _collectionRepository.GetCollectionByTmdbIdAsync(metadata.Collection.Id, item.LibraryId);
        if (existingCollection == null)
        {
            existingCollection = new Collection
            {
                TmdbId = metadata.Collection.Id,
                Title = metadata.Collection.Name,
                PosterUrl = metadata.Collection.PosterUrl,
                BackdropUrl = metadata.Collection.BackdropUrl,
                LibraryId = item.LibraryId,
                SystemGenerated = true,
                DefaultSort = CollectionSortOrder.ReleaseDateAsc
            };
            await _collectionRepository.AddCollectionAsync(existingCollection);
        }

        if (!item.Collections.Any(c => c.TmdbId == existingCollection.TmdbId))
        {
            item.Collections.Add(existingCollection);
        }
    }

    private async Task ProcessProductionCompaniesExplicitAsync(MediaItem item, MetadataResult metadata)
    {
        if (!metadata.ProductionCompanies.Any()) return;

        var tmdbIds = metadata.ProductionCompanies.Select(c => c.Id).Distinct().ToList();
        var existingComps = await _referenceRepository.GetCompaniesByTmdbIdsAsync(tmdbIds);
        var existingCompDict = existingComps.ToDictionary(c => c.Id);

        var newCompsToSave = new List<Company>();
        foreach (var comp in metadata.ProductionCompanies)
        {
            if (!existingCompDict.TryGetValue(comp.Id, out var company))
            {
                company = new Company { Id = comp.Id, Name = comp.Name, LogoPath = comp.LogoPath, OriginCountry = comp.OriginCountry };
                newCompsToSave.Add(company);
            }
        }

        if (newCompsToSave.Any()) await _referenceRepository.AddCompaniesAsync(newCompsToSave);
        await _repository.SetMediaCompaniesAsync(item.Id, metadata.ProductionCompanies.Select(c => c.Id));
    }

    private async Task ProcessOriginCountriesExplicitAsync(MediaItem item, MetadataResult metadata)
    {
        if (!metadata.OriginCountries.Any()) return;

        var isoCodes = metadata.OriginCountries.Select(c => c.IsoCode).Distinct().ToList();
        var existingCountries = await _referenceRepository.GetCountriesByIsoCodesAsync(isoCodes);
        var existingCountryDict = existingCountries.ToDictionary(c => c.Iso3166_1);

        var newCountriesToSave = new List<Country>();
        foreach (var ctry in metadata.OriginCountries)
        {
            if (!existingCountryDict.TryGetValue(ctry.IsoCode, out var country))
            {
                country = new Country { Iso3166_1 = ctry.IsoCode, Name = ctry.Name };
                newCountriesToSave.Add(country);
            }
        }

        if (newCountriesToSave.Any()) await _referenceRepository.AddCountriesAsync(newCountriesToSave);
        await _repository.SetMediaCountriesAsync(item.Id, metadata.OriginCountries.Select(c => c.IsoCode));
    }

    private async Task ProcessGenresExplicitAsync(MediaItem item, MetadataResult metadata, bool forceOverride)
    {
        if (!item.IsLocked(nameof(item.Genres)) || forceOverride)
        {
            if (metadata.GenreIds.Any())
            {
                await _repository.SetMediaGenresAsync(item.Id, metadata.GenreIds);
            }
        }
    }

    private async Task ProcessCastExplicitAsync(MediaItem item, MetadataResult metadata)
    {
        if (!metadata.Cast.Any()) return;

        var uniqueIncomingCast = metadata.Cast
            .GroupBy(c => c.TmdbId != 0 ? $"tmdb:{c.TmdbId}" : c.TvdbId != 0 ? $"tvdb:{c.TvdbId}" : c.Name.ToLower())
            .Select(g => new CastMemberResult
            {
                TmdbId = g.First().TmdbId,
                TvdbId = g.First().TvdbId,
                Name = g.First().Name,
                ProfileImageUrl = g.FirstOrDefault(c => c.ProfileImageUrl != null)?.ProfileImageUrl,
                Roles = g.Aggregate(CastRole.None, (acc, c) => acc | c.Roles),
                CharacterName = string.Join(" / ", g.Select(c => c.CharacterName).Where(c => !string.IsNullOrEmpty(c)).Distinct())
            })
            .ToList();

        var tmdbIds = uniqueIncomingCast.Where(c => c.TmdbId != 0).Select(c => c.TmdbId).ToList();
        var names = uniqueIncomingCast.Select(c => c.Name).ToList();
        var existingActors = await _actorRepository.GetActorsByTmdbIdsOrNamesAsync(tmdbIds, names);

        var actorsByTmdbId = existingActors.Where(a => a.TmdbId != 0).GroupBy(a => a.TmdbId).ToDictionary(g => g.Key, g => g.First());
        var actorsByTvdbId = existingActors.Where(a => a.TvdbId != 0).GroupBy(a => a.TvdbId).ToDictionary(g => g.Key, g => g.First());
        var actorsByName = existingActors.GroupBy(a => a.Name.ToLower()).ToDictionary(g => g.Key, g => g.First());

        var newActorsToSave = new List<Actor>();
        var finalActorsList = new List<Actor>();

        foreach (var castResult in uniqueIncomingCast)
        {
            Actor? actor = null;
            if (castResult.TmdbId != 0) actorsByTmdbId.TryGetValue(castResult.TmdbId, out actor);
            if (actor == null && castResult.TvdbId != 0) actorsByTvdbId.TryGetValue(castResult.TvdbId, out actor);
            if (actor == null) actorsByName.TryGetValue(castResult.Name.ToLower(), out actor);

            if (actor == null)
            {
                actor = new Actor
                {
                    Id = Guid.NewGuid(),
                    TmdbId = castResult.TmdbId,
                    TvdbId = castResult.TvdbId,
                    Name = castResult.Name,
                    ProfileImageUrl = castResult.ProfileImageUrl
                };
                newActorsToSave.Add(actor);
                actorsByName[castResult.Name.ToLower()] = actor;
                if (castResult.TmdbId != 0) actorsByTmdbId[castResult.TmdbId] = actor;
                if (castResult.TvdbId != 0) actorsByTvdbId[castResult.TvdbId] = actor;
            }
            else if (!actor.IsCustom)
            {
                actor.Name = castResult.Name;
                actor.ProfileImageUrl = castResult.ProfileImageUrl ?? actor.ProfileImageUrl;

                // A person first seen through one provider can be matched by
                // name through the other later; record the id so they become
                // enrichable rather than staying stuck without one.
                if (actor.TmdbId == 0 && castResult.TmdbId != 0)
                {
                    actor.TmdbId = castResult.TmdbId;
                    actorsByTmdbId[castResult.TmdbId] = actor;
                }
                if (actor.TvdbId == 0 && castResult.TvdbId != 0)
                {
                    actor.TvdbId = castResult.TvdbId;
                    actorsByTvdbId[castResult.TvdbId] = actor;
                }
            }

            finalActorsList.Add(actor);
        }

        if (newActorsToSave.Any())
        {
            await _actorRepository.AddActorsAsync(newActorsToSave.DistinctBy(a => a.Id).ToList());
        }

        var existingCastDict = item.Cast
            .Where(c => c.ActorId != Guid.Empty)
            .GroupBy(c => c.ActorId)
            .ToDictionary(g => g.Key, g => g.First());

        var incomingMatchKeys = new HashSet<Guid>();
        var linksToAdd = new List<MediaCastMember>();
        var processedActorIds = new HashSet<Guid>();

        for (int i = 0; i < finalActorsList.Count; i++)
        {
            var actor = finalActorsList[i];
            var castResult = uniqueIncomingCast[i];

            if (processedActorIds.Contains(actor.Id)) continue;
            processedActorIds.Add(actor.Id);

            incomingMatchKeys.Add(actor.Id);

            if (existingCastDict.TryGetValue(actor.Id, out var existingJunction))
            {
                if (existingJunction.CharacterName != castResult.CharacterName || existingJunction.Order != i || existingJunction.Roles != (MediaCastRole)castResult.Roles)
                {
                    existingJunction.CharacterName = castResult.CharacterName;
                    existingJunction.Order = i;
                    existingJunction.Roles = (MediaCastRole)castResult.Roles;
                }
            }
            else
            {
                linksToAdd.Add(new MediaCastMember
                {
                    ActorId = actor.Id,
                    MediaItemId = item.Id,
                    CharacterName = castResult.CharacterName,
                    Order = i,
                    Roles = (MediaCastRole)castResult.Roles
                });
            }
        }

        var linksToRemove = item.Cast.Where(c => !incomingMatchKeys.Contains(c.ActorId)).ToList();

        if (linksToRemove.Any()) await _repository.RemoveMediaCastMembersAsync(linksToRemove);
        if (linksToAdd.Any()) await _repository.AddMediaCastMembersAsync(linksToAdd);
    }

    private async Task ProcessVideosExplicitAsync(MediaItem item, MetadataResult metadata)
    {
        if (item.Library == null) return;

        if (item.Library.FindExtras)
        {
            var filteredVideos = metadata.Videos.AsEnumerable();
            if (item.Library.OnlyShowTrailers)
            {
                filteredVideos = filteredVideos.Where(v => v.Type.Equals("Trailer", StringComparison.OrdinalIgnoreCase));
            }

            var sortedVideos = filteredVideos.OrderByDescending(v => v.Type == "Trailer").ThenByDescending(v => v.IsOfficial).ToList();

            if (sortedVideos.Any() || item.Videos.Any())
            {
                var existingVideoDict = item.Videos.ToDictionary(v => v.VideoKey);
                var processedVideoKeys = new HashSet<string>();
                var videosToAdd = new List<MediaVideo>();

                foreach (var vid in sortedVideos)
                {
                    if (processedVideoKeys.Contains(vid.Key)) continue;
                    processedVideoKeys.Add(vid.Key);

                    if (existingVideoDict.TryGetValue(vid.Key, out var existingVid))
                    {
                        existingVid.Name = vid.Name;
                        existingVid.Site = vid.Site;
                        existingVid.Type = vid.Type;
                        existingVid.IsOfficial = vid.IsOfficial;
                    }
                    else
                    {
                        videosToAdd.Add(new MediaVideo { VideoKey = vid.Key, Name = vid.Name, Site = vid.Site, Type = vid.Type, IsOfficial = vid.IsOfficial, MediaItemId = item.Id });
                    }
                }

                var videosToRemove = item.Videos.Where(v => !processedVideoKeys.Contains(v.VideoKey)).ToList();

                if (videosToRemove.Any()) await _repository.RemoveMediaVideosAsync(videosToRemove);
                if (videosToAdd.Any()) await _repository.AddMediaVideosAsync(videosToAdd);
            }
        }
        else if (item.Videos.Any())
        {
            var videosToRemove = item.Videos.ToList();
            await _repository.RemoveMediaVideosAsync(videosToRemove);
        }
    }

    private async Task ProcessTvNetworksExplicitAsync(TvShow tvItem, MetadataResult metadata)
    {
        if (metadata.Networks.Any())
        {
            var netIds = metadata.Networks.Select(n => n.Id).Distinct().ToList();
            var existingNets = await _referenceRepository.GetNetworksByTmdbIdsAsync(netIds);
            var existingNetDict = existingNets.ToDictionary(n => n.Id);
            var newNetsToSave = new List<Network>();

            foreach (var net in metadata.Networks)
            {
                if (!existingNetDict.TryGetValue(net.Id, out var network))
                {
                    network = new Network { Id = net.Id, Name = net.Name, LogoPath = net.LogoPath, OriginCountry = net.OriginCountry };
                    newNetsToSave.Add(network);
                }
            }

            if (newNetsToSave.Any()) await _referenceRepository.AddNetworksAsync(newNetsToSave);
            await _repository.SetTvNetworksAsync(tvItem.Id, metadata.Networks.Select(n => n.Id));
        }
    }

    private async Task ProcessTvSeasonsAsync(TvShow tvItem, MetadataResult metadata, bool forceOverride, string providerId)
    {
        if (!metadata.Seasons.Any()) return;

        var existingSeasonDict = tvItem.Seasons.ToDictionary(s => s.SeasonNumber);

        foreach (var parsedSeason in metadata.Seasons)
        {
            if (existingSeasonDict.TryGetValue(parsedSeason.SeasonNumber, out var season))
            {
                string? tmdbId = providerId != "tvdb_metadata" ? parsedSeason.Id.ToString() : null;
                string? tvdbId = providerId == "tvdb_metadata" ? parsedSeason.Id.ToString() : null;

                if (!season.IsLocked(nameof(season.TmdbId)) || forceOverride) season.TmdbId = tmdbId ?? season.TmdbId;
                if (!season.IsLocked(nameof(season.TvdbId)) || forceOverride) season.TvdbId = tvdbId ?? season.TvdbId;
                if (!season.IsLocked(nameof(season.Title)) || forceOverride) season.Title = parsedSeason.Name ?? $"Season {parsedSeason.SeasonNumber}";
                if (!season.IsLocked(nameof(season.OriginalTitle)) || forceOverride) season.OriginalTitle = parsedSeason.Name ?? $"Season {parsedSeason.SeasonNumber}";
                if (!season.IsLocked(nameof(season.SortTitle)) || forceOverride) season.SortTitle = parsedSeason.Name ?? $"Season {parsedSeason.SeasonNumber}";
                if (!season.IsLocked(nameof(season.Overview)) || forceOverride) season.Overview = parsedSeason.Overview;
                if (!season.IsLocked(nameof(season.PosterUrl)) || forceOverride) season.PosterUrl = parsedSeason.PosterUrl;
                if (!season.IsLocked(nameof(season.ReleaseDate)) || forceOverride) season.ReleaseDate = parsedSeason.AirDate;
                if (!season.IsLocked(nameof(season.EpisodeCount)) || forceOverride) season.EpisodeCount = parsedSeason.EpisodeCount;
                if (!season.IsLocked(nameof(season.VoteAverage)) || forceOverride) season.VoteAverage = parsedSeason.VoteAverage;

                season.LastMetadataRefresh = DateTime.UtcNow;

                if (!season.IsLocked(nameof(season.ThirdPartyRating1)) || forceOverride)
                {
                    if (parsedSeason.VoteAverage.HasValue && parsedSeason.VoteAverage > 0)
                    {
                        season.ThirdPartyRating1 = (decimal)parsedSeason.VoteAverage.Value;
                        season.ThirdPartyRating1Name = providerId == "tvdb_metadata" ? "TVDB" : "TMDB";
                    }
                }

                await _repository.UpdateMediaItemAsync(season);
            }
        }
    }

    private void CleanupOrphanedOverlay(MediaItem item)
    {
        var configPath = _storagePaths.CustomArtwork;
        var overlayDir = !string.IsNullOrWhiteSpace(configPath) ? configPath : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        var urlsToCheck = new[] { item.PosterUrl, item.BackgroundUrl };

        foreach (var url in urlsToCheck)
        {
            if (!string.IsNullOrEmpty(url) && url.Contains("_overlay_") && url.StartsWith("/api/artwork/custom/"))
            {
                var fileName = url.Split('/').Last();
                var physicalPath = Path.Combine(overlayDir, fileName);
                if (File.Exists(physicalPath))
                {
                    try { File.Delete(physicalPath); } catch { }
                }
            }
        }
    }
}