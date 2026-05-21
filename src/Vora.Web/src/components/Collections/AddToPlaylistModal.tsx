import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { playlistService, type PlaylistSummaryVM } from '../../api/Collections/playlistService';
import { useDialog } from '../../dialogs';
import { Modal, ModalHeader } from '../Common/Modal';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    mediaId: string;
}

export default function AddToPlaylistModal({
    isOpen, onClose, mediaId }: Props) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [playlists, setPlaylists] = useState<PlaylistSummaryVM[]>([]);
    const [activePlaylistIds, setActivePlaylistIds] = useState<Set<string>>(new Set());
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (!isOpen) return;
        let cancelled = false;
        Promise.all([
            playlistService.getPlaylists(serverId),
            playlistService.getPlaylistsContainingItem(mediaId, serverId)
        ]).then(([allPlaylists, containedIds]) => {
            if (cancelled) return;
            setPlaylists(allPlaylists);
            setActivePlaylistIds(new Set(containedIds));
            setLoading(false);
        }).catch(err => {
            if (cancelled) return;
            console.error(err);
            setLoading(false);
        });
        return () => { cancelled = true; };
    }, [isOpen, mediaId, serverId]);

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
            <div className="border-b border-gray-800 mb-6" />

            {loading ? (
                <div className="py-8 text-center text-gray-500 font-medium">Loading playlists...</div>
            ) : playlists.length === 0 ? (
                <div className="py-8 text-center text-gray-500 font-medium">
                    You don't have any playlists yet.<br />
                    <span className="text-sm mt-2 block">Create one from the Playlists page!</span>
                </div>
            ) : (
                <div className="space-y-2 max-h-[50vh] overflow-y-auto custom-scrollbar pr-2">
                    {playlists.map(p => {
                        const isActive = activePlaylistIds.has(p.id);
                        return (
                            <div
                                key={p.id}
                                onClick={() => togglePlaylist(p.id)}
                                className={`flex items-center gap-4 p-3 rounded-lg border transition-all cursor-pointer ${isActive ? 'bg-orange-600/10 border-orange-500/50' : 'bg-gray-800/50 border-gray-700 hover:bg-gray-800'}`}
                            >
                                <div className={`w-6 h-6 rounded flex items-center justify-center border transition-colors shrink-0 ${isActive ? 'bg-orange-500 border-orange-500 text-white' : 'bg-gray-900 border-gray-600'}`}>
                                    {isActive && <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>}
                                </div>
                                <div className="flex-1 overflow-hidden">
                                    <h3 className={`font-bold text-sm truncate transition-colors ${isActive ? 'text-orange-400' : 'text-gray-200'}`}>{p.name}</h3>
                                    <p className="text-xs text-gray-500">{p.itemCount} Items</p>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}
        </Modal>
    );
}
