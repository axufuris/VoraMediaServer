import { useState, useEffect, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
import type { CollectionDetails, CollectionDetailsLibraryItem } from '../../api/Collections/collectionService';
import { useDialog } from '../../dialogs';
import { Modal, ModalHeader, ModalBody, ModalFooter } from '../Common/Modal';

interface ReorderCollectionModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSaved: () => void;
    collection: CollectionDetails;
}

export default function ReorderCollectionModal({
    isOpen, onClose, onSaved, collection }: ReorderCollectionModalProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [items, setItems] = useState<CollectionDetailsLibraryItem[]>([]);
    const [isSaving, setIsSaving] = useState(false);
    const [chrono, setChrono] = useState<Record<string, { year: string, locked: boolean }>>({});
    const [savingItemId, setSavingItemId] = useState<string | null>(null);

    // A chronology-driven collection derives its order from each item's
    // in-universe year, so a drag-and-drop order is overwritten on the next
    // sync. Editing + locking the in-universe year is what survives a re-sync.
    const isChronology = !!collection.sortProviderId;

    const dragItem = useRef<number | null>(null);
    const dragOverItem = useRef<number | null>(null);

    useEffect(() => {
        if (isOpen) {
            setItems([...collection.items]);
            const map: Record<string, { year: string, locked: boolean }> = {};
            for (const item of collection.items) {
                map[item.id] = {
                    year: item.inUniverseYear != null ? String(item.inUniverseYear) : '',
                    locked: !!item.inUniverseYearLocked,
                };
            }
            setChrono(map);
        }
    }, [isOpen, collection]);

    const handleSort = () => {
        if (dragItem.current === null || dragOverItem.current === null) return;

        const _items = [...items];
        const draggedItemContent = _items.splice(dragItem.current, 1)[0];
        _items.splice(dragOverItem.current, 0, draggedItemContent);

        dragItem.current = null;
        dragOverItem.current = null;
        setItems(_items);
    };

    const persistChrono = async (itemId: string, next: { year: string, locked: boolean }) => {
        setSavingItemId(itemId);
        try {
            const parsed = next.year.trim() === '' ? null : Number(next.year);
            const year = parsed != null && !Number.isNaN(parsed) ? parsed : null;
            await collectionAdminService.setItemChronology(collection.id, itemId, year, next.locked, serverId);
            onSaved();
        } catch (error) {
            await dialog.alert('Failed to update the in-universe year.');
            console.error(error);
        } finally {
            setSavingItemId(null);
        }
    };

    const setYear = (itemId: string, year: string) => {
        setChrono(prev => ({ ...prev, [itemId]: { ...prev[itemId], year } }));
    };

    const commitYear = (itemId: string) => {
        const current = chrono[itemId];
        if (!current) return;
        void persistChrono(itemId, current);
    };

    const toggleLock = (itemId: string) => {
        const current = chrono[itemId] ?? { year: '', locked: false };
        const next = { ...current, locked: !current.locked };
        setChrono(prev => ({ ...prev, [itemId]: next }));
        void persistChrono(itemId, next);
    };

    const handleSave = async () => {
        setIsSaving(true);
        try {
            const orderedIds = items.map(i => i.id);
            await collectionAdminService.reorderItems(collection.id, orderedIds, serverId);
            onSaved();
            onClose();
        } catch (error) {
            await dialog.alert('Failed to save new order.');
            console.error(error);
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="4xl" cardClassName="flex flex-col max-h-[85vh]">
            <ModalHeader
                title="Manual Sort Order"
                subtitle={isChronology
                    ? "Set and lock an item's in-universe year — a locked year is never changed by the AI on a re-sync."
                    : "Drag and drop items to set their custom chronological timeline."}
                onClose={onClose}
            />

            <ModalBody className="space-y-2">
                {items.map((item, index) => (
                    <div
                        key={item.id}
                        draggable
                        onDragStart={() => (dragItem.current = index)}
                        onDragEnter={() => (dragOverItem.current = index)}
                        onDragEnd={handleSort}
                        onDragOver={(e) => e.preventDefault()}
                        className="flex items-center gap-4 p-3 bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-lg cursor-grab active:cursor-grabbing hover:border-[var(--vora-accent-500)] transition-colors"
                    >
                        <div className="text-[var(--vora-text-muted)] font-bold w-6 text-center">{index + 1}</div>

                        <div className="w-10 h-14 bg-[var(--vora-bg-sunken)] rounded shrink-0 overflow-hidden">
                            {item.posterUrl && <img src={item.posterUrl} alt="" className="w-full h-full object-cover" />}
                        </div>

                        <div className="flex-1 min-w-0">
                            <h4 className="font-bold text-[var(--vora-text-secondary)] text-sm truncate">{item.tvShowTitle ? `${item.tvShowTitle}: ${item.title}` : item.title}</h4>
                            <p className="text-xs text-[var(--vora-text-muted)]">{item.releaseDate ? new Date(item.releaseDate).getFullYear() : 'Unknown'}</p>
                        </div>

                        {isChronology && (
                            <div
                                className="flex items-center gap-2"
                                draggable={false}
                                onDragStart={(e) => { e.preventDefault(); e.stopPropagation(); }}
                                onMouseDown={(e) => e.stopPropagation()}
                            >
                                <label className="text-[10px] uppercase tracking-widest text-[var(--vora-text-muted)]">Set year</label>
                                <input
                                    type="number"
                                    step="0.1"
                                    value={chrono[item.id]?.year ?? ''}
                                    placeholder="—"
                                    onChange={(e) => setYear(item.id, e.target.value)}
                                    onBlur={() => commitYear(item.id)}
                                    onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
                                    className="w-28 px-3 py-1.5 text-sm rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none"
                                    title="In-universe (story) year used to order this item"
                                />
                                <button
                                    type="button"
                                    onClick={() => toggleLock(item.id)}
                                    disabled={savingItemId === item.id}
                                    title={chrono[item.id]?.locked ? 'Locked — the AI will not change this year' : 'Unlocked — the AI may re-score this year'}
                                    className={`px-2 py-1 rounded text-sm transition-colors ${chrono[item.id]?.locked
                                        ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]'
                                        : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]'}`}
                                >
                                    {chrono[item.id]?.locked ? '🔒' : '🔓'}
                                </button>
                            </div>
                        )}

                        <div className="text-[var(--vora-text-muted)]">
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                        </div>
                    </div>
                ))}
            </ModalBody>

            <ModalFooter className="flex justify-between items-center">
                <span className="text-xs text-[var(--vora-accent-500)] font-medium bg-[var(--vora-accent-500)]/10 px-3 py-1 rounded">
                    {isChronology
                        ? "Locked years survive AI re-syncs. Drag order is a fallback and is replaced on the next timeline sync."
                        : "Note: Collection Default Sort must be set to 'Chronological' for this to apply."}
                </span>
                <div className="flex gap-3">
                    <button onClick={onClose} className="px-5 py-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] font-medium transition-colors cursor-pointer">Close</button>
                    <button onClick={handleSave} disabled={isSaving} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors cursor-pointer">
                        {isSaving ? 'Saving...' : 'Save Order'}
                    </button>
                </div>
            </ModalFooter>
        </Modal>
    );
}
