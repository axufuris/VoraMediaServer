import { useState, useMemo } from 'react';
import { iptvAdminService, type IptvChannelVM, type IptvChannelKind } from '../../api/Iptv/iptvAdminService';
import { Modal, ModalHeader } from '../Common/Modal';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    playlistName: string;
    channels: IptvChannelVM[];
    serverId?: string;
    onChannelToggled: () => void;
}

type KindFilter = 'All' | 'Tv' | 'Radio';

export default function IptvChannelsModal({ isOpen, onClose, playlistName, channels, serverId, onChannelToggled }: Props) {
    const [search, setSearch] = useState('');
    const [kindFilter, setKindFilter] = useState<KindFilter>('All');
    const [localChannels, setLocalChannels] = useState<IptvChannelVM[]>(channels);

    const filteredChannels = useMemo(() => {
        return localChannels
            .filter(c => kindFilter === 'All' || c.kind === kindFilter)
            .filter(c => c.name.toLowerCase().includes(search.toLowerCase()) || (c.groupTitle || '').toLowerCase().includes(search.toLowerCase()))
            .sort((a, b) => a.name.localeCompare(b.name));
    }, [localChannels, search, kindFilter]);

    const handleToggle = async (channelId: string, currentState: boolean) => {
        try {
            setLocalChannels(prev => prev.map(c => c.id === channelId ? { ...c, isHiddenByAdmin: !currentState } : c));
            await iptvAdminService.toggleChannelVisibility(channelId, serverId);
            onChannelToggled();
        } catch (error) {
            console.error('Failed to toggle channel', error);
            setLocalChannels(prev => prev.map(c => c.id === channelId ? { ...c, isHiddenByAdmin: currentState } : c));
        }
    };

    const handleKindToggle = async (channelId: string, currentKind: IptvChannelKind) => {
        const next: IptvChannelKind = currentKind === 'Radio' ? 'Tv' : 'Radio';
        try {
            setLocalChannels(prev => prev.map(c => c.id === channelId ? { ...c, kind: next } : c));
            await iptvAdminService.setChannelKind(channelId, next, serverId);
            onChannelToggled();
        } catch (error) {
            console.error('Failed to change channel kind', error);
            setLocalChannels(prev => prev.map(c => c.id === channelId ? { ...c, kind: currentKind } : c));
        }
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="3xl"
            zIndex="z-[200]"
            surface="light"
            closeOnBackdropClick
            overlayPadding="p-6"
            cardClassName="p-6 flex flex-col max-h-[90vh] h-full"
        >
            <ModalHeader
                title="Manage Channels"
                subtitle={<span className="text-[var(--vora-accent-text)] text-sm font-semibold">{playlistName} ({localChannels.length} channels)</span>}
                onClose={onClose}
                bordered={false}
                surface="light"
            />
            <div className="border-b border-[var(--vora-border-subtle)] mb-4" />

            <div className="flex gap-3 mb-4 shrink-0">
                <input
                    type="text"
                    placeholder="Search channels by name or group…"
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    className="vora-input flex-1"
                />
                <div className="flex border border-[var(--vora-border-strong)] rounded-[var(--vora-radius-md)] overflow-hidden">
                    {(['All', 'Tv', 'Radio'] as const).map(k => {
                        const isActive = kindFilter === k;
                        return (
                            <button
                                key={k}
                                type="button"
                                onClick={() => setKindFilter(k)}
                                className={`px-4 text-xs font-semibold transition-colors cursor-pointer ${isActive ? 'bg-[var(--vora-accent-500)] text-white' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)]'}`}
                            >
                                {k === 'Tv' ? 'TV' : k}
                            </button>
                        );
                    })}
                </div>
            </div>

            <div className="flex-1 overflow-y-auto pr-2 space-y-2">
                {filteredChannels.map(c => (
                    <div key={c.id} className={`flex items-center justify-between p-3 rounded-[var(--vora-radius-md)] border transition-colors ${c.isHiddenByAdmin ? 'bg-[var(--vora-danger-soft)]/40 border-[var(--vora-danger-500)]/30' : 'bg-[var(--vora-bg-surface)] border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-strong)]'}`}>
                        <div className="flex items-center gap-4">
                            <div className="w-12 h-8 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded flex items-center justify-center shrink-0 overflow-hidden">
                                {c.logoUrl ? <img src={c.logoUrl} alt="" className="max-w-full max-h-full object-contain" /> : <span className="text-[8px] text-[var(--vora-text-disabled)]">No logo</span>}
                            </div>
                            <div className="flex flex-col">
                                <span className={`text-sm font-semibold ${c.isHiddenByAdmin ? 'text-[var(--vora-text-disabled)] line-through' : 'text-[var(--vora-text-primary)]'}`}>{c.name}</span>
                                <span className="text-[10px] text-[var(--vora-text-muted)]">{c.groupTitle || 'No group'}</span>
                            </div>
                        </div>
                        <div className="flex items-center gap-2">
                            <button
                                type="button"
                                onClick={() => handleKindToggle(c.id, c.kind)}
                                className={`px-3 py-1.5 rounded-[var(--vora-radius-md)] text-xs font-semibold transition-colors cursor-pointer border ${c.kind === 'Radio' ? 'bg-purple-100 text-purple-800 border-purple-200 hover:bg-purple-600 hover:text-white hover:border-purple-600' : 'bg-[var(--vora-info-soft)] text-[var(--vora-info-text)] border-[var(--vora-info-500)]/20 hover:bg-[var(--vora-info-500)] hover:text-white hover:border-[var(--vora-info-500)]'}`}
                                title="Click to flip between TV and Radio"
                            >
                                {c.kind === 'Radio' ? 'Radio' : 'TV'}
                            </button>
                            <button
                                type="button"
                                onClick={() => handleToggle(c.id, c.isHiddenByAdmin)}
                                className={`px-4 py-1.5 rounded-[var(--vora-radius-md)] text-xs font-semibold transition-colors cursor-pointer ${c.isHiddenByAdmin ? 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-border-strong)]' : 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)] border border-[var(--vora-accent-500)]/30 hover:bg-[var(--vora-accent-500)] hover:text-white hover:border-[var(--vora-accent-500)]'}`}
                            >
                                {c.isHiddenByAdmin ? 'Hidden (click to show)' : 'Visible (click to hide)'}
                            </button>
                        </div>
                    </div>
                ))}
            </div>
        </Modal>
    );
}
