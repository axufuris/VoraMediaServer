import { type ReactNode } from 'react';
import {
    type AlbumVM,
    type ArtistVM,
    type ArtistTrackVM,
    type GeneratedMixSummaryVM,
    type BecauseYouPlayedRowVM,
    type StationVM,
    type ServerPlaybackSessionVM,
} from '../../../../api/Music/musicService';
import { useDialog } from '../../../../dialogs';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';
import EmptyState from '../../../../components/Client/Primitives/EmptyState';
import { type MusicNavState } from './musicNavState';
import { albumCaption, trackCaption } from './musicCaptions';

type DialogApi = ReturnType<typeof useDialog>;

interface MusicForYouViewProps {
    isLoading: boolean;
    serverPlayback: ServerPlaybackSessionVM[];
    dailyMixes: GeneratedMixSummaryVM[];
    stations: StationVM[];
    becauseYouPlayed: BecauseYouPlayedRowVM[];
    recentlyPlayed: ArtistTrackVM[];
    recentlyAddedAlbums: AlbumVM[];
    topArtists: ArtistVM[];
    topTracks: ArtistTrackVM[];
    likedCount: number;
    availableYears: number[];
    hasAnyHistory: boolean;
    updateNav: (next: MusicNavState) => void;
    playArtistTrackList: (tracks: ArtistTrackVM[], startIndex: number) => void;
    startStationRadio: (station: StationVM) => Promise<void>;
    deleteStation: (stationId: string) => Promise<void>;
    dialog: DialogApi;
}

const trackCountLabel = (count: number) => `${count} ${count === 1 ? 'track' : 'tracks'}`;

function Shortcut({ label, detail, icon, onClick }: { label: string; detail: string; icon: ReactNode; onClick: () => void }) {
    return (
        <button
            type="button"
            onClick={onClick}
            className="vora-row-interactive flex min-w-0 cursor-pointer items-center gap-3 rounded-[var(--vora-radius-md)] border p-2.5 text-left"
            style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-bg-surface)' }}
        >
            <span
                className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-[var(--vora-radius-sm)]"
                style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                aria-hidden="true"
            >
                {icon}
            </span>
            <span className="min-w-0">
                <span className="block truncate text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{label}</span>
                <span className="block truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{detail}</span>
            </span>
        </button>
    );
}

