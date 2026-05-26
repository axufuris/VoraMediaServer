import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
    smartPlaylistService,
    type SmartPlaylistDetailVM,
    type SmartPlaylistItemsVM,
    type PlaylistMediaType
} from '../../../api/Music/smartPlaylistService';
import { musicService, type ArtistTrackVM } from '../../../api/Music/musicService';
import { usePlayer, type PlayableMedia } from '../../../contexts/usePlayer';
import { serverVault } from '../../../utils/serverVault';
import { audioQualityStore } from '../../../utils/audioQuality';
import { useDialog } from '../../../dialogs';
import SmartPlaylistEditorModal from './SmartPlaylistEditorModal';

export default function SmartPlaylistDetailsPage() {
    const { serverId, id } = useParams<{ serverId?: string; id: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();
    const { playQueue } = usePlayer();

    const [detail, setDetail] = useState<SmartPlaylistDetailVM | null>(null);
    const [items, setItems] = useState<SmartPlaylistItemsVM | null>(null);
    const [loading, setLoading] = useState(true);
    const [editing, setEditing] = useState(false);

    const load = useCallback(async () => {
        if (!id) return;
        setLoading(true);
        try {
            const [d, t] = await Promise.all([
                smartPlaylistService.get(id, serverId),
                smartPlaylistService.getItems(id, serverId)
            ]);
            setDetail(d);
            setItems(t);
        } catch (err) {
            console.error(err);
        } finally {
            setLoading(false);
        }
    }, [id, serverId]);

    useEffect(() => { load(); }, [load]);

    const buildMusicQueue = (list: ArtistTrackVM[]): PlayableMedia[] => {
        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
        const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
        return list.map(t => ({
            id: t.id,
            title: t.title,
            subtitle: t.artist ?? '',
            posterUrl: t.albumArtworkUrl,
            streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
            serverId: server?.id,
            container: 'audio',
            playbackContextType: 'Music'
        }));
    };

    const handlePlay = (startIndex = 0) => {
        if (!items) return;
        if (items.mediaType === 'Music' && items.tracks && items.tracks.length > 0) {
            playQueue(buildMusicQueue(items.tracks), startIndex);
        } else if (items.mediaType === 'Movies' && items.movies && items.movies.length > 0) {
            const movie = items.movies[startIndex];
            navigate(serverId ? `/server/${serverId}/media/${movie.id}` : `/media/${movie.id}`);
        } else if (items.mediaType === 'Shows' && items.episodes && items.episodes.length > 0) {
            const ep = items.episodes[startIndex];
            navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`);
        }
    };

    const handleShuffle = () => {
        if (!items) return;
        if (items.mediaType === 'Music' && items.tracks && items.tracks.length > 0) {
            const shuffled = [...items.tracks].sort(() => Math.random() - 0.5);
            playQueue(buildMusicQueue(shuffled), 0);
        }
    };

    const handleDelete = async () => {
        if (!detail) return;
        if (!await dialog.confirm(`Delete the smart playlist "${detail.name}"?`)) return;
        try {
            await smartPlaylistService.remove(detail.id, serverId);
            navigate(serverId ? `/server/${serverId}/playlists` : '/playlists');
        } catch (err) {
            console.error(err);
            await dialog.alert('Failed to delete smart playlist.');
        }
    };

    const formatDuration = (s?: number) => {
        if (!s || s <= 0) return '';
        const h = Math.floor(s / 3600);
        const m = Math.floor((s % 3600) / 60);
        const sec = Math.floor(s % 60);
        if (h > 0) return `${h}:${m.toString().padStart(2, '0')}:${sec.toString().padStart(2, '0')}`;
        return `${m}:${sec.toString().padStart(2, '0')}`;
    };

    if (loading) {
        return <div className="p-12 text-center text-[var(--vora-text-muted)] mt-16">Loading…</div>;
    }
    if (!detail || !items) {
        return <div className="p-12 text-center text-[var(--vora-text-muted)] mt-16">Smart playlist not found.</div>;
    }

    const itemCount = items.mediaType === 'Music'
        ? items.tracks?.length ?? 0
        : items.mediaType === 'Movies'
            ? items.movies?.length ?? 0
            : items.episodes?.length ?? 0;

    const itemLabel = items.mediaType === 'Shows' ? 'episodes' : items.mediaType === 'Movies' ? 'movies' : 'tracks';
    const showShuffleBtn = items.mediaType === 'Music';

    const heroGradient = items.mediaType === 'Music'
        ? 'from-fuchsia-700 via-violet-900 to-indigo-900'
        : items.mediaType === 'Movies'
            ? 'from-sky-700 via-blue-900 to-indigo-900'
            : 'from-amber-700 via-orange-900 to-red-900';

    const playLabel = items.mediaType === 'Music' ? '▶ Play' : '▶ Open';

    return (
        <div className="min-h-full pb-16">
            <div className="mx-auto w-full max-w-6xl px-8 pt-12">
                <div className="mb-8 flex items-end gap-6 pb-6" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                    <div className={`w-48 h-48 rounded shadow-2xl flex items-center justify-center bg-gradient-to-br ${heroGradient} flex-shrink-0 relative overflow-hidden`}>
                        {detail.artworkUrl
                            ? <img src={detail.artworkUrl} alt="" className="w-full h-full object-cover" />
                            : <span className="text-6xl">⚙</span>}
                        <div className="absolute top-2 right-2 px-2 py-0.5 text-[10px] uppercase tracking-widest font-bold rounded bg-white/20 text-[var(--vora-text-primary)] border border-white/30">Smart</div>
                    </div>
                    <div className="flex-1 min-w-0">
                        <div className="text-xs uppercase tracking-widest text-[var(--vora-text-muted)] mb-2 font-bold">Smart Playlist · {detail.mediaType}</div>
                        <h1 className="text-4xl font-bold text-[var(--vora-text-primary)] truncate" title={detail.name}>{detail.name}</h1>
                        {detail.description && <p className="text-[var(--vora-text-muted)] mt-2">{detail.description}</p>}
                        <div className="text-sm text-[var(--vora-text-muted)] mt-3">{itemCount} {itemLabel} · auto-updates as your library changes</div>
                        <div className="flex flex-wrap gap-3 mt-4">
                            <button onClick={() => handlePlay(0)} disabled={itemCount === 0} className="px-6 py-2 bg-fuchsia-600 hover:bg-fuchsia-500 text-[var(--vora-text-primary)] font-bold rounded shadow-lg cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed">{playLabel}</button>
                            {showShuffleBtn && (
                                <button onClick={handleShuffle} disabled={itemCount === 0} className="px-4 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] font-bold rounded cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed">🔀 Shuffle</button>
                            )}
                            <button onClick={() => setEditing(true)} className="px-4 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] font-bold rounded cursor-pointer">Edit Rules</button>
                            <button onClick={handleDelete} className="px-4 py-2 bg-rose-900 hover:bg-rose-700 text-[var(--vora-text-primary)] font-bold rounded cursor-pointer">Delete</button>
                        </div>
                    </div>
                </div>

                {itemCount === 0 ? (
                    <div
                        className="mt-12 rounded-xl p-8 text-center text-sm"
                        style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-muted)' }}
                    >
                        No {itemLabel} match the current rules. Try loosening them in Edit Rules.
                    </div>
                ) : items.mediaType === 'Music' && items.tracks ? (
                    <MusicTable tracks={items.tracks} onPlay={handlePlay} formatDuration={formatDuration} />
                ) : items.mediaType === 'Movies' && items.movies ? (
                    <MovieGrid movies={items.movies} onSelect={(idx) => handlePlay(idx)} />
                ) : items.mediaType === 'Shows' && items.episodes ? (
                    <EpisodeList episodes={items.episodes} onSelect={(idx) => handlePlay(idx)} formatDuration={formatDuration} />
                ) : null}
            </div>

            {editing && (
                <SmartPlaylistEditorModal
                    serverId={serverId}
                    initialId={detail.id}
                    initialMediaType={detail.mediaType as PlaylistMediaType}
                    onClose={() => setEditing(false)}
                    onSaved={() => { setEditing(false); load(); }}
                />
            )}
        </div>
    );
}

function MusicTable({ tracks, onPlay, formatDuration }: { tracks: ArtistTrackVM[]; onPlay: (i: number) => void; formatDuration: (s?: number) => string }) {
    return (
        <div className="bg-[var(--vora-bg-canvas)]/40 border border-[var(--vora-border-subtle)] rounded overflow-hidden">
            <table className="w-full text-sm">
                <thead className="text-[var(--vora-text-muted)] border-b border-[var(--vora-border-subtle)] text-left">
                    <tr>
                        <th className="w-12 px-3 py-2">#</th>
                        <th className="px-3 py-2">Title</th>
                        <th className="px-3 py-2 hidden md:table-cell">Album</th>
                        <th className="px-3 py-2 hidden md:table-cell text-right">Duration</th>
                    </tr>
                </thead>
                <tbody>
                    {tracks.map((t, idx) => (
                        <tr key={t.id} className="border-b border-[var(--vora-border-subtle)] hover:bg-[var(--vora-bg-sunken)]/50 cursor-pointer" onClick={() => onPlay(idx)}>
                            <td className="px-3 py-2 text-[var(--vora-text-muted)]">{idx + 1}</td>
                            <td className="px-3 py-2">
                                <div className="flex items-center gap-3">
                                    {t.albumArtworkUrl ? <img src={t.albumArtworkUrl} alt="" className="w-9 h-9 rounded object-cover flex-shrink-0" /> : <div className="w-9 h-9 rounded bg-[var(--vora-bg-sunken)] flex-shrink-0" />}
                                    <div className="min-w-0">
                                        <div className="text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist ?? ''}</div>
                                    </div>
                                </div>
                            </td>
                            <td className="px-3 py-2 hidden md:table-cell text-[var(--vora-text-muted)] truncate max-w-[280px]" title={t.albumTitle ?? ''}>{t.albumTitle ?? '—'}</td>
                            <td className="px-3 py-2 hidden md:table-cell text-right text-[var(--vora-text-muted)] font-mono">{formatDuration(t.durationSeconds)}</td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

function MovieGrid({ movies, onSelect }: { movies: { id: string; title: string; year?: number; posterUrl?: string; isWatched: boolean }[]; onSelect: (i: number) => void }) {
    return (
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-4">
            {movies.map((m, idx) => (
                <button
                    key={m.id}
                    type="button"
                    onClick={() => onSelect(idx)}
                    className="group text-left cursor-pointer"
                    title={m.title}
                >
                    <div className="w-full aspect-[2/3] rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-sky-500 transition-all overflow-hidden mb-2 relative">
                        {m.posterUrl ? <img src={m.posterUrl} alt="" className="w-full h-full object-cover" /> : <div className="absolute inset-0 flex items-center justify-center text-3xl text-[var(--vora-text-muted)]">🎬</div>}
                        {m.isWatched && <div className="absolute top-1 right-1 px-1.5 py-0.5 text-[9px] uppercase tracking-widest font-bold rounded bg-emerald-500/30 text-emerald-100 border border-emerald-400/40">Watched</div>}
                    </div>
                    <div className="text-sm font-bold text-[var(--vora-text-secondary)] truncate" title={m.title}>{m.title}</div>
                    {m.year && <div className="text-xs text-[var(--vora-text-muted)]">{m.year}</div>}
                </button>
            ))}
        </div>
    );
}

function EpisodeList({ episodes, onSelect, formatDuration }: { episodes: { id: string; title: string; showTitle?: string; seasonNumber?: number; episodeNumber?: number; posterUrl?: string; durationSeconds?: number; isWatched: boolean }[]; onSelect: (i: number) => void; formatDuration: (s?: number) => string }) {
    return (
        <div className="bg-[var(--vora-bg-canvas)]/40 border border-[var(--vora-border-subtle)] rounded overflow-hidden">
            <table className="w-full text-sm">
                <thead className="text-[var(--vora-text-muted)] border-b border-[var(--vora-border-subtle)] text-left">
                    <tr>
                        <th className="w-12 px-3 py-2">#</th>
                        <th className="px-3 py-2">Show</th>
                        <th className="px-3 py-2 w-20">S/E</th>
                        <th className="px-3 py-2">Episode</th>
                        <th className="px-3 py-2 hidden md:table-cell text-right w-24">Duration</th>
                    </tr>
                </thead>
                <tbody>
                    {episodes.map((e, idx) => (
                        <tr key={e.id} className="border-b border-[var(--vora-border-subtle)] hover:bg-[var(--vora-bg-sunken)]/50 cursor-pointer" onClick={() => onSelect(idx)}>
                            <td className="px-3 py-2 text-[var(--vora-text-muted)]">{idx + 1}</td>
                            <td className="px-3 py-2 text-[var(--vora-text-secondary)] truncate max-w-[200px]" title={e.showTitle ?? ''}>{e.showTitle ?? '—'}</td>
                            <td className="px-3 py-2 text-[var(--vora-text-muted)] font-mono">{e.seasonNumber != null && e.episodeNumber != null ? `S${e.seasonNumber.toString().padStart(2, '0')}E${e.episodeNumber.toString().padStart(2, '0')}` : '—'}</td>
                            <td className="px-3 py-2">
                                <div className="flex items-center gap-2">
                                    <span className="text-[var(--vora-text-primary)] truncate" title={e.title}>{e.title}</span>
                                    {e.isWatched && <span className="px-1.5 py-0.5 text-[9px] uppercase font-bold rounded bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">✓</span>}
                                </div>
                            </td>
                            <td className="px-3 py-2 hidden md:table-cell text-right text-[var(--vora-text-muted)] font-mono">{formatDuration(e.durationSeconds)}</td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
