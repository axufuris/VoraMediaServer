import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { aiPlaylistService, type AiPlaylistVM, type AiPlaylistsVM, type BlendPartner } from '../../../../api/Music/aiPlaylistService';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';
import { Modal, ModalHeader } from '../../../../components/Common/Modal';
import { resolveReason } from '../../../../utils/apiError';
import type { MusicNavState } from './musicNavState';

// "Made for you by AI" on the For You page: the weekly themes and Bridge, any
// Blends, and the "Make me a playlist for..." and Blend buttons. Hidden
// entirely when the server has AI playlists off or this profile opted out.
interface AiPlaylistsSectionProps {
    updateNav: (next: MusicNavState) => void;
}

const KIND_LABEL: Record<string, string> = {
    AiPlaylist: 'AI',
    Bridge: 'Bridge',
    Blend: 'Blend',
    Requested: 'Request',
};

const REQUEST_EXAMPLES = [
    'Cooking dinner, 90s hip hop, nothing sad',
    'A road trip that builds from calm to loud',
    'Songs to focus to, no lyrics',
    'Rainy Sunday morning',
];

export default function AiPlaylistsSection({ updateNav }: AiPlaylistsSectionProps) {
    const { serverId } = useParams<{ serverId?: string }>();
    const [data, setData] = useState<AiPlaylistsVM | null>(null);
    const [requestOpen, setRequestOpen] = useState(false);
    const [blendOpen, setBlendOpen] = useState(false);

    const load = useCallback(() => {
        aiPlaylistService.get(serverId).then(setData).catch(() => setData(null));
    }, [serverId]);

    useEffect(() => { load(); }, [load]);

    if (!data?.enabled) return null;

    const open = (mixId: string) => updateNav({ view: 'mix', mixId });
    const tiles = [...data.weekly, ...data.blends];

    const actions = (
        <div className="flex flex-wrap items-center gap-2">
            {data.requestsEnabled && (
                <button type="button" onClick={() => setRequestOpen(true)} className="vora-pill cursor-pointer rounded-full px-3 py-1.5 text-xs font-semibold">
                    Make me a playlist
                </button>
            )}
            <button type="button" onClick={() => setBlendOpen(true)} className="vora-pill cursor-pointer rounded-full px-3 py-1.5 text-xs font-semibold">
                Blend
            </button>
        </div>
    );

    return (
        <>
            {tiles.length > 0 ? (
                <MediaRow title="Made for you by AI" actions={actions}>
                    {tiles.map(p => <MediaRowItem key={p.id}><AiTile playlist={p} onOpen={open} /></MediaRowItem>)}
                </MediaRow>
            ) : (
                <section className="px-8" aria-label="Made for you by AI">
                    <div className="flex flex-wrap items-center justify-between gap-3 rounded-[var(--vora-radius-md)] border p-4" style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-bg-surface)' }}>
                        <div>
                            <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Made for you by AI</h2>
                            <p className="text-sm text-[var(--vora-text-muted)]">Your weekly AI playlists appear here once you've listened to a little more music.</p>
                        </div>
                        {actions}
                    </div>
                </section>
            )}

            {data.requests.length > 0 && (
                <MediaRow title="Your requests">
                    {data.requests.map(p => <MediaRowItem key={p.id}><AiTile playlist={p} onOpen={open} /></MediaRowItem>)}
                </MediaRow>
            )}

            {requestOpen && (
                <MakePlaylistDialog
                    serverId={serverId}
                    onClose={() => setRequestOpen(false)}
                    onMade={mixId => { setRequestOpen(false); load(); open(mixId); }}
                />
            )}
            {blendOpen && (
                <BlendDialog
                    serverId={serverId}
                    onClose={() => setBlendOpen(false)}
                    onMade={mixId => { setBlendOpen(false); load(); open(mixId); }}
                />
            )}
        </>
    );
}

function AiTile({ playlist, onOpen }: { playlist: AiPlaylistVM; onOpen: (mixId: string) => void }) {
    const caption = playlist.kind === 'Blend' && playlist.partnerName
        ? `With ${playlist.partnerName}`
        : playlist.kind === 'Requested' && playlist.prompt ? `"${playlist.prompt}"` : (playlist.description ?? `${playlist.trackCount} songs`);

    return (
        <MediaCard
            title={playlist.name}
            captionLines={[caption]}
            imageUrl={playlist.artworkUrl ?? undefined}
            shape="square"
            size="xs"
            badge={
                <span className="rounded px-1.5 py-0.5 font-bold uppercase tracking-widest" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', fontSize: 'var(--vora-card-badge-size)' }}>
                    {KIND_LABEL[playlist.kind] ?? 'AI'}
                </span>
            }
            onClick={() => onOpen(playlist.id)}
        />
    );
}

