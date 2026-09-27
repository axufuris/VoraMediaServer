import { useState } from 'react';
import { type GeneratedMixDetailVM } from '../../../../api/Music/musicService';
import SaveMixButton from '../../../../components/Collections/SaveMixButton';
import AddToPlaylistButton from '../../../../components/Collections/AddToPlaylistButton';
import ContentRatingBadge from '../../../../components/Media/ContentRatingBadge';
import TrackListeners from '../../../../components/Media/TrackListeners';
import { MakePlaylistDialog } from './AiPlaylistsSection';
import { aiPlaylistService } from '../../../../api/Music/aiPlaylistService';
import { RestartIcon, ShuffleIcon, TrashIcon } from '../../../../components/Client/Primitives/ActionIcons';
import { useDialog } from '../../../../dialogs';
import { resolveReason } from '../../../../utils/apiError';

// What kind of mix the page is showing. AI playlists say so, so nobody
// mistakes one for the ordinary Daily Mixes.
const AI_KINDS = new Set(['AiPlaylist', 'Bridge', 'Blend', 'Requested']);

const mixCoverLabel = (kind: string | undefined, slot: number, hasArtwork: boolean): string | null => {
    if (kind && AI_KINDS.has(kind)) return hasArtwork ? null : 'AI';
    switch (kind) {
        case 'DiscoverMix': return 'Discover';
        case 'MoodMix': return 'Mood';
        case 'ReleaseRadar': return 'Release Radar';
        default: return `Daily Mix ${slot}`;
    }
};

const mixKicker = (kind?: string): string => {
    switch (kind) {
        case 'AiPlaylist': return 'Made for you by AI';
        case 'Bridge': return 'Bridge · made by AI';
        case 'Blend': return 'Blend';
        case 'Requested': return 'Your request · made by AI';
        default: return 'Made for You';
    }
};

interface MusicMixViewProps {
    serverId?: string;
    onRegenerated: () => void;
    onDeleted: () => void;
    isLoading: boolean;
    currentMix: GeneratedMixDetailVM | null;
    isShuffled: boolean;
    toggleShuffle: () => void;
    playMixFromIndex: (startIndex: number) => void;
    formatDuration: (seconds?: number) => string;
}

