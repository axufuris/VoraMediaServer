import { useState, useEffect } from 'react';
import { mediaService, type MediaMarker, type MediaMarkerType } from '../../api/Media/mediaService';
import { useDialog } from '../../dialogs';

interface MarkerEditorModalProps {
    isOpen: boolean;
    mediaItemId: string;
    mediaItemTitle: string;
    durationSeconds?: number;
    serverId?: string;
    onClose: () => void;
    onSaved?: () => void;
}

const MARKER_TYPES: MediaMarkerType[] = ['Intro', 'Recap', 'Preview', 'Credits', 'CreditsScene'];

function formatHms(totalSeconds: number): string {
    if (!isFinite(totalSeconds) || totalSeconds < 0) return '00:00:00';
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    const s = Math.floor(totalSeconds % 60);
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

function parseHms(value: string): number | null {
    const trimmed = value.trim();
    if (!trimmed) return null;
    const parts = trimmed.split(':').map(p => p.trim());
    if (parts.some(p => p === '' || !/^\d+(\.\d+)?$/.test(p))) return null;
    const nums = parts.map(Number);
    if (nums.length === 1) return nums[0];
    if (nums.length === 2) return nums[0] * 60 + nums[1];
    if (nums.length === 3) return nums[0] * 3600 + nums[1] * 60 + nums[2];
    return null;
}

export default function MarkerEditorModal({ isOpen, mediaItemId, mediaItemTitle, durationSeconds, serverId, onClose, onSaved }: MarkerEditorModalProps) {
    const dialog = useDialog();
    const [markers, setMarkers] = useState<MediaMarker[]>([]);
    const [locked, setLocked] = useState(false);
    const [loading, setLoading] = useState(false);
    const [saving, setSaving] = useState(false);
    const [dirty, setDirty] = useState(false);

    useEffect(() => {
        if (!isOpen) return;
        let cancelled = false;
        (async () => {
            setLoading(true);
            try {
                const [m, l] = await Promise.all([
                    mediaService.getMarkers(mediaItemId, serverId),
                    mediaService.getMarkersLocked(mediaItemId, serverId)
                ]);
                if (cancelled) return;
                setMarkers(m);
                setLocked(l);
                setDirty(false);
            } catch (err) {
                console.error('Failed to load markers', err);
                if (!cancelled) await dialog.alert('Could not load markers.');
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [isOpen, mediaItemId, serverId, dialog]);

    if (!isOpen) return null;

    const updateMarker = (idx: number, patch: Partial<MediaMarker>) => {
        setMarkers(prev => prev.map((m, i) => i === idx ? { ...m, ...patch } : m));
        setDirty(true);
    };

    const removeMarker = (idx: number) => {
        setMarkers(prev => prev.filter((_, i) => i !== idx));
        setDirty(true);
    };

    const addMarker = (type: MediaMarkerType) => {
        const existingScenes = markers.filter(m => m.type === 'CreditsScene').length;
        setMarkers(prev => [...prev, {
            type,
            startSeconds: 0,
            endSeconds: Math.min(durationSeconds ?? 60, 60),
            order: type === 'CreditsScene' ? existingScenes + 1 : 0
        }]);
        setDirty(true);
    };

    const handleSave = async () => {
        const invalid = markers.find(m => m.endSeconds <= m.startSeconds || m.startSeconds < 0);
        if (invalid) {
            await dialog.alert(`Marker "${invalid.type}" has invalid timing (end must be after start, start must be ≥ 0).`);
            return;
        }
        setSaving(true);
        try {
            await mediaService.replaceMarkers(mediaItemId, markers, serverId);
            setDirty(false);
            onSaved?.();
        } catch (err) {
            console.error('Failed to save markers', err);
            await dialog.alert('Could not save markers.');
        } finally {
            setSaving(false);
        }
    };

    const handleLockToggle = async () => {
        const next = !locked;
        setSaving(true);
        try {
            await mediaService.setMarkersLocked(mediaItemId, next, serverId);
            setLocked(next);
        } catch (err) {
            console.error('Failed to toggle lock', err);
            await dialog.alert('Could not toggle marker lock.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <div
            className="fixed inset-0 z-[200] flex items-center justify-center p-4"
            style={{ background: 'rgba(0, 0, 0, 0.7)', backdropFilter: 'blur(6px)' }}
            onClick={onClose}
        >
            <div
                className="vora-card w-full max-w-3xl max-h-[90vh] overflow-hidden flex flex-col"
                onClick={e => e.stopPropagation()}
            >
                <div className="flex items-start justify-between p-6" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                    <div>
                        <h2 className="m-0 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Edit markers</h2>
                        <p className="m-0 mt-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{mediaItemTitle}{durationSeconds ? ` • ${formatHms(durationSeconds)} runtime` : ''}</p>
                    </div>
                    <button type="button" onClick={onClose} className="vora-button-secondary text-xs cursor-pointer">Close</button>
                </div>

                <div className="flex-1 overflow-y-auto p-6 space-y-4">
                    <div className="flex items-center justify-between rounded-md p-3" style={{ background: locked ? 'var(--vora-warning-soft, var(--vora-bg-sunken))' : 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                        <div>
                            <div className="text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Lock markers</div>
                            <div className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>When locked, automatic re-analysis skips this item and preserves your edits.</div>
                        </div>
                        <button
                            type="button"
                            onClick={handleLockToggle}
                            disabled={saving}
                            className="vora-button-secondary text-xs cursor-pointer disabled:opacity-50"
                        >
                            {locked ? 'Unlock' : 'Lock'}
                        </button>
                    </div>

                    {loading ? (
                        <div className="vora-skeleton h-32" />
                    ) : markers.length === 0 ? (
                        <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>No markers yet. Use the buttons below to add one.</p>
                    ) : (
                        <div className="space-y-3">
                            {markers.map((m, idx) => (
                                <MarkerRow
                                    key={idx}
                                    marker={m}
                                    duration={durationSeconds}
                                    onChange={patch => updateMarker(idx, patch)}
                                    onDelete={() => removeMarker(idx)}
                                />
                            ))}
                        </div>
                    )}

                    <div className="flex flex-wrap gap-2 pt-2">
                        {MARKER_TYPES.map(t => (
                            <button
                                key={t}
                                type="button"
                                onClick={() => addMarker(t)}
                                className="vora-button-secondary text-xs cursor-pointer"
                            >
                                + Add {t}
                            </button>
                        ))}
                    </div>
                </div>

                <div className="flex items-center justify-end gap-2 p-4" style={{ borderTop: '1px solid var(--vora-border-subtle)' }}>
                    <button type="button" onClick={onClose} className="vora-button-secondary cursor-pointer">Cancel</button>
                    <button
                        type="button"
                        onClick={handleSave}
                        disabled={saving || !dirty}
                        className="vora-button-primary cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        {saving ? 'Saving…' : 'Save markers'}
                    </button>
                </div>
            </div>
        </div>
    );
}

function MarkerRow({ marker, duration, onChange, onDelete }: { marker: MediaMarker, duration?: number, onChange: (patch: Partial<MediaMarker>) => void, onDelete: () => void }) {
    const [startInput, setStartInput] = useState(formatHms(marker.startSeconds));
    const [endInput, setEndInput] = useState(formatHms(marker.endSeconds));
    const [lastStartSeconds, setLastStartSeconds] = useState(marker.startSeconds);
    const [lastEndSeconds, setLastEndSeconds] = useState(marker.endSeconds);

    if (lastStartSeconds !== marker.startSeconds) {
        setLastStartSeconds(marker.startSeconds);
        setStartInput(formatHms(marker.startSeconds));
    }
    if (lastEndSeconds !== marker.endSeconds) {
        setLastEndSeconds(marker.endSeconds);
        setEndInput(formatHms(marker.endSeconds));
    }

    const commitStart = () => {
        const parsed = parseHms(startInput);
        if (parsed != null) onChange({ startSeconds: parsed });
        else setStartInput(formatHms(marker.startSeconds));
    };
    const commitEnd = () => {
        const parsed = parseHms(endInput);
        if (parsed != null) onChange({ endSeconds: parsed });
        else setEndInput(formatHms(marker.endSeconds));
    };

    const pctStart = duration && duration > 0 ? Math.min(100, (marker.startSeconds / duration) * 100) : 0;
    const pctEnd = duration && duration > 0 ? Math.min(100, (marker.endSeconds / duration) * 100) : 0;

    return (
        <div className="rounded-md p-3 space-y-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
            <div className="flex flex-wrap items-end gap-3">
                <div>
                    <label className="block text-[10px] font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Type</label>
                    <select
                        value={marker.type}
                        onChange={e => onChange({ type: e.target.value as MediaMarkerType })}
                        className="vora-input cursor-pointer mt-1"
                    >
                        {MARKER_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                    </select>
                </div>
                <div>
                    <label className="block text-[10px] font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Start (HH:MM:SS)</label>
                    <input
                        type="text"
                        value={startInput}
                        onChange={e => setStartInput(e.target.value)}
                        onBlur={commitStart}
                        className="vora-input w-32 mt-1 tabular-nums"
                    />
                </div>
                <div>
                    <label className="block text-[10px] font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>End (HH:MM:SS)</label>
                    <input
                        type="text"
                        value={endInput}
                        onChange={e => setEndInput(e.target.value)}
                        onBlur={commitEnd}
                        className="vora-input w-32 mt-1 tabular-nums"
                    />
                </div>
                {marker.type === 'CreditsScene' && (
                    <div>
                        <label className="block text-[10px] font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Order</label>
                        <input
                            type="number"
                            min={1}
                            value={marker.order}
                            onChange={e => onChange({ order: parseInt(e.target.value, 10) || 1 })}
                            className="vora-input w-20 mt-1 tabular-nums"
                        />
                    </div>
                )}
                <button type="button" onClick={onDelete} className="vora-button-secondary text-xs cursor-pointer ml-auto" style={{ color: 'var(--vora-danger-text)' }}>
                    Delete
                </button>
            </div>
            {duration && duration > 0 && (
                <div className="relative h-1.5 rounded-full" style={{ background: 'rgba(255, 255, 255, 0.08)' }}>
                    <div
                        className="absolute top-0 h-full rounded-full"
                        style={{ left: `${pctStart}%`, width: `${Math.max(0.4, pctEnd - pctStart)}%`, background: 'var(--vora-accent-500)' }}
                    />
                </div>
            )}
        </div>
    );
}
