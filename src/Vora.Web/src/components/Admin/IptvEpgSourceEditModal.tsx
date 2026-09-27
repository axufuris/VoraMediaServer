import { useState, useEffect } from 'react';
import { iptvEpgAdminService, type IptvEpgSourceVM } from '../../api/Iptv/iptvEpgAdminService';
import { Modal, ModalHeader } from '../Common/Modal';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    source: IptvEpgSourceVM;
    serverId?: string;
    onSaved: () => void;
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

export default function IptvEpgSourceEditModal({ isOpen, onClose, source, serverId, onSaved }: Props) {
    const [name, setName] = useState(source.name);
    const [xmlTvUrl, setXmlTvUrl] = useState(source.xmlTvUrl);
    const [priority, setPriority] = useState(source.priority);
    const [isActive, setIsActive] = useState(source.isActive);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        setName(source.name);
        setXmlTvUrl(source.xmlTvUrl);
        setPriority(source.priority);
        setIsActive(source.isActive);
        setError(null);
    }, [source]);

    const handleSave = async () => {
        if (!name.trim() || !xmlTvUrl.trim()) {
            setError('Both a name and an XMLTV URL are required.');
            return;
        }

        setIsSaving(true);
        setError(null);
        try {
            await iptvEpgAdminService.updateSource(
                source.id,
                name.trim(),
                xmlTvUrl.trim(),
                priority,
                isActive,
                serverId
            );
            onSaved();
            onClose();
        } catch (err) {
            console.error('Failed to update EPG source', err);
            setError('Failed to update EPG source. Check the URL and try again.');
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
                title="Edit EPG Source"
                subtitle={<span className="text-[var(--vora-accent-text)] text-sm font-semibold">{source.name}</span>}
                onClose={onClose}
                closeDisabled={isSaving}
                bordered={false}
                surface="light"
            />
            <div className="border-b border-[var(--vora-border-subtle)] mb-6" />

            <div className="space-y-4">
                <div>
                    <FieldLabel>Source name</FieldLabel>
                    <input
                        type="text"
                        value={name}
                        onChange={e => setName(e.target.value)}
                        className="vora-input"
                        placeholder="e.g. US Sports Bundle"
                    />
                </div>

                <div>
                    <FieldLabel>XMLTV EPG URL</FieldLabel>
                    <input
                        type="text"
                        value={xmlTvUrl}
                        onChange={e => setXmlTvUrl(e.target.value)}
                        className="vora-input font-mono text-sm"
                        placeholder="https://…"
                    />
                    <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Saving will trigger a full EPG re-sync.</p>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div className="bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] flex items-center justify-between gap-3">
                        <div className="flex flex-col min-w-0">
                            <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Priority</span>
                            <span className="text-xs text-[var(--vora-text-muted)]">Lower numbers sync first.</span>
                        </div>
                        <input
                            type="number"
                            value={priority}
                            onChange={e => setPriority(parseInt(e.target.value) || 0)}
                            className="vora-input w-20 text-center font-semibold"
                        />
                    </div>

                    <label className="flex items-center gap-3 cursor-pointer bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] select-none">
                        <input
                            type="checkbox"
                            checked={isActive}
                            onChange={e => setIsActive(e.target.checked)}
                            className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                        />
                        <div className="flex flex-col">
                            <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Active</span>
                            <span className="text-xs text-[var(--vora-text-muted)]">Inactive sources are skipped on sync.</span>
                        </div>
                    </label>
                </div>

                {error && (
                    <div className="text-sm text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-danger-500)]/20">
                        {error}
                    </div>
                )}
            </div>

            <div className="flex justify-end gap-3 mt-6 pt-6 border-t border-[var(--vora-border-subtle)]">
                <button type="button" onClick={onClose} disabled={isSaving} className="vora-button-secondary disabled:opacity-50">
                    Cancel
                </button>
                <button
                    type="button"
                    onClick={handleSave}
                    disabled={isSaving || !name.trim() || !xmlTvUrl.trim()}
                    className="vora-button-primary disabled:opacity-50"
                >
                    {isSaving ? 'Saving…' : 'Save changes'}
                </button>
            </div>
        </Modal>
    );
}
