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

    const dragItem = useRef<number | null>(null);
    const dragOverItem = useRef<number | null>(null);

    useEffect(() => {
        if (isOpen) {
            setItems([...collection.items]);
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
        <Modal isOpen={isOpen} onClose={onClose} size="xl" cardClassName="flex flex-col max-h-[85vh]">
            <ModalHeader
                title="Manual Sort Order"
                subtitle="Drag and drop items to set their custom chronological timeline."
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

                        <div className="flex-1">
                            <h4 className="font-bold text-[var(--vora-text-secondary)] text-sm">{item.title}</h4>
                            <p className="text-xs text-[var(--vora-text-muted)]">{item.releaseDate ? new Date(item.releaseDate).getFullYear() : 'Unknown'}</p>
                        </div>

                        <div className="text-[var(--vora-text-muted)]">
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                        </div>
                    </div>
                ))}
            </ModalBody>

            <ModalFooter className="flex justify-between items-center">
                <span className="text-xs text-[var(--vora-accent-500)] font-medium bg-[var(--vora-accent-500)]/10 px-3 py-1 rounded">
                    Note: Collection Default Sort must be set to 'Chronological' for this to apply.
                </span>
                <div className="flex gap-3">
                    <button onClick={onClose} className="px-5 py-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] font-medium transition-colors cursor-pointer">Cancel</button>
                    <button onClick={handleSave} disabled={isSaving} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors cursor-pointer">
                        {isSaving ? 'Saving...' : 'Save Order'}
                    </button>
                </div>
            </ModalFooter>
        </Modal>
    );
}
