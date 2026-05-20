import React, { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
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
    const [defaultSort, setDefaultSort] = useState(0);
    const [isGlobal, setIsGlobal] = useState(activeTab === 'global');
    const [autoSyncChronology, setAutoSyncChronology] = useState(false);

    const [sortTitle, setSortTitle] = useState('');
    const [visibleStartDate, setVisibleStartDate] = useState('');
    const [visibleEndDate, setVisibleEndDate] = useState('');
    const [chronologyProviders, setChronologyProviders] = useState<PluginOptionVM[]>([]);
    const [syncProviders, setSyncProviders] = useState<PluginOptionVM[]>([]);
    const [contentSyncProviderId, setContentSyncProviderId] = useState('');
    const [contentSyncExternalId, setContentSyncExternalId] = useState('');

    const [sortProviderId, setSortProviderId] = useState('');
    const [externalListId, setExternalListId] = useState('');

    const [isCreating, setIsCreating] = useState(false);

    useEffect(() => {
        if (isOpen) {
            setTitle('');
            setDescription('');
            setPosterUrl('');
            setBackdropUrl('');
            setDefaultSort(0);
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

            pluginAdminService.getChronologyProviders(serverId)
                .then(setChronologyProviders)
                .catch(console.error);

            pluginAdminService.getCollectionSyncProviders(serverId)
                .then(setSyncProviders)
                .catch(console.error);
        }
    }, [isOpen, activeTab, serverId]);

    const handleSubmit = async (e: React.FormEvent) => {
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
                sortProviderId: Number(defaultSort) === 4 ? sortProviderId : undefined,
                externalListId: Number(defaultSort) === 4 ? externalListId : undefined,
                autoSyncChronology: Number(defaultSort) === 4 ? autoSyncChronology : false,
                sortTitle: sortTitle.trim() || undefined,
                visibleStartDate: visibleStartDate || undefined,
                visibleEndDate: visibleEndDate || undefined,
                libraryId: isGlobal ? undefined : activeTab,
                contentSyncProviderId: contentSyncProviderId || undefined,
                contentSyncExternalId: contentSyncExternalId || undefined
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
                        <label className="block text-sm font-medium text-gray-400 mb-1">Title</label>
                        <input required autoFocus type="text" value={title} onChange={e => setTitle(e.target.value)} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none" />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-400 mb-1">Description (Optional)</label>
                        <textarea rows={3} value={description} onChange={e => setDescription(e.target.value)} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none" />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-400 mb-1">Sort Title (Optional)</label>
                        <input
                            type="text"
                            value={sortTitle}
                            onChange={e => setSortTitle(e.target.value)}
                            placeholder="e.g., Marvel Cinematic Universe 01"
                            className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none"
                        />
                        <p className="text-xs text-gray-500 mt-1">Overrides the normal title when sorting collections alphabetically.</p>
                    </div>

                    <div className="p-4 bg-gray-900/50 border border-gray-800 rounded-lg space-y-4">
                        <h3 className="text-gray-300 font-bold text-sm tracking-wide uppercase">Seasonal Visibility (Optional)</h3>
                        <p className="text-xs text-gray-500 leading-relaxed">
                            Set a start and end date to automatically hide this collection during the off-season. If left blank, it is always visible.
                        </p>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-400 mb-1">Visible Start Date</label>
                                <input
                                    type="date"
                                    value={visibleStartDate}
                                    onChange={e => setVisibleStartDate(e.target.value)}
                                    className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none color-scheme-dark"
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-400 mb-1">Visible End Date</label>
                                <input
                                    type="date"
                                    value={visibleEndDate}
                                    onChange={e => setVisibleEndDate(e.target.value)}
                                    className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none color-scheme-dark"
                                />
                            </div>
                        </div>
                    </div>

                    <div className="grid grid-cols-2 gap-4">
                        <div>
                            <label className="block text-sm font-medium text-gray-400 mb-1">Poster URL</label>
                            <input type="url" value={posterUrl} onChange={e => setPosterUrl(e.target.value)} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none" />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-400 mb-1">Backdrop URL</label>
                            <input type="url" value={backdropUrl} onChange={e => setBackdropUrl(e.target.value)} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none" />
                        </div>
                    </div>

                    <div className="pt-4 border-t border-gray-800">
                        <h3 className="text-lg font-bold text-gray-200 mb-4">Auto-Fill Content</h3>
                        <p className="text-sm text-gray-400 mb-4">Automatically add movies and shows to this collection from an external list.</p>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-400 mb-1">List Provider</label>
                                <select
                                    value={contentSyncProviderId}
                                    onChange={e => setContentSyncProviderId(e.target.value)}
                                    className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none"
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
                                    <label className="block text-sm font-medium text-gray-400 mb-1">
                                        {syncProviders.find(p => p.id === contentSyncProviderId)?.externalIdLabel || 'List ID'}
                                    </label>
                                    <input
                                        type="text"
                                        value={contentSyncExternalId}
                                        onChange={e => setContentSyncExternalId(e.target.value)}
                                        placeholder={syncProviders.find(p => p.id === contentSyncProviderId)?.externalIdPlaceholder || 'Enter ID'}
                                        className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none"
                                    />
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="pt-4 border-t border-gray-800">
                        <label className="block text-sm font-medium text-gray-400 mb-1">Default Sort Order</label>
                        <select value={defaultSort} onChange={e => setDefaultSort(Number(e.target.value))} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none">
                            <option value={0}>Release Date (Oldest First)</option>
                            <option value={1}>Release Date (Newest First)</option>
                            <option value={2}>Date Added</option>
                            <option value={3}>Alphabetical</option>
                            {chronologyProviders.length > 0 && (
                                <option value={4}>Chronological (Timeline Order)</option>
                            )}
                        </select>
                    </div>

                    {Number(defaultSort) === 4 && (
                        <div className="p-4 bg-gray-900/50 border border-orange-500/30 rounded-lg space-y-4">
                            <h3 className="text-orange-400 font-bold text-sm tracking-wide uppercase">Chronological Sync Settings</h3>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-400 mb-1">Data Provider</label>
                                    <select
                                        value={sortProviderId}
                                        onChange={e => setSortProviderId(e.target.value)}
                                        className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none"
                                    >
                                        <option value="">Select a Provider...</option>
                                        {chronologyProviders.map(provider => (
                                            <option key={provider.id} value={provider.id}>
                                                {provider.name}
                                            </option>
                                        ))}
                                    </select>
                                </div>

                                {sortProviderId && (
                                    <div>
                                        <label className="block text-sm font-medium text-gray-400 mb-1">
                                            {chronologyProviders.find(p => p.id === sortProviderId)?.externalIdLabel || 'List ID'}
                                        </label>
                                        <input
                                            type="text"
                                            value={externalListId}
                                            onChange={e => setExternalListId(e.target.value)}
                                            placeholder={chronologyProviders.find(p => p.id === sortProviderId)?.externalIdPlaceholder || 'Enter ID'}
                                            className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none"
                                        />
                                    </div>
                                )}
                            </div>
                            <div className="flex items-center gap-2 pt-2 border-t border-gray-800/50 mt-4">
                                <input
                                    type="checkbox"
                                    id="autoSyncChronologyCreate"
                                    checked={autoSyncChronology}
                                    onChange={e => setAutoSyncChronology(e.target.checked)}
                                    className="w-4 h-4 accent-orange-500 rounded bg-gray-900 border-gray-700"
                                />
                                <label htmlFor="autoSyncChronologyCreate" className="text-sm text-gray-300 font-medium cursor-pointer">
                                    Enable Auto-Sync
                                </label>
                            </div>
                            <p className="text-xs text-gray-500 leading-relaxed mt-2">
                                If enabled, Vora will run a background task periodically to check the provider for updates and automatically sort new items into this collection's timeline.
                            </p>
                        </div>
                    )}

                    {activeTab !== 'global' && (
                        <div className="flex items-center gap-2 pt-4 border-t border-gray-800">
                            <input type="checkbox" id="isGlobalCreate" checked={isGlobal} onChange={e => setIsGlobal(e.target.checked)} className="w-4 h-4 accent-orange-500" />
                            <label htmlFor="isGlobalCreate" className="text-sm text-gray-300 font-medium cursor-pointer">Make this a Global Collection (visible across all libraries)</label>
                        </div>
                    )}
                </form>
            </ModalBody>

            <ModalFooter className="flex justify-end gap-3">
                <button type="button" onClick={onClose} className="px-5 py-2 text-gray-400 hover:text-white font-medium transition-colors cursor-pointer">Cancel</button>
                <button type="submit" form="create-collection-form" disabled={isCreating} className="px-6 py-2 bg-orange-600 hover:bg-orange-500 disabled:opacity-50 text-white font-bold rounded shadow-lg transition-colors cursor-pointer">
                    {isCreating ? 'Creating...' : 'Create Collection'}
                </button>
            </ModalFooter>
        </Modal>
    );
}
