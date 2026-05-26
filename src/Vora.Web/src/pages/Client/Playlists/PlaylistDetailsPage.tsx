import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { playlistService, type PlaylistDetailsVM, type PlaylistItemVM } from '../../../api/Collections/playlistService';
import { mediaService } from '../../../api/Media/mediaService';
import { musicService } from '../../../api/Music/musicService';
import { usePlayer } from '../../../contexts/usePlayer';
import { streamingService } from '../../../api/Streaming/streamingService';
import { serverVault } from '../../../utils/serverVault';
import { useDialog } from '../../../dialogs';
import { audioQualityStore } from '../../../utils/audioQuality';
import CinematicBackdrop from '../../../components/Client/Primitives/CinematicBackdrop';
import { StorageKeys } from '../../../utils/storageKeys';

export default function PlaylistDetailsPage() {
    const dialog = useDialog();
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();
    const { playMedia, playQueue } = usePlayer();

    const formatTrackDuration = (s?: number): string => {
        if (!s || s <= 0) return '';
        const m = Math.floor(s / 60);
        const sec = Math.floor(s % 60);
        return `${m}:${sec.toString().padStart(2, '0')}`;
    };

    const [playlist, setPlaylist] = useState<PlaylistDetailsVM | null>(null);
    const [selectedItem, setSelectedItem] = useState<PlaylistItemVM | null>(null);
    const [loading, setLoading] = useState(true);
    const [draggedIndex, setDraggedIndex] = useState<number | null>(null);

    const [isEditModalOpen, setIsEditModalOpen] = useState(false);
    const [editName, setEditName] = useState('');
    const [editDescription, setEditDescription] = useState('');

    const loadPlaylist = useCallback(async () => {
        if (!id) return;
        try {
            const data = await playlistService.getPlaylist(id, serverId);
            setPlaylist(data);
            if (data.items.length > 0) {
                const firstUnwatched = data.items.find(i => !i.isPlayed) || data.items[0];

                setSelectedItem(prev => {
                    if (prev) {
                        const stillExists = data.items.find(i => i.id === prev.id);
                        if (stillExists) return stillExists;
                    }
                    return firstUnwatched;
                });
            } else {
                setSelectedItem(null);
            }
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    }, [id, serverId]);

    useEffect(() => { loadPlaylist(); }, [loadPlaylist]);

    const handleDragStart = (e: React.DragEvent, index: number) => {
        setDraggedIndex(index);
        e.dataTransfer.effectAllowed = "move";
    };

    const handleDragOver = (e: React.DragEvent) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
    };

    const handleDrop = async (e: React.DragEvent, dropIndex: number) => {
        e.preventDefault();
        if (draggedIndex === null || draggedIndex === dropIndex || !playlist) return;

        const newItems = [...playlist.items];
        const [movedItem] = newItems.splice(draggedIndex, 1);
        newItems.splice(dropIndex, 0, movedItem);

        newItems.forEach((item, idx) => item.order = idx);
        setPlaylist({ ...playlist, items: newItems });
        setDraggedIndex(null);

        try {
            await playlistService.reorderPlaylist(playlist.id, newItems.map(i => i.id), serverId);
        } catch {
            await dialog.alert("Failed to save new order.");
            loadPlaylist();
        }
    };

    const handleMarkAllUnwatched = async () => {
        if (!playlist || !await dialog.confirm("Mark all items in this playlist as unwatched?")) return;
        await playlistService.markAllUnplayed(playlist.id, serverId);
        loadPlaylist();
    };

    const handleRemoveItem = async (itemId: string) => {
        if (!playlist || !await dialog.confirm("Remove this item?")) return;
        await playlistService.removeFromPlaylist(playlist.id, itemId, serverId);
        loadPlaylist();
    };

    const handleTogglePlayed = async (e: React.MouseEvent, mediaId: string, currentIsPlayed: boolean) => {
        e.stopPropagation();
        try {
            await mediaService.markAsPlayed(mediaId, !currentIsPlayed, serverId);
            loadPlaylist();
        } catch (error) {
            console.error("Failed to toggle played state", error);
            await dialog.alert("Failed to update play state.");
        }
    };

    const handleGoToDetails = (e: React.MouseEvent, mediaId: string) => {
        e.stopPropagation();
        navigate(serverId ? `/server/${serverId}/media/${mediaId}` : `/media/${mediaId}`);
    };

    const handlePlay = async () => {
        if (!selectedItem) return;

        if (selectedItem.type === 'Track' && playlist) {
            const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
            const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
            const trackItems = playlist.items.filter(i => i.type === 'Track');
            const startIndex = Math.max(0, trackItems.findIndex(i => i.id === selectedItem.id));
            const queue = trackItems.map(t => ({
                id: t.mediaItemId,
                title: t.title,
                subtitle: t.artistName && t.albumTitle ? `${t.artistName} — ${t.albumTitle}` : (t.albumTitle ?? playlist.name),
                posterUrl: t.albumArtworkUrl,
                streamUrl: musicService.getTrackStreamUrl(t.mediaItemId, baseUrl, audioQualityStore.get()),
                serverId: server?.id,
                container: 'audio' as const,
                playbackContextType: 'Music' as const
            }));
            playQueue(queue, startIndex);
            return;
        }

        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';

        let targetMediaId = selectedItem.mediaItemId;
        let startPos = selectedItem.resumePositionSeconds || 0;
        let playTitle = selectedItem.title;
        let playSubtitle = selectedItem.type === 'Episode' ? `S${selectedItem.seasonNumber} E${selectedItem.episodeNumber} - ${selectedItem.tvShowTitle}` : playlist?.name;

        if (selectedItem.type === 'Season') {
            try {
                const seasonData = await mediaService.getSeason(selectedItem.mediaItemId, serverId);
                if (!seasonData.episodes || seasonData.episodes.length === 0) {
                    await dialog.alert("This season has no episodes to play.");
                    return;
                }

                const firstUnwatched = seasonData.episodes.find(e => !e.isPlayed) || seasonData.episodes[0];

                targetMediaId = firstUnwatched.id;
                startPos = firstUnwatched.resumePositionSeconds || 0;
                playTitle = firstUnwatched.title;
                playSubtitle = `S${seasonData.seasonNumber} E${firstUnwatched.episodeNumber} - ${seasonData.tvShowTitle}`;
            } catch (error) {
                console.error("Failed to load season episodes", error);
                await dialog.alert("Failed to resolve episodes for this season.");
                return;
            }
        }

        try {
            const session = await streamingService.startSession(targetMediaId, deviceId, startPos, undefined, undefined, undefined, serverId);
            playMedia({
                id: targetMediaId,
                title: playTitle,
                subtitle: playSubtitle,
                posterUrl: selectedItem.posterUrl,
                backgroundUrl: selectedItem.backgroundUrl,
                ...session,
                serverId: serverId ?? serverVault.getActiveServerId() ?? undefined,
                startPosition: startPos,
                playbackContextType: 'playlist',
                playbackContextId: playlist?.id
            });
        } catch {
            await dialog.alert("Failed to start playback.");
        }
    };

    if (loading) return <div className="p-12 text-center text-[var(--vora-text-muted)] mt-16">Loading playlist...</div>;
    if (!playlist) return <div className="p-12 text-center text-[var(--vora-danger-500)] mt-16">Playlist not found.</div>;

    const inProgress = selectedItem && selectedItem.resumePositionSeconds > 0 && !selectedItem.isPlayed;

    const totalMinutes = playlist.items.reduce((acc, item) => acc + (item.durationMinutes || 0), 0);
    const totalDurationFormatted = totalMinutes > 0 ? `${Math.floor(totalMinutes / 60)}h ${totalMinutes % 60}m` : '';

    const handleDeletePlaylist = async () => {
        if (!playlist || !await dialog.confirm(`Are you absolutely sure you want to delete "${playlist.name}"?`)) return;
        try {
            await playlistService.deletePlaylist(playlist.id, serverId);
            navigate(serverId ? `/server/${serverId}/playlists` : '/playlists');
        } catch {
            await dialog.alert("Failed to delete playlist.");
        }
    };

    const handleOpenEdit = () => {
        if (!playlist) return;
        setEditName(playlist.name);
        setEditDescription(playlist.description || '');
        setIsEditModalOpen(true);
    };

    const handleSaveEdit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!playlist || !editName.trim()) return;

        try {
            await playlistService.updatePlaylist(playlist.id, editName, editDescription, serverId);
            setIsEditModalOpen(false);
            loadPlaylist();
        } catch {
            await dialog.alert("Failed to update playlist.");
        }
    };

    return (
        <div className="relative min-h-full pb-16">

            <div className="absolute inset-x-0 top-0 z-0">
                <CinematicBackdrop src={selectedItem?.backgroundUrl} intensity="detail" parallax transitionKey={selectedItem?.id} />
            </div>

            <div className="relative z-10 mx-auto w-full max-w-7xl flex-1 px-12 pt-8">
                <button
                    type="button"
                    onClick={() => navigate(serverId ? `/server/${serverId}/playlists` : '/playlists')}
                    className="mb-8 inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                    style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                    Back to Playlists
                </button>

                <div className="flex flex-col md:flex-row gap-10 mb-16">
                    <div className="w-64 shrink-0 relative">
                        <div className={`${selectedItem?.type === 'Track' ? 'aspect-square' : 'aspect-[2/3]'} rounded-lg overflow-hidden shadow-2xl border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] relative`}>
                            {(selectedItem?.type === 'Track' ? selectedItem.albumArtworkUrl : selectedItem?.posterUrl) ? (
                                <img src={selectedItem?.type === 'Track' ? selectedItem.albumArtworkUrl : selectedItem?.posterUrl} alt="" className="w-full h-full object-cover" />
                            ) : (
                                <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-muted)]">{selectedItem?.type === 'Track' ? <svg className="w-16 h-16" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg> : 'No Image'}</div>
                            )}
                            {selectedItem?.isPlayed && (
                                <div className="absolute top-2 right-2 bg-black/60 backdrop-blur-sm rounded-full p-1 shadow-lg border border-white/10">
                                    <svg className="w-5 h-5 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                                </div>
                            )}

                            {inProgress && selectedItem.durationMinutes && (
                                <div className="absolute bottom-0 left-0 right-0 h-1.5 bg-[var(--vora-bg-sunken)] z-10">
                                    <div className="h-full bg-[var(--vora-accent-500)]" style={{ width: `${(selectedItem.resumePositionSeconds / (selectedItem.durationMinutes * 60)) * 100}%` }}></div>
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="flex-1 pt-0">
                        <div className="flex items-center justify-between mb-1">
                            <h2 className="text-xl font-bold text-[var(--vora-accent-500)] tracking-wider uppercase flex items-center gap-3">
                                {playlist.name}
                                <span className="text-[var(--vora-text-muted)] text-sm font-medium tracking-normal normal-case">
                                    ({playlist.items.length} items {totalDurationFormatted ? `• ${totalDurationFormatted}` : ''})
                                </span>
                            </h2>

                            <div className="flex gap-2">
                                <button onClick={handleOpenEdit} className="p-1.5 rounded-md text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-sunken)] transition-colors" title="Edit Playlist">
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                </button>
                                <button onClick={handleDeletePlaylist} className="p-1.5 rounded-md text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)] hover:bg-[var(--vora-bg-sunken)] transition-colors" title="Delete Playlist">
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                                </button>
                            </div>
                        </div>

                        {playlist.description && (
                            <p className="text-[var(--vora-text-muted)] text-sm mb-4 max-w-3xl">{playlist.description}</p>
                        )}

                        <h1 className="text-[2.75rem] leading-tight font-bold text-[var(--vora-text-primary)] drop-shadow-lg mb-1">
                            {selectedItem?.type === 'Episode' || selectedItem?.type === 'Season'
                                ? selectedItem.tvShowTitle
                                : selectedItem?.title || "Empty Playlist"}
                        </h1>

                        {(selectedItem?.type === 'Episode' || selectedItem?.type === 'Season') && (
                            <h2 className="text-2xl font-bold text-[var(--vora-text-secondary)] mb-5">{selectedItem.title}</h2>
                        )}

                        {selectedItem?.type === 'Track' && (
                            <h2 className="text-2xl font-bold text-[var(--vora-text-secondary)] mb-5">
                                {selectedItem.artistName ? `${selectedItem.artistName}${selectedItem.albumTitle ? ` — ${selectedItem.albumTitle}` : ''}` : selectedItem.albumTitle}
                            </h2>
                        )}

                        <div className="flex items-center gap-3 text-sm font-medium text-[var(--vora-text-muted)] mb-8">
                            {selectedItem?.type === 'Episode' && <span>Season {selectedItem.seasonNumber} &nbsp;&nbsp; Episode {selectedItem.episodeNumber}</span>}
                            {selectedItem?.releaseYear && <span>{selectedItem.releaseYear}</span>}
                            {selectedItem?.type === 'Track' && selectedItem.durationSeconds ? (
                                <span>{formatTrackDuration(selectedItem.durationSeconds)}</span>
                            ) : selectedItem?.durationMinutes ? (
                                <span>{selectedItem.durationMinutes} min</span>
                            ) : null}
                            {selectedItem?.contentRating && <span>{selectedItem.contentRating}</span>}
                        </div>

                        {selectedItem && (
                            <div className="flex flex-wrap gap-3 items-center">
                                <button onClick={handlePlay} className="px-6 py-2.5 bg-yellow-500 hover:bg-yellow-400 text-black font-bold rounded shadow-lg transition-colors flex items-center gap-2 cursor-pointer">
                                    <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 20 20"><path d="M4 4l12 6-12 6z" /></svg>
                                    {inProgress ? 'Resume' : 'Play'}
                                </button>

                                <button
                                    onClick={(e) => handleTogglePlayed(e, selectedItem.mediaItemId, selectedItem.isPlayed)}
                                    className={`p-2.5 rounded-full transition-colors border cursor-pointer shrink-0 ${selectedItem.isPlayed ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] border-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)]' : 'text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)] border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-subtle)]'}`}
                                    title={selectedItem.isPlayed ? "Mark as Unplayed" : "Mark as Played"}
                                >
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={selectedItem.isPlayed ? 3 : 2} d="M5 13l4 4L19 7" opacity={selectedItem.isPlayed ? 1 : 0.4} />
                                    </svg>
                                </button>
                                <button
                                    onClick={(e) => handleGoToDetails(e, selectedItem.mediaItemId)}
                                    className="p-2.5 rounded-full text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)] transition-colors border border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-subtle)] cursor-pointer shrink-0"
                                    title="View Details"
                                >
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                                </button>
                                <button
                                    onClick={handleMarkAllUnwatched}
                                    className="px-4 py-2.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] font-bold rounded shadow-lg transition-colors border border-[var(--vora-border-subtle)] cursor-pointer text-sm"
                                >
                                    Unwatch All
                                </button>
                            </div>
                        )}
                    </div>
                </div>

                <div>
                    <h2 className="text-2xl font-bold mb-6 text-[var(--vora-text-primary)] border-b border-[var(--vora-border-subtle)] pb-2">Up Next ({playlist.items.length})</h2>
                    <div className="space-y-3">
                        {playlist.items.map((item, index) => (
                            <div
                                key={item.id}
                                draggable
                                onDragStart={(e) => handleDragStart(e, index)}
                                onDragOver={handleDragOver}
                                onDrop={(e) => handleDrop(e, index)}
                                onClick={() => setSelectedItem(item)}
                                className={`flex items-center gap-4 p-3 rounded-lg border transition-all cursor-pointer ${draggedIndex === index ? 'opacity-50' : 'opacity-100'} ${item.isPlayed ? 'opacity-60 grayscale-[40%]' : ''} ${selectedItem?.id === item.id ? 'bg-[var(--vora-bg-sunken)] border-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-raised)]/50 border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-subtle)]'}`}
                            >
                                <div className="text-[var(--vora-text-muted)] px-2 cursor-grab" title="Drag to reorder">
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                                </div>
                                {item.type === 'Track' ? (
                                    <div className="w-16 h-16 shrink-0 bg-[var(--vora-bg-sunken)] rounded overflow-hidden relative">
                                        {item.albumArtworkUrl
                                            ? <img src={item.albumArtworkUrl} className="w-full h-full object-cover" />
                                            : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-muted)]"><svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                    </div>
                                ) : (
                                    <div className="w-16 h-24 shrink-0 bg-[var(--vora-bg-sunken)] rounded overflow-hidden relative">
                                        {item.posterUrl ? <img src={item.posterUrl} className="w-full h-full object-cover" /> : null}
                                        {item.isPlayed && (
                                            <div className="absolute top-1 right-1 bg-black/60 rounded-full p-0.5">
                                                <svg className="w-3 h-3 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                                            </div>
                                        )}
                                        {!item.isPlayed && item.resumePositionSeconds > 0 && item.durationMinutes && (
                                            <div className="absolute bottom-0 left-0 right-0 h-1 bg-[var(--vora-bg-sunken)] z-10">
                                                <div className="h-full bg-[var(--vora-accent-500)]" style={{ width: `${(item.resumePositionSeconds / (item.durationMinutes * 60)) * 100}%` }}></div>
                                            </div>
                                        )}
                                    </div>
                                )}
                                <div className="flex-1 flex flex-col justify-center">
                                    <h4 className={`font-bold text-lg ${selectedItem?.id === item.id ? 'text-[var(--vora-accent-500)]' : 'text-[var(--vora-text-secondary)]'}`}>
                                        {item.type === 'Episode' ? `${item.episodeNumber}. ${item.title}` : item.title}
                                    </h4>
                                    <div className="flex items-center gap-3 text-sm text-[var(--vora-text-muted)] font-medium">
                                        {item.type === 'Track' ? (
                                            <span>
                                                {item.artistName ? `${item.artistName}${item.albumTitle ? ` — ${item.albumTitle}` : ''}` : (item.albumTitle ?? 'Track')}
                                            </span>
                                        ) : (
                                            <span>
                                                {item.type === 'Episode' || item.type === 'Season' ? `${item.tvShowTitle} - Season ${item.seasonNumber}` : item.type}
                                            </span>
                                        )}
                                        {item.type === 'Track' && item.durationSeconds ? (
                                            <>
                                                <span className="w-1 h-1 rounded-full bg-[var(--vora-bg-sunken)]"></span>
                                                <span>{formatTrackDuration(item.durationSeconds)}</span>
                                            </>
                                        ) : item.durationMinutes ? (
                                            <>
                                                <span className="w-1 h-1 rounded-full bg-[var(--vora-bg-sunken)]"></span>
                                                <span>{item.durationMinutes} min</span>
                                            </>
                                        ) : null}
                                    </div>
                                </div>

                                <div className="flex items-center">
                                    <button
                                        onClick={(e) => handleTogglePlayed(e, item.mediaItemId, item.isPlayed)}
                                        className={`p-2 transition-colors cursor-pointer ${item.isPlayed ? 'text-[var(--vora-accent-500)] hover:text-[var(--vora-accent-500)]' : 'text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]'}`}
                                        title={item.isPlayed ? "Mark as Unplayed" : "Mark as Played"}
                                    >
                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={item.isPlayed ? 3 : 2} d="M5 13l4 4L19 7" />
                                        </svg>
                                    </button>
                                    <button
                                        onClick={(e) => handleGoToDetails(e, item.mediaItemId)}
                                        className="p-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer"
                                        title="View Details"
                                    >
                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                                        </svg>
                                    </button>
                                    <button
                                        onClick={(e) => { e.stopPropagation(); handleRemoveItem(item.id); }}
                                        className="p-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)] transition-colors cursor-pointer"
                                        title="Remove from Playlist"
                                    >
                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                        </svg>
                                    </button>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            </div>

            {isEditModalOpen && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
                    <div className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-xl shadow-2xl max-w-md w-full p-6">
                        <h2 className="text-2xl font-bold text-[var(--vora-text-primary)] mb-6">Edit Playlist</h2>

                        <form onSubmit={handleSaveEdit} className="space-y-4">
                            <div>
                                <label className="block text-sm font-bold text-[var(--vora-text-muted)] mb-2">Name</label>
                                <input
                                    autoFocus
                                    required
                                    type="text"
                                    value={editName}
                                    onChange={e => setEditName(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]"
                                />
                            </div>

                            <div>
                                <label className="block text-sm font-bold text-[var(--vora-text-muted)] mb-2">Description</label>
                                <textarea
                                    value={editDescription}
                                    onChange={e => setEditDescription(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] min-h-[100px] resize-none"
                                />
                            </div>

                            <div className="flex justify-end gap-3 mt-8 pt-4 border-t border-[var(--vora-border-subtle)]">
                                <button
                                    type="button"
                                    onClick={() => setIsEditModalOpen(false)}
                                    className="px-4 py-2 rounded text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-raised)] transition-colors cursor-pointer"
                                >
                                    Cancel
                                </button>
                                <button
                                    type="submit"
                                    className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors cursor-pointer"
                                >
                                    Save Changes
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}