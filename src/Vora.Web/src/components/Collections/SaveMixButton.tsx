import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { musicService } from '../../api/Music/musicService';
import { useDialog } from '../../dialogs';
import type { SavedCopyState } from '../../pages/Client/Playlists/PlaylistSharing';

// Keeps a generated mix - a Daily Mix today, an AI playlist tomorrow - as the
// profile's own playlist, so a refresh can't take away one they liked. Opens
// the new playlist with the same "saved" confirmation a copy gets.
export default function SaveMixButton({ mixId }: { mixId: string }) {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();
    const [saving, setSaving] = useState(false);

    const save = async () => {
        if (saving) return;
        setSaving(true);
        try {
            const { id } = await musicService.saveMixAsPlaylist(mixId, serverId);
            const state: SavedCopyState = { savedCopy: true };
            navigate(serverId ? `/server/${serverId}/playlist/${id}` : `/playlist/${id}`, { state });
        } catch {
            setSaving(false);
            await dialog.alert('Could not save this mix to your playlists.');
        }
    };

    return (
        <button
            type="button"
            onClick={save}
            disabled={saving}
            className="vora-pill flex cursor-pointer items-center gap-2 rounded px-4 py-2 text-sm font-semibold disabled:opacity-60"
        >
            <svg className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" d="M4 6h11M4 12h11M4 18h7M18 14v6M15 17h6" /></svg>
            {saving ? 'Saving…' : 'Save to my playlists'}
        </button>
    );
}