function MakePlaylistDialog({ serverId, onClose, onMade }: { serverId?: string; onClose: () => void; onMade: (mixId: string) => void }) {
    const [prompt, setPrompt] = useState('');
    const [making, setMaking] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!prompt.trim() || making) return;
        setMaking(true);
        setError(null);
        try {
            const { mixId } = await aiPlaylistService.make(prompt.trim(), serverId);
            onMade(mixId);
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not make that playlist.');
            setMaking(false);
        }
    };

    return (
        <Modal isOpen onClose={onClose} size="sm" zIndex="z-[200]" cardClassName="p-6">
            <ModalHeader title="Make me a playlist for…" onClose={onClose} bordered={false} />
            <form onSubmit={submit} className="space-y-3">
                <textarea
                    autoFocus
                    aria-label="What the playlist is for"
                    value={prompt}
                    maxLength={300}
                    onChange={e => setPrompt(e.target.value)}
                    placeholder="A mood, an activity, an era — anything"
                    className="vora-input min-h-[90px] w-full resize-none"
                />
                <div className="flex flex-wrap gap-2">
                    {REQUEST_EXAMPLES.map(example => (
                        <button key={example} type="button" onClick={() => setPrompt(example)} className="vora-pill cursor-pointer rounded-full px-2.5 py-1 text-xs">
                            {example}
                        </button>
                    ))}
                </div>
                <p className="text-xs text-[var(--vora-text-muted)]">Songs come from your library and follow your profile's settings.</p>
                {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
                <div className="flex justify-end gap-2 pt-1">
                    <button type="button" onClick={onClose} className="vora-button-secondary">Cancel</button>
                    <button type="submit" disabled={!prompt.trim() || making} className="vora-button-primary disabled:opacity-50">
                        {making ? 'Making your playlist…' : 'Make it'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}

function BlendDialog({ serverId, onClose, onMade }: { serverId?: string; onClose: () => void; onMade: (mixId: string) => void }) {
    const [partners, setPartners] = useState<BlendPartner[] | null>(null);
    const [busy, setBusy] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        aiPlaylistService.getBlendPartners(serverId).then(setPartners).catch(() => setPartners([]));
    }, [serverId]);

    const blend = async (partner: BlendPartner) => {
        if (busy) return;
        setBusy(partner.profileId);
        setError(null);
        try {
            const { mixId } = await aiPlaylistService.blend(partner.profileId, serverId);
            onMade(mixId);
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not make that Blend.');
            setBusy(null);
        }
    };

    return (
        <Modal isOpen onClose={onClose} size="sm" zIndex="z-[200]" cardClassName="p-6">
            <ModalHeader title="Blend with…" onClose={onClose} bordered={false} />
            <p className="mb-4 text-sm text-[var(--vora-text-muted)]">A playlist of where your taste and theirs meet. Only profiles that use AI playlists are shown.</p>
            {partners === null ? (
                <div className="vora-skeleton h-24" />
            ) : partners.length === 0 ? (
                <p className="py-4 text-center text-sm text-[var(--vora-text-muted)]">No one else on this server uses AI playlists yet.</p>
            ) : (
                <ul className="space-y-2">
                    {partners.map(p => (
                        <li key={p.profileId}>
                            <button
                                type="button"
                                onClick={() => blend(p)}
                                disabled={busy !== null}
                                className="vora-row-interactive flex w-full cursor-pointer items-center gap-3 rounded-[var(--vora-radius-md)] border p-2.5 text-left disabled:opacity-60"
                                style={{ borderColor: 'var(--vora-border-subtle)' }}
                            >
                                <span aria-hidden="true" className="flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-full font-bold" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-secondary)' }}>
                                    {p.imageUrl ? <img src={p.imageUrl} alt="" className="h-full w-full object-cover" /> : p.name.charAt(0).toUpperCase()}
                                </span>
                                <span className="text-sm font-semibold text-[var(--vora-text-primary)]">{busy === p.profileId ? `Blending with ${p.name}…` : p.name}</span>
                            </button>
                        </li>
                    ))}
                </ul>
            )}
            {error && <p role="alert" className="mt-3 text-sm text-[var(--vora-danger-text)]">{error}</p>}
        </Modal>
    );
}
