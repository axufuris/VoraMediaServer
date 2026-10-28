import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { mediaService, type Episode, type MediaItem } from '../../../api/Media/mediaService';
import { SelectedEpisodeParam } from '../../../utils/mediaPath';

const DetailsDelayMs = 150;

export interface SeasonEpisodeSelection {
    selectedId: string | null;
    // What the hero describes: the episode's full details once they are in,
    // and its row from the season's list until then.
    selected: MediaItem | null;
    // The full details only, for the file chips and the track picks.
    details: MediaItem | null;
    select: (episode: Episode) => void;
}

const asMediaItem = (season: MediaItem, episode: Episode): MediaItem => ({
    id: episode.id,
    title: episode.title,
    type: 'Episode',
    overview: episode.overview,
    posterUrl: episode.posterUrl,
    releaseDate: episode.releaseDate,
    durationMinutes: episode.durationMinutes,
    seasonNumber: season.seasonNumber,
    episodeNumber: episode.episodeNumber,
    endEpisodeNumber: episode.endEpisodeNumber,
    isPlayed: episode.isPlayed,
    resumePositionSeconds: episode.resumePositionSeconds,
    myRating: episode.myRating,
    serverAdminRating: episode.serverAdminRating,
    tvShowTitle: season.tvShowTitle,
    tvShowId: season.tvShowId,
    seasonId: season.id,
    libraryId: season.libraryId,
    collectionIds: [],
});

// The episode a season page describes, as the TV apps' focused episode. It is
// the one in the address (`?episode=`, where an episode link lands), else the
// season's up-next episode, else its first. Picking another puts it in the
// address, so a reload or Back keeps it. Its full details load a moment after
// it is picked, so stepping along the row with the arrow keys does not fetch
// every episode it passes.
export function useSeasonEpisodeSelection(season: MediaItem | null, upNext: MediaItem | null, upNextResolved: boolean, serverId?: string): SeasonEpisodeSelection {
    const [searchParams, setSearchParams] = useSearchParams();
    const episodes = season?.type === 'Season' ? season.episodes ?? [] : [];
    const requested = searchParams.get(SelectedEpisodeParam);

    const selectedId = episodes.some(episode => episode.id === requested)
        ? requested
        : upNextResolved
            ? (upNext && episodes.some(episode => episode.id === upNext.id) ? upNext.id : episodes[0]?.id ?? null)
            : null;

    const [loaded, setLoaded] = useState<{ forSeason: MediaItem; episode: MediaItem } | null>(null);
    const known = upNext && upNext.id === selectedId ? upNext : null;

    useEffect(() => {
        if (!season || !selectedId || known) return;
        let cancelled = false;
        const timer = window.setTimeout(() => {
            mediaService.getMediaItem(selectedId, serverId)
                .then(episode => { if (!cancelled) setLoaded({ forSeason: season, episode }); })
                .catch(() => { /* the row's own values stay on screen */ });
        }, DetailsDelayMs);
        return () => { cancelled = true; window.clearTimeout(timer); };
    }, [season, selectedId, known, serverId]);

    const details = known ?? (loaded && loaded.forSeason === season && loaded.episode.id === selectedId ? loaded.episode : null);
    const row = season && selectedId ? episodes.find(episode => episode.id === selectedId) : undefined;
    const selected = details ?? (season && row ? asMediaItem(season, row) : null);

    const select = useCallback((episode: Episode) => {
        if (episode.id === searchParams.get(SelectedEpisodeParam)) return;
        setSearchParams(previous => {
            const next = new URLSearchParams(previous);
            next.set(SelectedEpisodeParam, episode.id);
            return next;
        }, { replace: true });
    }, [searchParams, setSearchParams]);

    return { selectedId, selected, details, select };
}
