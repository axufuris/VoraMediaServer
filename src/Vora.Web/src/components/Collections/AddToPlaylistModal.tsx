import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { playlistService, type PlaylistSummaryVM } from '../../api/Collections/playlistService';
import type { PlaylistMediaType } from '../../api/Music/smartPlaylistService';
import { useDialog } from '../../dialogs';
import { Modal, ModalHeader } from '../Common/Modal';

// Which playlists can take the item. A song goes in a music or mixed playlist,
// a film or episode in anything but a music one; offering the rest only lets
// someone fill a music playlist with films by mistake.
export type PlaylistItemKind = 'music' | 'video';

const ACCEPTS: Record<PlaylistItemKind, PlaylistMediaType[]> = {
    music: ['Music', 'Mixed'],
    video: ['Movies', 'Shows', 'Mixed'],
};

const NEW_PLAYLIST_TYPE: Record<PlaylistItemKind, PlaylistMediaType> = {
    music: 'Music',
    video: 'Mixed',
};

interface Props {
    isOpen: boolean;
    onClose: () => void;
    mediaId: string;
    kind?: PlaylistItemKind;
    serverId?: string;
}

export default function AddToPlaylistModal({
    isOpen, onClose, mediaId, kind, serverId: serverIdProp }: Props) {
    const dialog = useDialog();
    const { serverId: routeServerId } = useParams<{ serverId?: string }>();
    const serverId = serverIdProp ?? routeServerId;
    const [playlists, setPlaylists] = useState<PlaylistSummaryVM[]>([]);
    const [activePlaylistIds, setActivePlaylistIds] = useState<Set<string>>(new Set());
    const [loading, setLoading] = useState(true);
    const [newName, setNewName] = useState('');
    const [creating, setCreating] = useState(false);

    useEffect(() => {
        if (!isOpen) return;
        let cancelled = false;
        Promise.all([
            playlistService.getPlaylists(serverId),
            playlistService.getPlaylistsContainingItem(mediaId, serverId)
        ]).then(([allPlaylists, containedIds]) => {
            if (cancelled) return;
            setPlaylists(kind ? allPlaylists.filter(p => ACCEPTS[kind].includes(p.mediaType)) : allPlaylists);
            setActivePlaylistIds(new Set(containedIds));
            setLoading(false);
        }).catch(err => {
            if (cancelled) return;
            console.error(err);
            setLoading(false);
        });
        return () => { cancelled = true; };
    }, [isOpen, mediaId, kind, serverId]);

    const togglePlaylist = async (playlistId: string) => {
        const isCurrentlyIn = activePlaylistIds.has(playlistId);

        const newSet = new Set(activePlaylistIds);
        if (isCurrentlyIn) newSet.delete(playlistId);
        else newSet.add(playlistId);
        setActivePlaylistIds(newSet);

        try {
            if (isCurrentlyIn) {
                await playlistService.removeMediaFromPlaylist(playlistId, mediaId, serverId);
            } else {
                await playlistService.addToPlaylist(playlistId, mediaId, serverId);
            }
        } catch (error) {
            console.error("Failed to toggle playlist", error);
            const revertSet = new Set(activePlaylistIds);
            if (isCurrentlyIn) revertSet.add(playlistId);
            else revertSet.delete(playlistId);
            setActivePlaylistIds(revertSet);
            await dialog.alert("Failed to update playlist.");
        }
    };

    // A new playlist, with this item already in it. Without this the only way
    // to start one was to leave, create it on the Playlists page and come back.
    const createWithItem = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        const name = newName.trim();
        if (!name || creating) return;
        setCreating(true);
        try {
            const mediaType = kind ? NEW_PLAYLIST_TYPE[kind] : 'Mixed';
            const { id } = await playlistService.createPlaylist(name, undefined, mediaType, serverId);
            await playlistService.addToPlaylist(id, mediaId, serverId);
            const created: PlaylistSummaryVM = {
                id, name, mediaType, itemCount: 1, posterUrls: [], backdropUrls: [],
                isShared: false, isOwner: true, ownerName: '',
            };
            setPlaylists(prev => [created, ...prev]);
            setActivePlaylistIds(prev => new Set(prev).add(id));
            setNewName('');
        } catch (error) {
            console.error('Failed to create playlist', error);
            await dialog.alert('Could not create the playlist.');
        } finally {
            setCreating(false);
        }
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="sm"
            zIndex="z-[200]"
            surface="gray-900"
            closeOnBackdropClick
            cardClassName="p-6"
        >
            <ModalHeader title="Add to Playlist" onClose={onClose} bordered={false} />
            <div className="border-b border-[var(--vora-border-subtle)] mb-4" />

            <form onSubmit={createWithItem} className="mb-4 flex gap-2">
                <input
                    type="text"
                    value={newName}
                    onChange={e => setNewName(e.target.value)}
                    placeholder="New playlist name"
                    aria-label="New playlist name"
                    className="min-w-0 flex-1 rounded-md border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-canvas)] px-3 py-2 text-sm text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]"
                />
                <button
                    type="submit"
                    disabled={!newName.trim() || creating}
                    className="vora-button-primary shrink-0 cursor-pointer rounded-md px-3 py-2 text-sm font-semibold disabled:opacity-50"
                >
                    {creating ? 'Creating…' : 'Create'}
                </button>
            </form>

            {loading ? (
                <div className="py-8 text-center text-[var(--vora-text-muted)] font-medium">Loading playlists...</div>
            ) : playlists.length === 0 ? (
                <div className="py-6 text-center text-sm text-[var(--vora-text-muted)]">
                    No playlists yet. Name one above to create it with this in it.
                </div>
            ) : (
                <div className="space-y-2 max-h-[50vh] overflow-y-auto custom-scrollbar pr-2">
                    {playlists.map(p => {
                        const isActive = activePlaylistIds.has(p.id);
                        return (
                            <div
                                key={p.id}
                                role="button"
                                tabIndex={0}
                                aria-pressed={isActive}
                                onClick={() => togglePlaylist(p.id)}
                                onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); togglePlaylist(p.id); } }}
                                className={`flex items-center gap-4 p-3 rounded-lg border transition-all cursor-pointer ${isActive ? 'bg-[var(--vora-accent-500)]/10 border-[var(--vora-accent-500)]/50' : 'bg-[var(--vora-bg-sunken)]/50 border-[var(--vora-border-subtle)] hover:bg-[var(--vora-bg-sunken)]'}`}
                            >
                                <div className={`w-6 h-6 rounded flex items-center justify-center border transition-colors shrink-0 ${isActive ? 'bg-[var(--vora-accent-500)] border-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)]'}`}>
                                    {isActive && <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>}
                                </div>
                                <div className="flex-1 overflow-hidden">
                                    <h3 className={`font-bold text-sm truncate transition-colors ${isActive ? 'text-[var(--vora-accent-500)]' : 'text-[var(--vora-text-secondary)]'}`}>{p.name}</h3>
                                    <p className="text-xs text-[var(--vora-text-muted)]">{p.itemCount} {p.itemCount === 1 ? 'item' : 'items'}</p>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}
        </Modal>
    );
}
