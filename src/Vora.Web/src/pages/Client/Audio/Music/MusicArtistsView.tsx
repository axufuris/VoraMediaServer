import {
    type ArtistVM,
    type AlbumVM,
    type ArtistTrackVM,
    type GeneratedMixSummaryVM,
    type BecauseYouPlayedRowVM,
    type StationVM,
    type ServerPlaybackSessionVM,
} from '../../../../api/Music/musicService';
import { useDialog } from '../../../../dialogs';
import { type MusicNavState } from './musicNavState';

type DialogApi = ReturnType<typeof useDialog>;

interface MusicArtistsViewProps {
    isLoading: boolean;
    artists: ArtistVM[];
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
    isServerAdmin: boolean;
    updateNav: (next: MusicNavState) => void;
    playArtistTrackList: (tracks: ArtistTrackVM[], startIndex: number) => void;
    startStationRadio: (station: StationVM) => Promise<void>;
    deleteStation: (stationId: string) => Promise<void>;
    onEditArtist: (artist: ArtistVM) => void;
    dialog: DialogApi;
}

export default function MusicArtistsView({
    isLoading,
    artists,
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
    isServerAdmin,
    updateNav,
    playArtistTrackList,
    startStationRadio,
    deleteStation,
    onEditArtist,
    dialog,
}: MusicArtistsViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading artists...</div>;
    }
    if (artists.length === 0) {
        return (
            <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                <p className="mb-2">No music in your library yet.</p>
                <p className="text-xs">Create a Music library in Server Settings, point it at a folder of audio files, then trigger a scan.</p>
            </div>
        );
    }

    return (
        <>
            {serverPlayback.length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3 flex items-center gap-2">
                        <span className="inline-block w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
                        Listening Now
                    </h2>
                    <div className="flex gap-3 overflow-x-auto pb-2 -mx-2 px-2">
                        {serverPlayback.map(s => (
                            <div
                                key={s.profileId}
                                className="shrink-0 flex items-center gap-3 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-3 min-w-[280px] max-w-[320px]"
                                title={`${s.profileName} is listening to ${s.trackTitle}${s.artist ? ` by ${s.artist}` : ''}`}
                            >
                                <div className="w-12 h-12 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] overflow-hidden shrink-0">
                                    {s.albumArtworkUrl
                                        ? <img src={s.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                        : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                </div>
                                <div className="flex-1 min-w-0">
                                    <div className="flex items-center gap-2 min-w-0">
                                        {s.profileImageUrl
                                            ? <img src={s.profileImageUrl} alt="" className="w-5 h-5 rounded-full shrink-0 object-cover" />
                                            : <div className="w-5 h-5 rounded-full bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] text-[10px] font-bold flex items-center justify-center shrink-0">{s.profileName.charAt(0).toUpperCase()}</div>}
                                        <span className="text-xs text-emerald-300 font-bold truncate">{s.profileName}</span>
                                    </div>
                                    <div className="text-sm text-[var(--vora-text-primary)] truncate mt-0.5" title={s.trackTitle}>{s.trackTitle}</div>
                                    <div className="text-xs text-[var(--vora-text-muted)] truncate">{s.artist ?? s.albumTitle ?? ''}</div>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {dailyMixes.filter(m => m.kind === 'DailyMix' || m.kind === 'DiscoverMix').length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Made for You</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {dailyMixes.filter(m => m.kind === 'DailyMix' || m.kind === 'DiscoverMix').map(mix => (
                            <button
                                key={mix.id}
                                type="button"
                                onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                                className="w-36 sm:w-40 shrink-0 group text-left cursor-pointer"
                                title={mix.name}
                            >
                                <div className={`w-full aspect-square rounded border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2 relative ${mix.kind === 'DiscoverMix' ? 'bg-gradient-to-br from-emerald-700 via-teal-900 to-cyan-900' : 'bg-gradient-to-br from-orange-700 via-purple-900 to-indigo-900'}`}>
                                    {mix.artworkUrl
                                        ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                        : null}
                                    <div className="absolute inset-0 flex flex-col justify-end p-3 bg-gradient-to-t from-black/80 via-black/30 to-transparent">
                                        <div className={`text-xs uppercase tracking-widest font-bold ${mix.kind === 'DiscoverMix' ? 'text-emerald-300/90' : 'text-orange-300/90'}`}>
                                            {mix.kind === 'DiscoverMix' ? 'Discover' : `Daily Mix ${mix.slot}`}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] drop-shadow-md truncate">{mix.descriptionTag ?? 'Mix'}</div>
                                    </div>
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={mix.name}>{mix.name}</div>
                                <div className="text-xs text-[var(--vora-text-muted)]">{mix.trackCount} tracks</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {dailyMixes.filter(m => m.kind === 'ReleaseRadar').length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3 flex items-center gap-2">
                        Release Radar
                        <span className="px-2 py-0.5 text-[10px] uppercase tracking-widest font-bold rounded bg-rose-500/20 text-rose-300 border border-rose-500/30">New</span>
                    </h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {dailyMixes.filter(m => m.kind === 'ReleaseRadar').map(mix => (
                            <button
                                key={mix.id}
                                type="button"
                                onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                                className="w-36 sm:w-40 shrink-0 group text-left cursor-pointer"
                                title={mix.name}
                            >
                                <div className="w-full aspect-square rounded border border-[var(--vora-border-subtle)] group-hover:border-rose-400 transition-all overflow-hidden mb-2 relative bg-gradient-to-br from-rose-700 via-fuchsia-900 to-violet-900">
                                    {mix.artworkUrl
                                        ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                        : null}
                                    <div className="absolute inset-0 flex flex-col justify-end p-3 bg-gradient-to-t from-black/80 via-black/30 to-transparent">
                                        <div className="text-xs uppercase tracking-widest font-bold text-rose-200/90">Release Radar</div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] drop-shadow-md truncate">{mix.descriptionTag ?? 'New releases'}</div>
                                    </div>
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={mix.name}>{mix.name}</div>
                                <div className="text-xs text-[var(--vora-text-muted)]">{mix.trackCount} tracks</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {dailyMixes.filter(m => m.kind === 'MoodMix').length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Moods</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {dailyMixes.filter(m => m.kind === 'MoodMix').map(mix => (
                            <button
                                key={mix.id}
                                type="button"
                                onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                                className="w-36 sm:w-40 shrink-0 group text-left cursor-pointer"
                                title={mix.name}
                            >
                                <div className="w-full aspect-square rounded bg-gradient-to-br from-blue-700 via-purple-800 to-pink-800 border border-[var(--vora-border-subtle)] group-hover:border-pink-400 transition-all overflow-hidden mb-2 relative">
                                    {mix.artworkUrl
                                        ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                        : null}
                                    <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-t from-black/70 to-transparent">
                                        <div className="text-xl sm:text-2xl font-bold text-[var(--vora-text-primary)] drop-shadow-lg text-center px-2">{mix.name}</div>
                                    </div>
                                </div>
                                <div className="text-xs text-[var(--vora-text-muted)] text-center">{mix.trackCount} tracks</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {stations.length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Your Stations</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {stations.map(station => (
                            <div
                                key={station.id}
                                className="w-32 sm:w-36 shrink-0 group cursor-pointer relative"
                                title={station.name}
                            >
                                <button
                                    type="button"
                                    onClick={() => startStationRadio(station)}
                                    className="block w-full text-left"
                                >
                                    <div className="w-full aspect-square rounded-full bg-gradient-to-br from-indigo-700 via-purple-900 to-orange-700 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2 relative">
                                        {station.artworkUrl
                                            ? <img src={station.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                            : null}
                                        <div className="absolute inset-0 flex items-center justify-center">
                                            <svg className="w-10 h-10 text-[var(--vora-text-primary)]/90 drop-shadow-lg" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M9.348 14.652a3.75 3.75 0 010-5.304m5.304 0a3.75 3.75 0 010 5.304m-7.425 2.121a6.75 6.75 0 010-9.546m9.546 0a6.75 6.75 0 010 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 0c3.808 3.807 3.808 9.98 0 13.788M12 12h.01" /></svg>
                                        </div>
                                    </div>
                                    <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center" title={station.name}>{station.name}</div>
                                    {station.subtitleHint && <div className="text-xs text-[var(--vora-text-muted)] truncate text-center">{station.subtitleHint}</div>}
                                </button>
                                <button
                                    type="button"
                                    onClick={async (e) => {
                                        e.stopPropagation();
                                        const ok = await dialog.confirm(`Delete station "${station.name}"?`);
                                        if (ok) deleteStation(station.id);
                                    }}
                                    className="absolute top-2 right-2 p-1 rounded-full bg-[var(--vora-bg-canvas)]/80 text-[var(--vora-text-secondary)] hover:text-[var(--vora-danger-text)] opacity-0 group-hover:opacity-100 transition-opacity cursor-pointer"
                                    title="Delete station"
                                >
                                    <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                                </button>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {becauseYouPlayed.map(row => (
                <div key={row.seedArtistId} className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3 truncate">{row.heading}</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {row.tracks.map((t, idx) => (
                            <button
                                key={t.id}
                                type="button"
                                onClick={() => playArtistTrackList(row.tracks, idx)}
                                className="w-32 sm:w-36 shrink-0 group text-left cursor-pointer"
                                title={t.title}
                            >
                                <div className="w-full aspect-square rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                    {t.albumArtworkUrl
                                        ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                        : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={t.title}>{t.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist ?? t.albumTitle ?? ''}</div>
                            </button>
                        ))}
                    </div>
                </div>
            ))}

            {recentlyPlayed.length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Recently Played</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {recentlyPlayed.map((t, idx) => (
                            <button
                                key={t.id}
                                type="button"
                                onClick={() => playArtistTrackList(recentlyPlayed, idx)}
                                className="w-32 sm:w-36 shrink-0 group text-left cursor-pointer"
                                title="Play"
                            >
                                <div className="w-full aspect-square rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                    {t.albumArtworkUrl
                                        ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                        : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={t.title}>{t.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.albumTitle ?? ''}</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {recentlyAddedAlbums.length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Recently Added</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {recentlyAddedAlbums.map(album => (
                            <button
                                key={album.id}
                                type="button"
                                onClick={() => updateNav({ view: 'album', artistId: album.artistId, albumId: album.id })}
                                className="w-32 sm:w-36 shrink-0 group text-left cursor-pointer"
                                title={`${album.title} — ${album.artistName}`}
                            >
                                <div className="w-full aspect-square rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                    {album.artworkUrl
                                        ? <img src={album.artworkUrl} alt={album.title} className="w-full h-full object-cover" />
                                        : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={album.title}>{album.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{album.artistName}</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {topArtists.length > 0 && (
                <div className="mb-8">
                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Artists</h2>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {topArtists.map(artist => (
                            <button
                                key={artist.id}
                                type="button"
                                onClick={() => updateNav({ view: 'artist', artistId: artist.id })}
                                className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                title={artist.name}
                            >
                                <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                    {artist.artworkUrl
                                        ? <img src={artist.artworkUrl} alt={artist.name} className="w-full h-full object-cover" />
                                        : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center" title={artist.name}>{artist.name}</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">All Artists</h2>
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-4">
                <div
                    onClick={() => updateNav({ view: 'likes' })}
                    className="relative flex flex-col items-center bg-gradient-to-br from-orange-700 to-purple-900 hover:from-orange-600 hover:to-purple-800 border border-[var(--vora-border-subtle)] hover:border-orange-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                    title="Liked Songs"
                >
                    <div className="w-24 h-24 rounded bg-gradient-to-br from-orange-500/40 to-purple-500/40 border border-orange-400/30 flex items-center justify-center overflow-hidden mb-3">
                        <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                    </div>
                    <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Liked Songs</div>
                    <div className="text-xs text-orange-100/80 text-center">{likedCount} {likedCount === 1 ? 'track' : 'tracks'}</div>
                </div>
                <div
                    onClick={() => updateNav({ view: 'top' })}
                    className="relative flex flex-col items-center bg-gradient-to-br from-indigo-700 to-cyan-900 hover:from-indigo-600 hover:to-cyan-800 border border-[var(--vora-border-subtle)] hover:border-cyan-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                    title="Top Tracks"
                >
                    <div className="w-24 h-24 rounded bg-gradient-to-br from-indigo-500/40 to-cyan-500/40 border border-cyan-400/30 flex items-center justify-center overflow-hidden mb-3">
                        <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1l3 6h6l-5 4 2 7-6-4-6 4 2-7-5-4h6z" /></svg>
                    </div>
                    <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Your Top Tracks</div>
                    <div className="text-xs text-cyan-100/80 text-center">{topTracks.length > 0 ? `${topTracks.length} tracks` : 'Build a history first'}</div>
                </div>
                <div
                    onClick={() => updateNav({ view: 'genres' })}
                    className="relative flex flex-col items-center bg-gradient-to-br from-emerald-700 to-teal-900 hover:from-emerald-600 hover:to-teal-800 border border-[var(--vora-border-subtle)] hover:border-emerald-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                    title="Browse by genre"
                >
                    <div className="w-24 h-24 rounded bg-gradient-to-br from-emerald-500/40 to-teal-500/40 border border-emerald-400/30 flex items-center justify-center overflow-hidden mb-3">
                        <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" /></svg>
                    </div>
                    <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Browse by Genre</div>
                    <div className="text-xs text-emerald-100/80 text-center">Discover by mood</div>
                </div>
                {hasAnyHistory && (
                    <div
                        onClick={() => updateNav({ view: 'recap', year: availableYears[0] ?? new Date().getFullYear() })}
                        className="relative flex flex-col items-center bg-gradient-to-br from-pink-700 to-amber-700 hover:from-pink-600 hover:to-amber-600 border border-[var(--vora-border-subtle)] hover:border-pink-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                        title="Year in music"
                    >
                        <div className="w-24 h-24 rounded bg-gradient-to-br from-pink-500/40 to-amber-500/40 border border-pink-400/30 flex items-center justify-center overflow-hidden mb-3">
                            <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M9 17.25v1.007a3 3 0 01-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0115 18.257V17.25m6-12V15a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 15V5.25m18 0A2.25 2.25 0 0018.75 3H5.25A2.25 2.25 0 003 5.25m18 0V12a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 12V5.25" /></svg>
                        </div>
                        <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Year in Music</div>
                        <div className="text-xs text-pink-100/80 text-center">{availableYears[0] ?? new Date().getFullYear()}</div>
                    </div>
                )}
                {artists.map(artist => (
                    <div
                        key={artist.id}
                        onClick={() => updateNav({ view: 'artist', artistId: artist.id })}
                        className="relative flex flex-col items-center bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] rounded-lg p-4 transition-all cursor-pointer text-left"
                    >
                        {isServerAdmin && (
                            <button
                                type="button"
                                onClick={(e) => { e.stopPropagation(); onEditArtist(artist); }}
                                className="absolute top-2 right-2 p-1.5 rounded bg-[var(--vora-bg-canvas)]/80 text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer opacity-0 group-hover:opacity-100"
                                title="Edit artist"
                                style={{ opacity: 1 }}
                            >
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                            </button>
                        )}
                        <div className="w-24 h-24 rounded-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden mb-3">
                            {artist.artworkUrl
                                ? <img src={artist.artworkUrl} alt={artist.name} className="max-w-full max-h-full object-cover" />
                                : <svg className="w-10 h-10 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg>}
                        </div>
                        <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center truncate w-full" title={artist.name}>{artist.name}</div>
                    </div>
                ))}
            </div>
        </>
    );
}
