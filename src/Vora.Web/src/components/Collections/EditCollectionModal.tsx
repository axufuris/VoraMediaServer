import React, { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
import type { CollectionDetails } from '../../api/Collections/collectionService';
import type { ArtworkResult } from '../../api/Media/artworkService';
import { Modal } from '../Common/Modal';
import ArtworkPicker from '../Common/ArtworkPicker';
import { pluginAdminService, type PluginOptionVM } from '../../api/System/pluginAdminService';
import { apiClient } from '../../api/client';
import { useDialog } from '../../dialogs';

interface EditCollectionModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSaved: () => void;
    onDeleted?: () => void;
    collection: CollectionDetails;
}

const formatDateForInput = (dateString?: string) => {
    if (!dateString) return '';
    return new Date(dateString).toISOString().split('T')[0];
};

export default function EditCollectionModal({
    isOpen, onClose, onSaved, onDeleted, collection }: EditCollectionModalProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [activeTab, setActiveTab] = useState<'general' | 'poster' | 'backdrop'>('general');
    const [loadingArt, setLoadingArt] = useState(false);
    const [artwork, setArtwork] = useState<ArtworkResult[]>([]);

    const [title, setTitle] = useState(collection.title);
    const [description, setDescription] = useState(collection.description || '');
    const [posterUrl, setPosterUrl] = useState(collection.posterUrl || '');
    const [backdropUrl, setBackdropUrl] = useState(collection.backdropUrl || '');
    const [defaultSort, setDefaultSort] = useState(collection.defaultSort);
    const [makeGlobal, setMakeGlobal] = useState(false);
    const [autoSyncChronology, setAutoSyncChronology] = useState(collection.autoSyncChronology || false);

    const [sortTitle, setSortTitle] = useState(collection.sortTitle || '');
    const [visibleStartDate, setVisibleStartDate] = useState(formatDateForInput(collection.visibleStartDate));
    const [visibleEndDate, setVisibleEndDate] = useState(formatDateForInput(collection.visibleEndDate));

    const [sortProviderId, setSortProviderId] = useState(collection.sortProviderId || '');
    const [externalListId, setExternalListId] = useState(collection.externalListId || '');
    const [chronologyProviders, setChronologyProviders] = useState<PluginOptionVM[]>([]);
    const [syncProviders, setSyncProviders] = useState<PluginOptionVM[]>([]);
    const [contentSyncProviderId, setContentSyncProviderId] = useState(collection.contentSyncProviderId || '');
    const [contentSyncExternalId, setContentSyncExternalId] = useState(collection.contentSyncExternalId || '');
    const [artworkProviders, setArtworkProviders] = useState<PluginOptionVM[]>([]);
    const [selectedProviderId, setSelectedProviderId] = useState('tmdb_artwork');

    const [lockedFields, setLockedFields] = useState<string[]>(collection.lockedFields || []);
    const [isSaving, setIsSaving] = useState(false);

    const fetchArtworkOptions = () => {
        setLoadingArt(true);
        collectionAdminService.getArtworkOptions(collection.id, serverId)
            .then(setArtwork)
            .catch(console.error)
            .finally(() => setLoadingArt(false));
    };

    useEffect(() => {
        if (isOpen) {
            setTitle(collection.title);
            setDescription(collection.description || '');
            setPosterUrl(collection.posterUrl || '');
            setBackdropUrl(collection.backdropUrl || '');
            setDefaultSort(collection.defaultSort);
            setSortProviderId(collection.sortProviderId || '');
            setExternalListId(collection.externalListId || '');
            setMakeGlobal(false);
            setAutoSyncChronology(collection.autoSyncChronology || false);
            setSortTitle(collection.sortTitle || '');
            setVisibleStartDate(formatDateForInput(collection.visibleStartDate));
            setVisibleEndDate(formatDateForInput(collection.visibleEndDate));
            setContentSyncProviderId(collection.contentSyncProviderId || '');
            setContentSyncExternalId(collection.contentSyncExternalId || '');
            setLockedFields(collection.lockedFields || []);
            setActiveTab('general');

            pluginAdminService.getChronologyProviders(serverId).then(setChronologyProviders).catch(console.error);
            pluginAdminService.getCollectionSyncProviders(serverId).then(setSyncProviders).catch(console.error);
            pluginAdminService.getArtworkProviders(serverId).then(providers => {
                setArtworkProviders(providers);
                if (providers.length > 0) setSelectedProviderId(providers[0].id);
            }).catch(console.error);
        }
    }, [isOpen, collection, serverId]);

    useEffect(() => {
        if (isOpen && (activeTab === 'poster' || activeTab === 'backdrop') && artwork.length === 0) {
            fetchArtworkOptions();
        }
    }, [isOpen, activeTab, collection.id, artwork.length]);


    const toggleLock = (field: string) => {
        setLockedFields(prev => prev.includes(field) ? prev.filter(f => f !== field) : [...prev, field]);
    };

    const handleSelectArt = (url: string, artType: 'PosterUrl' | 'BackdropUrl') => {
        if (artType === 'PosterUrl') setPosterUrl(url);
        else setBackdropUrl(url);

        if (!lockedFields.includes(artType)) toggleLock(artType);
    };

    const uploadArtwork = async (artType: 'Poster' | 'Backdrop', file: File) => {
        const data = new FormData();
        data.append('file', file);
        try {
            await apiClient.post(`/collections/${collection.id}/artwork/upload?type=${artType}`, data, { headers: { 'Content-Type': 'multipart/form-data' }, serverId });
            fetchArtworkOptions();
        } catch (err) {
            await dialog.alert("Upload failed");
            console.error(err);
        }
    };

    const addArtworkUrl = async (artType: 'Poster' | 'Backdrop', url: string) => {
        try {
            await apiClient.post(`/collections/${collection.id}/artwork/url?type=${artType}`, `"${url}"`, { headers: { 'Content-Type': 'application/json' }, serverId });
            fetchArtworkOptions();
        } catch (err) {
            await dialog.alert("Failed to add URL");
            console.error(err);
        }
    };

    const deleteArtwork = async (e: React.MouseEvent, artworkId: string) => {
        e.stopPropagation();
        if (!await dialog.confirm("Delete this custom artwork?")) return;
        try {
            await collectionAdminService.deleteArtwork(artworkId, serverId);
            fetchArtworkOptions();
        } catch (err) {
            await dialog.alert("Failed to delete");
            console.error(err);
        }
    };

    const handleFetchProvider = async () => {
        if (!selectedProviderId) return;
        setLoadingArt(true);
        try {
            await collectionAdminService.fetchProviderArtwork(collection.id, selectedProviderId, serverId);
            fetchArtworkOptions();
        } catch (err) { await dialog.alert("Failed to fetch artwork"); setLoadingArt(false); }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setIsSaving(true);
        try {
            await collectionAdminService.updateCollection(collection.id, {
                title, description: description.trim() || undefined,
                posterUrl: posterUrl.trim() || undefined, backdropUrl: backdropUrl.trim() || undefined,
                defaultSort, makeGlobal, lockedFields,
                sortProviderId: Number(defaultSort) === 4 ? sortProviderId : undefined,
                externalListId: Number(defaultSort) === 4 ? externalListId : undefined,
                autoSyncChronology: Number(defaultSort) === 4 ? autoSyncChronology : false,
                sortTitle: sortTitle.trim() || undefined,
                visibleStartDate: visibleStartDate || undefined, visibleEndDate: visibleEndDate || undefined,
                contentSyncProviderId, contentSyncExternalId
            }, serverId);
            onSaved();
            onClose();
        } catch (error) { await dialog.alert('Failed to update collection.'); }
        finally { setIsSaving(false); }
    };

    const handleDelete = async () => {
        if (collection.systemGenerated) { await dialog.alert("Cannot delete system collection."); return; }
        if (!await dialog.confirm(`Delete the collection "${collection.title}"?`)) return;
        try {
            await collectionAdminService.deleteCollection(collection.id, serverId);
            if (onDeleted) onDeleted();
        } catch { await dialog.alert("Failed to delete."); }
    };

    const LockIcon = ({ field }: { field: string }) => {
        const isLocked = lockedFields.includes(field);
        return (
            <button type="button" onClick={() => toggleLock(field)} className={`ml-2 focus:outline-none transition-colors cursor-pointer ${isLocked ? 'text-orange-500 hover:text-orange-400' : 'text-gray-600 hover:text-gray-400'}`}>
                {isLocked ? <svg className="w-4 h-4 inline-block" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clipRule="evenodd" /></svg> : <svg className="w-4 h-4 inline-block" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 2a5 5 0 00-5 5v2a2 2 0 00-2 2v5a2 2 0 002 2h10a2 2 0 002-2v-5a2 2 0 00-2-2H7V7a3 3 0 015.905-.75 1 1 0 001.937-.5A5.002 5.002 0 0010 2z" clipRule="evenodd" /></svg>}
            </button>
        );
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="4xl"
            cardClassName="flex flex-col max-h-[90vh]"
        >

                <div className="px-6 pt-6 border-b border-gray-800">
                    <div className="flex justify-between items-center mb-6">
                        <h2 className="text-xl font-bold text-white">Edit Collection</h2>
                        <button onClick={onClose} className="text-gray-500 hover:text-white text-2xl font-bold cursor-pointer">&times;</button>
                    </div>
                    <div className="flex gap-6 text-sm font-bold text-gray-400">
                        <button onClick={() => setActiveTab('general')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'general' ? 'border-orange-500 text-orange-400' : 'border-transparent hover:text-white'}`}>General</button>
                        <button onClick={() => setActiveTab('poster')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'poster' ? 'border-orange-500 text-orange-400' : 'border-transparent hover:text-white'}`}>Posters</button>
                        <button onClick={() => setActiveTab('backdrop')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'backdrop' ? 'border-orange-500 text-orange-400' : 'border-transparent hover:text-white'}`}>Backdrops</button>
                    </div>
                </div>

                <div className="p-6 overflow-y-auto custom-scrollbar flex-1">
                    {activeTab === 'general' && (
                        <form id="edit-collection-form" onSubmit={handleSubmit} className="space-y-5">
                            <div>
                                <label className="flex items-center text-sm font-medium text-gray-400 mb-1">Title {!collection.systemGenerated && <LockIcon field="Title" />}</label>
                                <input required type="text" value={title} onChange={e => setTitle(e.target.value)} disabled={collection.systemGenerated} className={`w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none ${collection.systemGenerated ? 'opacity-50' : ''}`} />
                            </div>
                            <div>
                                <label className="flex items-center text-sm font-medium text-gray-400 mb-1">Description <LockIcon field="Description" /></label>
                                <textarea rows={3} value={description} onChange={e => setDescription(e.target.value)} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none" />
                            </div>
                            <div>
                                <label className="flex items-center text-sm font-medium text-gray-400 mb-1">Sort Title <LockIcon field="SortTitle" /></label>
                                <input type="text" value={sortTitle} onChange={e => setSortTitle(e.target.value)} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none" />
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="flex items-center text-sm font-medium text-gray-400 mb-1">Visible Start Date <LockIcon field="VisibleStartDate" /></label>
                                    <input type="date" value={visibleStartDate} onChange={e => setVisibleStartDate(e.target.value)} className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none color-scheme-dark" />
                                </div>
                                <div>
                                    <label className="flex items-center text-sm font-medium text-gray-400 mb-1">Visible End Date <LockIcon field="VisibleEndDate" /></label>
                                    <input type="date" value={visibleEndDate} onChange={e => setVisibleEndDate(e.target.value)} className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none color-scheme-dark" />
                                </div>
                            </div>

                            {!collection.systemGenerated && (
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
                            )}

                            <div className="pt-4 border-t border-gray-800">
                                <label className="flex items-center text-sm font-medium text-gray-400 mb-1">Default Sort Order <LockIcon field="DefaultSort" /></label>
                                <select value={defaultSort} onChange={e => setDefaultSort(Number(e.target.value))} className="w-full bg-gray-900 border border-gray-700 rounded-md p-2 text-white focus:border-orange-500 outline-none">
                                    <option value={0}>Release Date (Oldest First)</option>
                                    <option value={1}>Release Date (Newest First)</option>
                                    <option value={2}>Date Added</option>
                                    <option value={3}>Alphabetical</option>
                                    {(chronologyProviders.length > 0 || defaultSort === 4) && <option value={4}>Chronological</option>}
                                </select>
                            </div>
                            {Number(defaultSort) === 4 && (
                                <div className="p-4 bg-gray-900/50 border border-orange-500/30 rounded-lg space-y-4 mt-4">
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
                                            id="autoSyncChronologyEdit"
                                            checked={autoSyncChronology}
                                            onChange={e => setAutoSyncChronology(e.target.checked)}
                                            className="w-4 h-4 accent-orange-500 rounded bg-gray-900 border-gray-700 cursor-pointer"
                                        />
                                        <label htmlFor="autoSyncChronologyEdit" className="text-sm text-gray-300 font-medium cursor-pointer">
                                            Enable Auto-Sync
                                        </label>
                                    </div>
                                    <p className="text-xs text-gray-500 leading-relaxed mt-2">
                                        If enabled, Vora will run a background task periodically to check the provider for updates and automatically sort new items into this collection's timeline.
                                    </p>
                                </div>
                            )}
                            {collection.libraryId && !collection.systemGenerated && (
                                <div className="flex items-center gap-2 pt-4 border-t border-gray-800">
                                    <input type="checkbox" checked={makeGlobal} onChange={e => setMakeGlobal(e.target.checked)} className="w-4 h-4 accent-orange-500" />
                                    <label className="text-sm text-gray-300">Make this a Global Collection</label>
                                </div>
                            )}
                        </form>
                    )}

                    {(activeTab === 'poster' || activeTab === 'backdrop') && (() => {
                        const isPoster = activeTab === 'poster';
                        const lockField = isPoster ? 'PosterUrl' : 'BackdropUrl';
                        return (
                            <ArtworkPicker
                                artType={isPoster ? 'Poster' : 'Backdrop'}
                                artwork={artwork}
                                loading={loadingArt}
                                selectedUrl={isPoster ? posterUrl : backdropUrl}
                                onSelect={(url) => handleSelectArt(url, lockField)}
                                onUpload={(file) => uploadArtwork(isPoster ? 'Poster' : 'Backdrop', file)}
                                onAddUrl={(url) => addArtworkUrl(isPoster ? 'Poster' : 'Backdrop', url)}
                                onDeleteArtwork={deleteArtwork}
                                actionRowLeft={
                                    <div className="flex items-center gap-2">
                                        <select
                                            value={selectedProviderId}
                                            onChange={e => setSelectedProviderId(e.target.value)}
                                            className="p-1.5 bg-gray-900 border border-gray-700 text-white text-xs rounded outline-none focus:border-orange-500"
                                        >
                                            {artworkProviders.map(p => (
                                                <option key={p.id} value={p.id}>{p.name}</option>
                                            ))}
                                        </select>
                                        <button type="button" onClick={handleFetchProvider} className="px-3 py-1.5 bg-blue-600 hover:bg-blue-500 active:bg-blue-700 text-white text-xs font-bold rounded cursor-pointer transition-colors">
                                            Fetch Artwork
                                        </button>
                                    </div>
                                }
                                actionRowRight={
                                    <div className="flex items-center text-sm text-gray-400">
                                        <span className="mr-2">Selected {activeTab}:</span>
                                        <LockIcon field={lockField} />
                                    </div>
                                }
                            />
                        );
                    })()}
                </div>

                <div className="p-5 border-t border-gray-800 bg-gray-900 rounded-b-xl flex justify-between">
                    <div>
                        {!collection.systemGenerated && <button type="button" onClick={handleDelete} className="px-4 py-2 bg-red-900/30 text-red-500 hover:bg-red-600 hover:text-white font-bold rounded">Delete Collection</button>}
                    </div>
                    <div className="flex gap-3">
                        <button type="button" onClick={onClose} className="px-5 py-2 text-gray-400 hover:text-white">Cancel</button>
                        <button type="submit" form="edit-collection-form" onClick={activeTab !== 'general' ? handleSubmit : undefined} disabled={isSaving} className="px-6 py-2 bg-orange-600 hover:bg-orange-500 disabled:opacity-50 text-white font-bold rounded shadow-lg">{isSaving ? 'Saving...' : 'Save Collection'}</button>
                    </div>
                </div>
        </Modal>
    );
}
