import React, { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { collectionService, type CollectionSummary } from '../../api/Collections/collectionService';
import { collectionAdminService, type CreateCollectionRequest } from '../../api/Collections/collectionAdminService';
import { useDialog } from '../../dialogs';
import { Modal, ModalHeader, ModalBody, ModalFooter } from '../Common/Modal';

interface AddToCollectionModalProps {
    isOpen: boolean;
    onClose: () => void;
    mediaId: string;
    libraryId: string;
    mediaType: string;
    initialCollectionIds: string[];
    onSaved: () => void;
}

export default function AddToCollectionModal({
    isOpen, onClose, mediaId, libraryId, mediaType, initialCollectionIds, onSaved }: AddToCollectionModalProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [collections, setCollections] = useState<CollectionSummary[]>([]);
    const [checkedIds, setCheckedIds] = useState<Set<string>>(new Set());
    const [loading, setLoading] = useState(true);

    const [isCreating, setIsCreating] = useState(false);
    const [newTitle, setNewTitle] = useState('');
    const [isGlobal, setIsGlobal] = useState(mediaType === 'Episode');

    useEffect(() => {
        if (isOpen) {
            const fetchCollections = () => {
                setLoading(true);
                const fetchPromise = mediaType === 'Episode'
                    ? collectionService.getGlobalCollections(serverId)
                    : collectionService.getLibraryCollections(libraryId, serverId);

                fetchPromise
                    .then(data => setCollections(data))
                    .catch(console.error)
                    .finally(() => setLoading(false));
            };

            const timeoutId = window.setTimeout(() => {
                setCheckedIds(new Set(initialCollectionIds));
                setIsCreating(false);
                setNewTitle('');
                fetchCollections();
            }, 0);
            return () => window.clearTimeout(timeoutId);
        }
    }, [isOpen, initialCollectionIds, serverId, mediaType, libraryId]);

    const toggleCollection = async (collectionId: string) => {
        const isCurrentlyChecked = checkedIds.has(collectionId);

        const newChecked = new Set(checkedIds);
        if (isCurrentlyChecked) newChecked.delete(collectionId);
        else newChecked.add(collectionId);
        setCheckedIds(newChecked);

        try {
            if (isCurrentlyChecked) {
                await collectionAdminService.removeFromCollection(collectionId, mediaId, serverId);
            } else {
                await collectionAdminService.addToCollection(collectionId, mediaId, serverId);
            }
            onSaved();
        } catch {
            await dialog.alert('Failed to update collection.');
            setCheckedIds(new Set(initialCollectionIds));
        }
    };

    const handleCreateCollection = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!newTitle.trim()) return;

        try {
            const req: CreateCollectionRequest = {
                title: newTitle,
                defaultSort: 0,
                libraryId: isGlobal ? undefined : libraryId,
                autoSyncChronology: false,
                systemGenerated: false
            };
            const newId = await collectionAdminService.createCollection(req, serverId);

            await collectionAdminService.addToCollection(newId, mediaId, serverId);

            setNewTitle('');
            setIsCreating(false);
            onSaved();
            onClose();
        } catch (error) {
            await dialog.alert('Failed to create collection.');
            console.error(error);
        }
    };

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="md" cardClassName="flex flex-col max-h-[80vh]">
            <ModalHeader title="Add to Collection" onClose={onClose} />

            <ModalBody className="space-y-2">
                {loading ? (
                    <div className="text-center text-[var(--vora-text-muted)] py-4">Loading...</div>
                ) : collections.length === 0 ? (
                    <div className="text-center text-[var(--vora-text-muted)] py-4">No collections available.</div>
                ) : (
                    collections.map(c => (
                        <label key={c.id} className="flex items-center gap-3 p-3 bg-[var(--vora-bg-raised)] rounded-lg border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] cursor-pointer transition-colors">
                            <input
                                type="checkbox"
                                checked={checkedIds.has(c.id)}
                                onChange={() => toggleCollection(c.id)}
                                className="w-5 h-5 accent-orange-500 cursor-pointer"
                            />
                            <div className="flex-1">
                                <h4 className="font-bold text-[var(--vora-text-secondary)]">{c.title}</h4>
                                <p className="text-xs text-[var(--vora-text-muted)]">{c.systemGenerated ? 'System Collection' : 'Custom Collection'}</p>
                            </div>
                        </label>
                    ))
                )}
            </ModalBody>

            <ModalFooter>
                {!isCreating ? (
                    <button onClick={() => setIsCreating(true)} className="w-full py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] font-bold rounded transition-colors border border-[var(--vora-border-subtle)] cursor-pointer">
                        + Create New Collection
                    </button>
                ) : (
                    <form onSubmit={handleCreateCollection} className="space-y-4">
                        <div>
                            <label className="block text-xs text-[var(--vora-text-muted)] mb-1">Collection Name</label>
                            <input type="text" value={newTitle} onChange={e => setNewTitle(e.target.value)} autoFocus className="w-full p-2 bg-[var(--vora-bg-canvas)] rounded border border-[var(--vora-border-subtle)] outline-none focus:border-[var(--vora-accent-500)] text-sm text-[var(--vora-text-primary)]" required />
                        </div>

                        {mediaType !== 'Episode' && (
                            <div className="flex items-center gap-2">
                                <input type="checkbox" id="globalCheck" checked={isGlobal} onChange={e => setIsGlobal(e.target.checked)} className="accent-orange-500 cursor-pointer" />
                                <label htmlFor="globalCheck" className="text-sm text-[var(--vora-text-secondary)] cursor-pointer">Make this a Global Collection</label>
                            </div>
                        )}

                        <div className="flex gap-2">
                            <button type="button" onClick={() => setIsCreating(false)} className="flex-1 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] font-bold rounded transition-colors text-sm cursor-pointer">Cancel</button>
                            <button type="submit" className="flex-1 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] text-[var(--vora-text-primary)] font-bold rounded transition-colors text-sm cursor-pointer">Create</button>
                        </div>
                    </form>
                )}
            </ModalFooter>
        </Modal>
    );
}
