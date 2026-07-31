import { useState, useEffect } from 'react';
import { iptvAdminService, type IptvPlaylistVM, type IptvChannelKind } from '../../api/Iptv/iptvAdminService';
import { Modal, ModalHeader } from '../Common/Modal';
import { COUNTRY_OPTIONS } from '../../utils/countries';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    playlist: IptvPlaylistVM;
    serverId?: string;
    onSaved: () => void;
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function CheckboxCard({ checked, onChange, label, hint, accent = 'orange' }: { checked: boolean, onChange: (v: boolean) => void, label: string, hint: string, accent?: 'orange' | 'purple' }) {
    return (
        <label className="flex items-center gap-3 cursor-pointer bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] select-none">
            <input
                type="checkbox"
                checked={checked}
                onChange={e => onChange(e.target.checked)}
                className={`w-4 h-4 cursor-pointer ${accent === 'purple' ? 'accent-purple-500' : 'accent-[var(--vora-accent-500)]'}`}
            />
            <div className="flex flex-col">
                <span className="text-sm font-semibold text-[var(--vora-text-primary)]">{label}</span>
                <span className="text-xs text-[var(--vora-text-muted)]">{hint}</span>
            </div>
        </label>
    );
}

export default function IptvPlaylistEditModal({ isOpen, onClose, playlist, serverId, onSaved }: Props) {
    const [name, setName] = useState(playlist.name);
    const [m3uUrl, setM3uUrl] = useState(playlist.m3uUrl || '');
    const [supportsWebPlayback, setSupportsWebPlayback] = useState(playlist.supportsWebPlayback);
    const [maxConcurrentStreams, setMaxConcurrentStreams] = useState(playlist.maxConcurrentStreams);
    const [isActive, setIsActive] = useState(playlist.isActive);
    const [isRadioPlaylist, setIsRadioPlaylist] = useState(playlist.defaultChannelKind === 'Radio');
    const [countryFilter, setCountryFilter] = useState(playlist.countryFilter ?? '');
    const [enableHealthCheck, setEnableHealthCheck] = useState(playlist.enableHealthCheck ?? false);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        setName(playlist.name);
        setM3uUrl(playlist.m3uUrl || '');
        setSupportsWebPlayback(playlist.supportsWebPlayback);
        setMaxConcurrentStreams(playlist.maxConcurrentStreams);
        setIsActive(playlist.isActive);
        setIsRadioPlaylist(playlist.defaultChannelKind === 'Radio');
        setCountryFilter(playlist.countryFilter ?? '');
        setEnableHealthCheck(playlist.enableHealthCheck ?? false);
        setError(null);
    }, [playlist]);

    const handleSave = async () => {
        if (!name.trim() || !m3uUrl.trim()) {
            setError('Both a name and an M3U playlist URL are required.');
            return;
        }

        setIsSaving(true);
        setError(null);
        try {
            const defaultKind: IptvChannelKind = isRadioPlaylist ? 'Radio' : 'Tv';
            await iptvAdminService.updatePlaylist(
                playlist.id,
                name.trim(),
                m3uUrl.trim(),
                supportsWebPlayback,
                maxConcurrentStreams,
                isActive,
                defaultKind,
                isRadioPlaylist ? (countryFilter || null) : null,
                enableHealthCheck,
                serverId
            );
            onSaved();
            onClose();
        } catch (err) {
            console.error('Failed to update playlist', err);
            setError('Failed to update playlist. Check the URL and try again.');
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="2xl"
            zIndex="z-[200]"
            surface="light"
            closeOnBackdropClick={!isSaving}
            overlayPadding="p-6"
            cardClassName="p-6"
        >
            <ModalHeader
                title="Edit Playlist"
                subtitle={<span className="text-[var(--vora-accent-text)] text-sm font-semibold">{playlist.name}</span>}
                onClose={onClose}
                closeDisabled={isSaving}
                bordered={false}
                surface="light"
            />
            <div className="border-b border-[var(--vora-border-subtle)] mb-6" />

            <div className="space-y-4">
                <div>
                    <FieldLabel>Playlist name</FieldLabel>
                    <input
                        type="text"
                        value={name}
                        onChange={e => setName(e.target.value)}
                        className="vora-input"
                        placeholder="e.g. Pluto TV"
                    />
                </div>

                <div>
                    <FieldLabel>M3U playlist URL</FieldLabel>
                    <input
                        type="text"
                        value={m3uUrl}
                        onChange={e => setM3uUrl(e.target.value)}
                        className="vora-input font-mono text-sm"
                        placeholder="https://…"
                    />
                    <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Changing the URL will trigger a channel re-sync on save.</p>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <CheckboxCard
                        checked={supportsWebPlayback}
                        onChange={setSupportsWebPlayback}
                        label="Supports web browser playback"
                        hint="Uncheck if streams block browsers with CORS."
                    />

                    <div className="bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] flex items-center justify-between gap-3">
                        <div className="flex flex-col min-w-0">
                            <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Max concurrent streams</span>
                            <span className="text-xs text-[var(--vora-text-muted)]">0 = unlimited.</span>
                        </div>
                        <input
                            type="number"
                            min={0}
                            value={maxConcurrentStreams}
                            onChange={e => setMaxConcurrentStreams(parseInt(e.target.value) || 0)}
                            className="vora-input w-20 text-center font-semibold"
                        />
                    </div>
                </div>

                <CheckboxCard
                    checked={isActive}
                    onChange={setIsActive}
                    label="Active"
                    hint="Inactive playlists are hidden from clients but kept for later."
                />

                <CheckboxCard
                    checked={isRadioPlaylist}
                    onChange={setIsRadioPlaylist}
                    label="Radio playlist"
                    hint="When on, channels from this M3U default to Radio (shown in the Audio hub) unless an individual channel is manually flipped. Saving will trigger a channel re-sync."
                    accent="purple"
                />

                <CheckboxCard
                    checked={enableHealthCheck}
                    onChange={setEnableHealthCheck}
                    label="Health-check channels"
                    hint="Probe each stream on sync and nightly; dead channels are hidden from clients until they work again. Leave off for large paid providers to avoid probing bans."
                />

                {isRadioPlaylist && (
                    <div>
                        <FieldLabel>Country</FieldLabel>
                        <select
                            value={countryFilter}
                            onChange={e => setCountryFilter(e.target.value)}
                            className="vora-input"
                        >
                            <option value="">All countries</option>
                            {COUNTRY_OPTIONS.map(c => (
                                <option key={c.code} value={c.code}>{c.name} ({c.code})</option>
                            ))}
                        </select>
                        <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">When set, clients only see this playlist's stations from that country.</p>
                    </div>
                )}

                {error && (
                    <div className="text-sm text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-danger-500)]/20">
                        {error}
                    </div>
                )}
            </div>

            <div className="flex justify-end gap-3 mt-6 pt-6 border-t border-[var(--vora-border-subtle)]">
                <button
                    type="button"
                    onClick={onClose}
                    disabled={isSaving}
                    className="vora-button-secondary disabled:opacity-50"
                >
                    Cancel
                </button>
                <button
                    type="button"
                    onClick={handleSave}
                    disabled={isSaving || !name.trim() || !m3uUrl.trim()}
                    className="vora-button-primary disabled:opacity-50"
                >
                    {isSaving ? 'Saving…' : 'Save changes'}
                </button>
            </div>
        </Modal>
    );
}