export default function MusicForYouView({
    isLoading,
    serverPlayback,
    dailyMixes,
    stations,
    becauseYouPlayed,
    recentlyPlayed,
    recentlyAddedAlbums,
    topArtists,
    topTracks,
    likedCount,
    availableYears,
    hasAnyHistory,
    updateNav,
    playArtistTrackList,
    startStationRadio,
    deleteStation,
    dialog,
}: MusicForYouViewProps) {
    if (isLoading) {
        return (
            <div className="space-y-8 px-8" aria-busy="true">
                <div className="vora-skeleton h-14" />
                <div className="vora-skeleton h-40" />
                <div className="vora-skeleton h-40" />
            </div>
        );
    }

    if (recentlyAddedAlbums.length === 0) {
        return (
            <EmptyState
                title="No music in your library yet"
                description="Create a Music library in Server Settings, point it at a folder of audio files, then trigger a scan."
            />
        );
    }

    const madeForYou = dailyMixes.filter(m => m.kind === 'DailyMix' || m.kind === 'DiscoverMix');
    const releaseRadar = dailyMixes.filter(m => m.kind === 'ReleaseRadar');
    const moods = dailyMixes.filter(m => m.kind === 'MoodMix');
    const recapYear = availableYears[0] ?? new Date().getFullYear();

    const mixRow = (title: string, mixes: GeneratedMixSummaryVM[], kicker: (mix: GeneratedMixSummaryVM) => string, badge?: ReactNode) => mixes.length > 0 && (
        <MediaRow title={title}>
            {mixes.map(mix => (
                <MediaRowItem key={mix.id}>
                    <MediaCard
                        title={mix.name}
                        captionLines={[kicker(mix), trackCountLabel(mix.trackCount)]}
                        imageUrl={mix.artworkUrl}
                        shape="square"
                        size="xs"
                        badge={badge}
                        onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                    />
                </MediaRowItem>
            ))}
        </MediaRow>
    );

    return (
        <div className="space-y-8">
            {serverPlayback.length > 0 && (
                <MediaRow title="Listening Now">
                    {serverPlayback.map(s => (
                        <MediaRowItem key={s.profileId}>
                            <div
                                className="flex w-[18rem] items-center gap-3 rounded-[var(--vora-radius-md)] border p-3"
                                style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-bg-surface)' }}
                                title={`${s.profileName} is listening to ${s.trackTitle}${s.artist ? ` by ${s.artist}` : ''}`}
                            >
                                <div className="h-12 w-12 shrink-0 overflow-hidden rounded-[var(--vora-radius-sm)]" style={{ background: 'var(--vora-bg-sunken)' }}>
                                    {s.albumArtworkUrl && <img src={s.albumArtworkUrl} alt="" className="h-full w-full object-cover" />}
                                </div>
                                <div className="min-w-0 flex-1">
                                    <div className="flex min-w-0 items-center gap-2">
                                        <span className="inline-block h-2 w-2 shrink-0 animate-pulse rounded-full" style={{ background: 'var(--vora-accent-500)' }} aria-hidden="true" />
                                        <span className="truncate text-xs font-semibold" style={{ color: 'var(--vora-accent-text)' }}>{s.profileName}</span>
                                    </div>
                                    <div className="mt-0.5 truncate text-sm" style={{ color: 'var(--vora-text-primary)' }}>{s.trackTitle}</div>
                                    <div className="truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{s.artist ?? s.albumTitle ?? ''}</div>
                                </div>
                            </div>
                        </MediaRowItem>
                    ))}
                </MediaRow>
            )}

            <section className="px-8" aria-label="Your library">
                <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                    <Shortcut
                        label="Liked Songs"
                        detail={trackCountLabel(likedCount)}
                        onClick={() => updateNav({ view: 'likes' })}
                        icon={<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>}
                    />
                    <Shortcut
                        label="Your Top Tracks"
                        detail={topTracks.length > 0 ? trackCountLabel(topTracks.length) : 'Build a history first'}
                        onClick={() => updateNav({ view: 'top' })}
                        icon={<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1l3 6h6l-5 4 2 7-6-4-6 4 2-7-5-4h6z" /></svg>}
                    />
                    <Shortcut
                        label="Browse by Genre"
                        detail="Discover by mood"
                        onClick={() => updateNav({ view: 'genres' })}
                        icon={<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75}><rect x="3.5" y="3.5" width="7" height="7" rx="1.5" /><rect x="13.5" y="3.5" width="7" height="7" rx="1.5" /><rect x="3.5" y="13.5" width="7" height="7" rx="1.5" /><rect x="13.5" y="13.5" width="7" height="7" rx="1.5" /></svg>}
                    />
                    {hasAnyHistory && (
                        <Shortcut
                            label="Year in Music"
                            detail={String(recapYear)}
                            onClick={() => updateNav({ view: 'recap', year: recapYear })}
                            icon={<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round"><rect x="3" y="4.5" width="18" height="16" rx="2" /><line x1="3" y1="9.5" x2="21" y2="9.5" /><line x1="8" y1="2.5" x2="8" y2="6.5" /><line x1="16" y1="2.5" x2="16" y2="6.5" /></svg>}
                        />
                    )}
                </div>
            </section>

            {mixRow('Made for You', madeForYou, mix => mix.kind === 'DiscoverMix' ? 'Discover' : `Daily Mix ${mix.slot}`)}

            {mixRow('Release Radar', releaseRadar, mix => mix.descriptionTag ?? 'New releases', (
                <span
                    className="rounded px-1.5 py-0.5 font-bold uppercase tracking-widest"
                    style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', fontSize: 'var(--vora-card-badge-size)' }}
                >
                    New
                </span>
            ))}

            {mixRow('Moods', moods, mix => mix.descriptionTag ?? 'Mood')}

            {stations.length > 0 && (
                <MediaRow title="Your Stations">
                    {stations.map(station => (
                        <MediaRowItem key={station.id}>
                            <MediaCard
                                title={station.name}
                                captionLines={station.subtitleHint ? [station.subtitleHint] : ['Station']}
                                imageUrl={station.artworkUrl}
                                shape="square"
                                size="xs"
                                onClick={() => { void startStationRadio(station); }}
                                onDelete={async (e) => {
                                    e.stopPropagation();
                                    if (await dialog.confirm(`Delete station "${station.name}"?`)) {
                                        void deleteStation(station.id);
                                    }
                                }}
                            />
                        </MediaRowItem>
                    ))}
                </MediaRow>
            )}

            {becauseYouPlayed.map(row => (
                <MediaRow key={row.seedArtistId} title={row.heading}>
                    {row.tracks.map((t, idx) => (
                        <MediaRowItem key={t.id}>
                            <MediaCard
                                item={trackCaption(t)}
                                imageUrl={t.albumArtworkUrl}
                                shape="square"
                                size="xs"
                                onClick={() => playArtistTrackList(row.tracks, idx)}
                            />
                        </MediaRowItem>
                    ))}
                </MediaRow>
            ))}

            {recentlyPlayed.length > 0 && (
                <MediaRow title="Recently Played">
                    {recentlyPlayed.map((t, idx) => (
                        <MediaRowItem key={t.id}>
                            <MediaCard
                                item={trackCaption(t)}
                                imageUrl={t.albumArtworkUrl}
                                shape="square"
                                size="xs"
                                onClick={() => playArtistTrackList(recentlyPlayed, idx)}
                            />
                        </MediaRowItem>
                    ))}
                </MediaRow>
            )}

            <MediaRow title="Recently Added">
                {recentlyAddedAlbums.map(album => (
                    <MediaRowItem key={album.id}>
                        <MediaCard
                            item={albumCaption(album)}
                            imageUrl={album.artworkUrl}
                            shape="square"
                            size="xs"
                            onClick={() => updateNav({ view: 'album', artistId: album.artistId, albumId: album.id })}
                        />
                    </MediaRowItem>
                ))}
            </MediaRow>

            {topArtists.length > 0 && (
                <MediaRow title="Top Artists">
                    {topArtists.map(artist => (
                        <MediaRowItem key={artist.id}>
                            <MediaCard
                                item={{ type: 'Artist', title: artist.name }}
                                imageUrl={artist.artworkUrl}
                                shape="circle"
                                size="xs"
                                onClick={() => updateNav({ view: 'artist', artistId: artist.id })}
                            />
                        </MediaRowItem>
                    ))}
                </MediaRow>
            )}
        </div>
    );
}
