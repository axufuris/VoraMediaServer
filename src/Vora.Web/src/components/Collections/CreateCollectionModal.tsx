import React, { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
import type { CollectionSortOrder } from '../../api/Collections/collectionService';
import { pluginAdminService, type PluginOptionVM } from '../../api/System/pluginAdminService';
import { useDialog } from '../../dialogs';
import { Modal, ModalHeader, ModalBody, ModalFooter } from '../Common/Modal';

interface CreateCollectionModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSaved: () => void;
    activeTab: string;
}

export default function CreateCollectionModal({
    isOpen, onClose, onSaved, activeTab }: CreateCollectionModalProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [title, setTitle] = useState('');
    const [description, setDescription] = useState('');
    const [posterUrl, setPosterUrl] = useState('');
    const [backdropUrl, setBackdropUrl] = useState('');
    const [defaultSort, setDefaultSort] = useState<CollectionSortOrder>('ReleaseDateAsc');
    const [isGlobal, setIsGlobal] = useState(activeTab === 'global');
    const [autoSyncChronology, setAutoSyncChronology] = useState(false);

    const [sortTitle, setSortTitle] = useState('');
    const [visibleStartDate, setVisibleStartDate] = useState('');
    const [visibleEndDate, setVisibleEndDate] = useState('');
    const [chronologyProviders, setChronologyProviders] = useState<PluginOptionVM[]>([]);
    const [syncProviders, setSyncProviders] = useState<PluginOptionVM[]>([]);
    const [contentSyncProviderId, setContentSyncProviderId] = useState('');
    const [contentSyncExternalId, setContentSyncExternalId] = useState('');
    const [syncIntervalDays, setSyncIntervalDays] = useState(1);
    const [mirrorList, setMirrorList] = useState(false);

    const [sortProviderId, setSortProviderId] = useState('');
    const [externalListId, setExternalListId] = useState('');

    const [isCreating, setIsCreating] = useState(false);

    useEffect(() => {
        if (isOpen) {
            setTitle('');
            setDescription('');
            setPosterUrl('');
            setBackdropUrl('');
            setDefaultSort('ReleaseDateAsc');
            setSortProviderId('');
            setExternalListId('');
            setIsGlobal(activeTab === 'global');
            setIsCreating(false);
            setAutoSyncChronology(false);

            setSortTitle('');
            setVisibleStartDate('');
            setVisibleEndDate('');

            setContentSyncProviderId('');
            setContentSyncExternalId('');
            setSyncIntervalDays(1);
            setMirrorList(false);

            pluginAdminService.getChronologyProviders(serverId)
                .then(setChronologyProviders)
                .catch(console.error);

            pluginAdminService.getCollectionSyncProviders(serverId)
                .then(setSyncProviders)
                .catch(console.error);
        }
    }, [isOpen, activeTab, serverId]);

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!title.trim()) return;
        setIsCreating(true);

        try {
            const newCollectionId = await collectionAdminService.createCollection({
                title: title.trim(),
                description: description.trim() || undefined,
                posterUrl: posterUrl.trim() || undefined,
                backdropUrl: backdropUrl.trim() || undefined,
                defaultSort,
                sortProviderId: sortProviderId || undefined,
                externalListId: sortProviderId ? (externalListId.trim() || undefined) : undefined,
                autoSyncChronology: sortProviderId ? autoSyncChronology : false,
                sortTitle: sortTitle.trim() || undefined,
                visibleStartDate: visibleStartDate || undefined,
                visibleEndDate: visibleEndDate || undefined,
                libraryId: isGlobal ? undefined : activeTab,
                contentSyncProviderId: contentSyncProviderId || undefined,
                contentSyncExternalId: contentSyncExternalId || undefined,
                syncIntervalDays: Math.max(1, syncIntervalDays),
                mirrorList: contentSyncProviderId ? mirrorList : false
            }, serverId);

            onSaved();
            onClose();

            navigate(serverId ? `/server/${serverId}/collection/${newCollectionId}` : `/collection/${newCollectionId}`);

        } catch (error) {
            await dialog.alert('Failed to create collection.');
            console.error(error);
        } finally {
            setIsCreating(false);
        }
    };

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="2xl" cardClassName="flex flex-col max-h-[90vh]">
            <ModalHeader title="Create New Collection" onClose={onClose} />

            <ModalBody className="p-6">
                <form id="create-collection-form" onSubmit={handleSubmit} className="space-y-5">

                    <div>
                        <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Title</label>
                        <input required autoFocus type="text" value={title} onChange={e => setTitle(e.target.value)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none" />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Description (Optional)</label>
                        <textarea rows={3} value={description} onChange={e => setDescription(e.target.value)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none" />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Sort Title (Optional)</label>
                        <input
                            type="text"
                            value={sortTitle}
                            onChange={e => setSortTitle(e.target.value)}
                            placeholder="e.g., Marvel Cinematic Universe 01"
                            className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none"
                        />
                        <p className="text-xs text-[var(--vora-text-muted)] mt-1">Overrides the normal title when sorting collections alphabetically.</p>
                    </div>

                    <div className="p-4 bg-[var(--vora-bg-raised)]/50 border border-[var(--vora-border-subtle)] rounded-lg space-y-4">
                        <h3 className="text-[var(--vora-text-secondary)] font-bold text-sm tracking-wide uppercase">Seasonal Visibility (Optional)</h3>
                        <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed">
                            Set a start and end date to automatically hide this collection during the off-season. If left blank, it is always visible.
                        </p>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Visible Start Date</label>
                                <input
                                    type="date"
                                    value={visibleStartDate}
                                    onChange={e => setVisibleStartDate(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none color-scheme-dark"
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Visible End Date</label>
                                <input
                                    type="date"
                                    value={visibleEndDate}
                                    onChange={e => setVisibleEndDate(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none color-scheme-dark"
                                />
                            </div>
                        </div>
                    </div>

                    <div className="grid grid-cols-2 gap-4">
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Poster URL</label>
                            <input type="url" value={posterUrl} onChange={e => setPosterUrl(e.target.value)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none" />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Backdrop URL</label>
                            <input type="url" value={backdropUrl} onChange={e => setBackdropUrl(e.target.value)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none" />
                        </div>
                    </div>

                    <div className="pt-4 border-t border-[var(--vora-border-subtle)]">
                        <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-4">Auto-Fill Content</h3>
                        <p className="text-sm text-[var(--vora-text-muted)] mb-4">Automatically add movies and shows to this collection from an external list.</p>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">List Provider</label>
                                <select
                                    value={contentSyncProviderId}
                                    onChange={e => setContentSyncProviderId(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                >
                                    <option value="">Manual Management (None)</option>
                                    {syncProviders.map(provider => (
                                        <option key={provider.id} value={provider.id}>
                                            {provider.name}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            {contentSyncProviderId && (
                                <div>
                                    <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">
                                        {syncProviders.find(p => p.id === contentSyncProviderId)?.externalIdLabel || 'List ID'}
                                    </label>
                                    <input
                                        type="text"
                                        value={contentSyncExternalId}
                                        onChange={e => setContentSyncExternalId(e.target.value)}
                                        placeholder={syncProviders.find(p => p.id === contentSyncProviderId)?.externalIdPlaceholder || 'Enter ID'}
                                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                    />
                                </div>
                            )}
                        </div>

                        {contentSyncProviderId && (
                            <div className="pt-3 mt-3 border-t border-[var(--vora-border-subtle)]/50">
                                <div className="flex items-center gap-2">
                                    <input
                                        type="checkbox"
                                        id="mirrorListCreate"
                                        checked={mirrorList}
                                        onChange={e => setMirrorList(e.target.checked)}
                                        className="w-4 h-4 accent-orange-500 rounded bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)]"
                                    />
                                    <label htmlFor="mirrorListCreate" className="text-sm text-[var(--vora-text-secondary)] font-medium cursor-pointer">
                                        Mirror the list exactly
                                    </label>
                                </div>
                                <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-2">
                                    Remove items from this collection when they're no longer on the external list. Manually added items will also be removed on the next sync.
                                </p>
                            </div>
                        )}

                        {(contentSyncProviderId || sortProviderId) && (
                            <div className="pt-3 mt-3 border-t border-[var(--vora-border-subtle)]/50">
                                <label htmlFor="syncIntervalCreate" className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Recheck every (days)</label>
                                <input
                                    id="syncIntervalCreate"
                                    type="number"
                                    min={1}
                                    value={syncIntervalDays}
                                    onChange={e => setSyncIntervalDays(Math.max(1, Number(e.target.value) || 1))}
                                    className="w-32 bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                />
                                <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-2">
                                    How often the background sync rechecks the list and re-evaluates ordering. Higher values reduce provider/API usage.
                                </p>
                            </div>
                        )}
                    </div>

                    <div className="pt-4 border-t border-[var(--vora-border-subtle)]">
                        <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Default Sort Order</label>
                        <select value={defaultSort} onChange={e => setDefaultSort(e.target.value as CollectionSortOrder)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none">
                            <option value="ReleaseDateAsc">Release Date (Oldest First)</option>
                            <option value="ReleaseDateDesc">Release Date (Newest First)</option>
                            <option value="DateAddedDesc">Date Added</option>
                            <option value="Alphabetical">Alphabetical</option>
                            {(chronologyProviders.length > 0 || sortProviderId) && (
                                <option value="Chronological">Chronological (Timeline Order)</option>
                            )}
                        </select>
                        <p className="text-xs text-[var(--vora-text-muted)] mt-1">The order items are shown in by default. Viewers can switch to other orders.</p>
                    </div>

                    {chronologyProviders.length > 0 && (
                        <div className="p-4 bg-[var(--vora-bg-raised)]/50 border border-orange-500/30 rounded-lg space-y-4">
                            <div>
                                <h3 className="text-[var(--vora-accent-500)] font-bold text-sm tracking-wide uppercase">Chronological Ordering (Optional)</h3>
                                <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-1">
                                    Pick a provider to make Chronological (timeline) order available for this collection. Choose "Chronological" as the Default Sort Order above to use it by default, or leave the default as-is and viewers can switch to it.
                                </p>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Data Provider</label>
                                    <select
                                        value={sortProviderId}
                                        onChange={e => setSortProviderId(e.target.value)}
                                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                    >
                                        <option value="">None (not enabled)</option>
                                        {chronologyProviders.map(provider => (
                                            <option key={provider.id} value={provider.id}>
                                                {provider.name}
                                            </option>
                                        ))}
                                    </select>
                                </div>

                                {sortProviderId && (
                                    <div>
                                        <label className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">
                                            {chronologyProviders.find(p => p.id === sortProviderId)?.externalIdLabel || 'List ID'}
                                        </label>
                                        <input
                                            type="text"
                                            value={externalListId}
                                            onChange={e => setExternalListId(e.target.value)}
                                            placeholder={chronologyProviders.find(p => p.id === sortProviderId)?.externalIdPlaceholder || 'Enter ID'}
                                            className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                        />
                                    </div>
                                )}
                            </div>

                            {sortProviderId && (
                                <div className="pt-2 border-t border-[var(--vora-border-subtle)]/50">
                                    <div className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            id="autoSyncChronologyCreate"
                                            checked={autoSyncChronology}
                                            onChange={e => setAutoSyncChronology(e.target.checked)}
                                            className="w-4 h-4 accent-orange-500 rounded bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)]"
                                        />
                                        <label htmlFor="autoSyncChronologyCreate" className="text-sm text-[var(--vora-text-secondary)] font-medium cursor-pointer">
                                            Enable Auto-Sync
                                        </label>
                                    </div>
                                    <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-2">
                                        If enabled, Vora will run a background task periodically to check the provider for updates and automatically sort new items into this collection's timeline.
                                    </p>
                                </div>
                            )}
                        </div>
                    )}

                    {activeTab !== 'global' && (
                        <div className="flex items-center gap-2 pt-4 border-t border-[var(--vora-border-subtle)]">
                            <input type="checkbox" id="isGlobalCreate" checked={isGlobal} onChange={e => setIsGlobal(e.target.checked)} className="w-4 h-4 accent-orange-500" />
                            <label htmlFor="isGlobalCreate" className="text-sm text-[var(--vora-text-secondary)] font-medium cursor-pointer">Make this a Global Collection (visible across all libraries)</label>
                        </div>
                    )}
                </form>
            </ModalBody>

            <ModalFooter className="flex justify-end gap-3">
                <button type="button" onClick={onClose} className="px-5 py-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] font-medium transition-colors cursor-pointer">Cancel</button>
                <button type="submit" form="create-collection-form" disabled={isCreating} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors cursor-pointer">
                    {isCreating ? 'Creating...' : 'Create Collection'}
                </button>
            </ModalFooter>
        </Modal>
    );
}