export default function MusicMixView({
    serverId,
    onRegenerated,
    onDeleted,
    isLoading,
    currentMix,
    isShuffled,
    toggleShuffle,
    playMixFromIndex,
    formatDuration,
}: MusicMixViewProps) {
    const dialog = useDialog();
    const [regenerateOpen, setRegenerateOpen] = useState(false);

    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading mix...</div>;
    }
    if (!currentMix) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Mix not found.</div>;
    }

    const coverLabel = mixCoverLabel(currentMix.kind, currentMix.slot, !!currentMix.artworkUrl);
    const canRegenerate = currentMix.kind === 'Requested' && !!currentMix.prompt;
    const canDelete = currentMix.kind === 'Requested' || currentMix.kind === 'Blend';

    const deleteMix = async () => {
        const confirmed = await dialog.confirm({
            title: 'Delete this playlist?',
            message: `"${currentMix.name}" will be removed. A copy you saved to your playlists stays.`,
            confirmText: 'Delete',
            tone: 'danger',
        });
        if (!confirmed) return;
        try {
            await aiPlaylistService.remove(currentMix.id, serverId);
            onDeleted();
        } catch (err) {
            await dialog.alert({ title: 'Could not delete it', message: resolveReason(err) ?? 'Try again in a moment.', tone: 'danger' });
        }
    };

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div
                    className="w-32 h-32 sm:w-40 sm:h-40 rounded border flex items-center justify-center shrink-0 shadow-lg overflow-hidden relative"
                    style={{ background: 'var(--vora-bg-raised)', borderColor: 'var(--vora-border-subtle)' }}
                >
                    {currentMix.artworkUrl
                        ? <img src={currentMix.artworkUrl} alt="" className={`w-full h-full object-cover ${coverLabel ? 'opacity-60' : ''}`} />
                        : null}
                    {coverLabel && (
                        <div className="absolute inset-0 flex flex-col items-center justify-center text-[var(--vora-text-primary)] drop-shadow-lg">
                            <div className="text-xs uppercase tracking-widest text-[var(--vora-accent-text)] font-bold">{coverLabel}</div>
                            <div className="text-lg sm:text-xl font-bold text-center px-2">{currentMix.descriptionTag ?? 'Mix'}</div>
                        </div>
                    )}
                </div>
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">{mixKicker(currentMix.kind)}</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)] truncate">{currentMix.name}</h2>
                    {currentMix.prompt && <p className="text-sm text-[var(--vora-text-secondary)] mt-2">You asked for “{currentMix.prompt}”</p>}
                    {currentMix.description && <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{currentMix.description}</p>}
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{currentMix.tracks.length} tracks{currentMix.lastDriftAt ? ` • Updated ${new Date(currentMix.lastDriftAt).toLocaleDateString()}` : ''}</p>
                    {currentMix.tracks.length > 0 && (
                        <div className="flex flex-wrap items-center gap-2 mt-4 justify-center sm:justify-start">
                            <button
                                type="button"
                                onClick={() => {
                                    if (isShuffled) toggleShuffle();
                                    playMixFromIndex(0);
                                }}
                                className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                            >
                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                Play
                            </button>
                            <button
                                type="button"
                                onClick={() => {
                                    if (!isShuffled) toggleShuffle();
                                    playMixFromIndex(0);
                                }}
                                className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                            >
                                <ShuffleIcon size={16} />
                                Shuffle
                            </button>
                            <SaveMixButton mixId={currentMix.id} />
                            {canRegenerate && (
                                <button
                                    type="button"
                                    onClick={() => setRegenerateOpen(true)}
                                    className="vora-pill flex cursor-pointer items-center gap-2 rounded px-4 py-2 text-sm font-semibold"
                                >
                                    <RestartIcon size={16} />
                                    Regenerate
                                </button>
                            )}
                            {canDelete && (
                                <button
                                    type="button"
                                    onClick={deleteMix}
                                    aria-label="Delete playlist"
                                    title="Delete playlist"
                                    className="vora-pill flex cursor-pointer items-center rounded px-3 py-2 text-sm font-semibold"
                                    style={{ color: 'var(--vora-danger-text)' }}
                                >
                                    <TrashIcon />
                                </button>
                            )}
                        </div>
                    )}
                </div>
            </div>

            {currentMix.tracks.length === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                    <p className="mb-2">This mix is empty.</p>
                    <p className="text-xs">It will populate once you build more play history.</p>
                </div>
            ) : (
                <div className="space-y-1">
                    {currentMix.tracks.map((t, idx) => (
                        <div
                            key={t.id}
                            onClick={() => playMixFromIndex(idx)}
                            className="w-full text-left flex items-center gap-3 p-2 vora-row-interactive border border-transparent rounded transition-all cursor-pointer group"
                        >
                            <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] tabular-nums shrink-0">{idx + 1}</div>
                            <div className="flex-1 min-w-0">
                                <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate flex items-center gap-2">
                                    <span className="truncate">{t.title}</span>
                                    <ContentRatingBadge rating={t.contentRating} />
                                </div>
                                {t.artist && <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist}</div>}
                            </div>
                            <TrackListeners listeners={t.globalListeners} />
                            <AddToPlaylistButton mediaId={t.id} title={t.title} />
                            <div className="text-xs text-[var(--vora-text-muted)] shrink-0 tabular-nums">{formatDuration(t.durationSeconds)}</div>
                        </div>
                    ))}
                </div>
            )}
            {regenerateOpen && currentMix.prompt && (
                <MakePlaylistDialog
                    serverId={serverId}
                    regenerate={{ mixId: currentMix.id, prompt: currentMix.prompt, trackCount: currentMix.tracks.length }}
                    onClose={() => setRegenerateOpen(false)}
                    onMade={() => { setRegenerateOpen(false); onRegenerated(); }}
                />
            )}
        </>
    );
}
