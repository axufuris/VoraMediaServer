import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { mediaTrashService, type TrashMediaItem } from '../../api/Media/mediaTrashService';
import { thumbUrl } from '../../utils/thumbnails';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

const TYPE_LABELS: Record<string, string> = {
    Movie: 'Movie',
    TvShow: 'Show',
    Season: 'Season',
    Episode: 'Episode',
};

function seasonEpisodeLabel(item: TrashMediaItem): string | null {
    if (item.type === 'Episode' && item.seasonNumber != null && item.episodeNumber != null) {
        return `S${String(item.seasonNumber).padStart(2, '0')}E${String(item.episodeNumber).padStart(2, '0')}`;
    }
    if (item.type === 'Season' && item.seasonNumber != null) {
        return `Season ${item.seasonNumber}`;
    }
    return null;
}

function formatMissingSince(iso: string) {
    const then = new Date(iso).getTime();
    const days = Math.floor((Date.now() - then) / (1000 * 60 * 60 * 24));
    if (days <= 0) return 'today';
    if (days === 1) return '1 day ago';
    return `${days} days ago`;
}

export default function MediaTrashPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [items, setItems] = useState<TrashMediaItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [busyId, setBusyId] = useState<string | null>(null);

    const load = useCallback(async () => {
        setLoading(true);
        try {
            setItems(await mediaTrashService.getTrash(serverId));
        } catch (error) {
            console.error('Failed to load media trash', error);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => { load(); }, [load]);

    const handleRestore = async (item: TrashMediaItem) => {
        setBusyId(item.id);
        try {
            await mediaTrashService.restore(item.id, serverId);
            setItems(prev => prev.filter(i => i.id !== item.id));
        } catch (error: unknown) {
            const err = error as { response?: { data?: { message?: string } } };
            await dialog.alert(err.response?.data?.message || 'Failed to restore item.');
        } finally {
            setBusyId(null);
        }
    };

    const handlePurge = async (item: TrashMediaItem) => {
        if (!await dialog.confirm(`Permanently delete "${item.title}" and its watch progress, ratings, and markers? This cannot be undone.`)) return;
        setBusyId(item.id);
        try {
            await mediaTrashService.purge(item.id, serverId);
            setItems(prev => prev.filter(i => i.id !== item.id));
        } catch (error: unknown) {
            const err = error as { response?: { data?: { message?: string } } };
            await dialog.alert(err.response?.data?.message || 'Failed to delete item.');
        } finally {
            setBusyId(null);
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Media Trash"
                description="Items whose files went missing are held here instead of being deleted, so watch progress and ratings survive. If the file returns, the item is restored automatically on the next scan."
            />

            <div className="px-8 pb-10 max-w-5xl mx-auto pt-6">
                {loading ? (
                    <div className="vora-skeleton h-48" />
                ) : items.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="Trash is empty"
                            description="No media is currently missing. When a file disappears, its item shows up here."
                            icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M5 13l4 4L19 7" /></svg>}
                        />
                    </div>
                ) : (
                    <div className="space-y-2">
                        {items.map(item => (
                            <div key={item.id} className="vora-card p-3 flex items-center gap-4">
                                <div className="w-12 h-[72px] shrink-0 overflow-hidden rounded-[var(--vora-radius-sm)]" style={{ background: 'var(--vora-bg-sunken)' }}>
                                    {item.posterUrl && (
                                        <img src={thumbUrl(item.posterUrl, 200)} alt={item.title} loading="lazy" decoding="async" className="w-full h-full object-cover" />
                                    )}
                                </div>
                                <div className="min-w-0 flex-1">
                                    <div className="flex items-center gap-2">
                                        <span className="text-[11px] font-semibold px-2 py-0.5 rounded bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)]">{TYPE_LABELS[item.type] ?? item.type}</span>
                                        {seasonEpisodeLabel(item) && (
                                            <span className="text-[11px] font-mono font-semibold px-1.5 py-0.5 rounded bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] shrink-0">{seasonEpisodeLabel(item)}</span>
                                        )}
                                        <p className="font-medium text-[var(--vora-text-primary)] truncate">{item.title}</p>
                                    </div>
                                    <p className="text-xs text-[var(--vora-text-muted)] mt-1 truncate">
                                        {item.seriesTitle ? `${item.seriesTitle} · ` : ''}{item.libraryName ?? 'Unknown library'} · missing {formatMissingSince(item.missingSince)}
                                    </p>
                                </div>
                                <div className="flex gap-2 shrink-0">
                                    <button
                                        type="button"
                                        disabled={busyId === item.id}
                                        onClick={() => handleRestore(item)}
                                        className="text-xs font-semibold px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-border-strong)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded-[var(--vora-radius-md)] transition-colors cursor-pointer disabled:opacity-50"
                                    >
                                        Restore
                                    </button>
                                    <button
                                        type="button"
                                        disabled={busyId === item.id}
                                        onClick={() => handlePurge(item)}
                                        className="text-xs font-semibold px-3 py-1.5 bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] text-[var(--vora-danger-text)] hover:text-[var(--vora-text-primary)] rounded-[var(--vora-radius-md)] transition-colors cursor-pointer disabled:opacity-50"
                                    >
                                        Delete now
                                    </button>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
