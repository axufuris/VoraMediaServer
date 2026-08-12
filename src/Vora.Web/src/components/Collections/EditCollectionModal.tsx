import React, { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
import type { CollectionDetails, CollectionSortOrder } from '../../api/Collections/collectionService';
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
    const [syncIntervalDays, setSyncIntervalDays] = useState(collection.syncIntervalDays || 1);
    const [mirrorList, setMirrorList] = useState(collection.mirrorList || false);
    const [artworkProviders, setArtworkProviders] = useState<PluginOptionVM[]>([]);
    const [selectedProviderId, setSelectedProviderId] = useState('tmdb_artwork');

    const [lockedFields, setLockedFields] = useState<string[]>(collection.lockedFields || []);
    const [isSaving, setIsSaving] = useState(false);

    const fetchArtworkOptions = useCallback(() => {
        setLoadingArt(true);
        collectionAdminService.getArtworkOptions(collection.id, serverId)
            .then(setArtwork)
            .catch(console.error)
            .finally(() => setLoadingArt(false));
    }, [collection.id, serverId]);

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
            setSyncIntervalDays(collection.syncIntervalDays || 1);
            setMirrorList(collection.mirrorList || false);
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
    }, [isOpen, activeTab, artwork.length, fetchArtworkOptions]);


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
        } catch { await dialog.alert("Failed to fetch artwork"); setLoadingArt(false); }
    };

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setIsSaving(true);
        try {
            await collectionAdminService.updateCollection(collection.id, {
                title, description: description.trim() || undefined,
                posterUrl: posterUrl.trim() || undefined, backdropUrl: backdropUrl.trim() || undefined,
                defaultSort, makeGlobal, lockedFields,
                sortProviderId: sortProviderId || undefined,
                externalListId: sortProviderId ? (externalListId.trim() || undefined) : undefined,
                autoSyncChronology: sortProviderId ? autoSyncChronology : false,
                sortTitle: sortTitle.trim() || undefined,
                visibleStartDate: visibleStartDate || undefined, visibleEndDate: visibleEndDate || undefined,
                contentSyncProviderId, contentSyncExternalId,
                syncIntervalDays: Math.max(1, syncIntervalDays),
                mirrorList: contentSyncProviderId ? mirrorList : false
            }, serverId);
            onSaved();
            onClose();
        } catch { await dialog.alert('Failed to update collection.'); }
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
            <button type="button" onClick={() => toggleLock(field)} className={`ml-2 focus:outline-none transition-colors cursor-pointer ${isLocked ? 'text-[var(--vora-accent-500)] hover:text-[var(--vora-accent-500)]' : 'text-[var(--vora-text-muted)] hover:text-[var(--vora-text-muted)]'}`}>
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

                <div className="px-6 pt-6 border-b border-[var(--vora-border-subtle)]">
                    <div className="flex justify-between items-center mb-6">
                        <h2 className="text-xl font-bold text-[var(--vora-text-primary)]">Edit Collection</h2>
                        <button onClick={onClose} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] text-2xl font-bold cursor-pointer">&times;</button>
                    </div>
                    <div className="flex gap-6 text-sm font-bold text-[var(--vora-text-muted)]">
                        <button onClick={() => setActiveTab('general')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'general' ? 'border-orange-500 text-[var(--vora-accent-500)]' : 'border-transparent hover:text-[var(--vora-text-primary)]'}`}>General</button>
                        <button onClick={() => setActiveTab('poster')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'poster' ? 'border-orange-500 text-[var(--vora-accent-500)]' : 'border-transparent hover:text-[var(--vora-text-primary)]'}`}>Posters</button>
                        <button onClick={() => setActiveTab('backdrop')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'backdrop' ? 'border-orange-500 text-[var(--vora-accent-500)]' : 'border-transparent hover:text-[var(--vora-text-primary)]'}`}>Backdrops</button>
                    </div>
                </div>

                <div className="p-6 overflow-y-auto custom-scrollbar flex-1">
                    {activeTab === 'general' && (
                        <form id="edit-collection-form" onSubmit={handleSubmit} className="space-y-5">
                            <div>
                                <label className="flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1">Title {!collection.systemGenerated && <LockIcon field="Title" />}</label>
                                <input required type="text" value={title} onChange={e => setTitle(e.target.value)} disabled={collection.systemGenerated} className={`w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none ${collection.systemGenerated ? 'opacity-50' : ''}`} />
                            </div>
                            <div>
                                <label className="flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1">Description <LockIcon field="Description" /></label>
                                <textarea rows={3} value={description} onChange={e => setDescription(e.target.value)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none" />
                            </div>
                            <div>
                                <label className="flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1">Sort Title <LockIcon field="SortTitle" /></label>
                                <input type="text" value={sortTitle} onChange={e => setSortTitle(e.target.value)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none" />
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1">Visible Start Date <LockIcon field="VisibleStartDate" /></label>
                                    <input type="date" value={visibleStartDate} onChange={e => setVisibleStartDate(e.target.value)} className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none color-scheme-dark" />
                                </div>
                                <div>
                                    <label className="flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1">Visible End Date <LockIcon field="VisibleEndDate" /></label>
                                    <input type="date" value={visibleEndDate} onChange={e => setVisibleEndDate(e.target.value)} className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none color-scheme-dark" />
                                </div>
                            </div>

                            {!collection.systemGenerated && (
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
                                                {syncProviders.find(p => p.id === contentSyncProviderId)?.isAiPlugin ? (
                                                    <textarea
                                                        value={contentSyncExternalId}
                                                        onChange={e => setContentSyncExternalId(e.target.value)}
                                                        placeholder={syncProviders.find(p => p.id === contentSyncProviderId)?.externalIdPlaceholder || 'Describe the list'}
                                                        rows={4}
                                                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none resize-y"
                                                    />
                                                ) : (
                                                <input
                                                    type="text"
                                                    value={contentSyncExternalId}
                                                    onChange={e => setContentSyncExternalId(e.target.value)}
                                                    placeholder={syncProviders.find(p => p.id === contentSyncProviderId)?.externalIdPlaceholder || 'Enter ID'}
                                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                                />
                                                )}
                                            </div>
                                        )}
                                    </div>

                                    {contentSyncProviderId && syncProviders.find(p => p.id === contentSyncProviderId)?.isAiPlugin && (
                                        <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-2">
                                            Name a specific franchise or shared universe — e.g. <span className="text-[var(--vora-text-secondary)]">Marvel Cinematic Universe</span>, <span className="text-[var(--vora-text-secondary)]">DC Extended Universe</span>, or <span className="text-[var(--vora-text-secondary)]">Star Wars</span>. Every movie and season is pulled in automatically, so the universe name alone is enough (no need to add movies and shows, and don&apos;t narrow it to films only). For a genre or mood like all your kung-fu movies, use a Smart Playlist instead.
                                        </p>
                                    )}

                                    {contentSyncProviderId && (
                                        <div className="pt-3 mt-3 border-t border-[var(--vora-border-subtle)]/50">
                                            <div className="flex items-center gap-2">
                                                <input
                                                    type="checkbox"
                                                    id="mirrorListEdit"
                                                    checked={mirrorList}
                                                    onChange={e => setMirrorList(e.target.checked)}
                                                    className="w-4 h-4 accent-orange-500 rounded bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)]"
                                                />
                                                <label htmlFor="mirrorListEdit" className="text-sm text-[var(--vora-text-secondary)] font-medium cursor-pointer">
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
                                            <label htmlFor="syncIntervalEdit" className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Recheck every (days)</label>
                                            <input
                                                id="syncIntervalEdit"
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
                            )}

                            <div className="pt-4 border-t border-[var(--vora-border-subtle)]">
                                <label className="flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1">Default Sort Order <LockIcon field="DefaultSort" /></label>
                                <select value={defaultSort} onChange={e => setDefaultSort(e.target.value as CollectionSortOrder)} className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none">
                                    <option value="ReleaseDateAsc">Release Date (Oldest First)</option>
                                    <option value="ReleaseDateDesc">Release Date (Newest First)</option>
                                    <option value="DateAddedDesc">Date Added</option>
                                    <option value="Alphabetical">Alphabetical</option>
                                    {(chronologyProviders.length > 0 || defaultSort === 'Chronological') && <option value="Chronological">Chronological</option>}
                                </select>
                            </div>
                            {(chronologyProviders.length > 0 || sortProviderId) && (
                                <div className="p-4 bg-[var(--vora-bg-raised)]/50 border border-orange-500/30 rounded-lg space-y-4 mt-4">
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
                                                {chronologyProviders.find(p => p.id === sortProviderId)?.isAiPlugin ? (
                                                    <textarea
                                                        value={externalListId}
                                                        onChange={e => setExternalListId(e.target.value)}
                                                        placeholder={chronologyProviders.find(p => p.id === sortProviderId)?.externalIdPlaceholder || 'Describe the ordering'}
                                                        rows={4}
                                                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none resize-y"
                                                    />
                                                ) : (
                                                <input
                                                    type="text"
                                                    value={externalListId}
                                                    onChange={e => setExternalListId(e.target.value)}
                                                    placeholder={chronologyProviders.find(p => p.id === sortProviderId)?.externalIdPlaceholder || 'Enter ID'}
                                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                                                />
                                                )}
                                            </div>
                                        )}
                                    </div>

                                    {sortProviderId && chronologyProviders.find(p => p.id === sortProviderId)?.isAiPlugin && (
                                        <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed">
                                            Name the universe and how it should be ordered — e.g. <span className="text-[var(--vora-text-secondary)]">Marvel Cinematic Universe in in-universe chronological order</span>. Left blank, the collection title is used.
                                        </p>
                                    )}

                                    {sortProviderId && (
                                        <div className="pt-2 border-t border-[var(--vora-border-subtle)]/50">
                                            <div className="flex items-center gap-2">
                                                <input
                                                    type="checkbox"
                                                    id="autoSyncChronologyEdit"
                                                    checked={autoSyncChronology}
                                                    onChange={e => setAutoSyncChronology(e.target.checked)}
                                                    className="w-4 h-4 accent-orange-500 rounded bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)] cursor-pointer"
                                                />
                                                <label htmlFor="autoSyncChronologyEdit" className="text-sm text-[var(--vora-text-secondary)] font-medium cursor-pointer">
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
                            {collection.libraryId && !collection.systemGenerated && (
                                <div className="flex items-center gap-2 pt-4 border-t border-[var(--vora-border-subtle)]">
                                    <input type="checkbox" checked={makeGlobal} onChange={e => setMakeGlobal(e.target.checked)} className="w-4 h-4 accent-orange-500" />
                                    <label className="text-sm text-[var(--vora-text-secondary)]">Make this a Global Collection</label>
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
                                            className="p-1.5 bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] text-xs rounded outline-none focus:border-[var(--vora-accent-500)]"
                                        >
                                            {artworkProviders.map(p => (
                                                <option key={p.id} value={p.id}>{p.name}</option>
                                            ))}
                                        </select>
                                        <button type="button" onClick={handleFetchProvider} className="px-3 py-1.5 bg-blue-600 hover:bg-blue-500 active:bg-blue-700 text-[var(--vora-text-primary)] text-xs font-bold rounded cursor-pointer transition-colors">
                                            Fetch Artwork
                                        </button>
                                    </div>
                                }
                                actionRowRight={
                                    <div className="flex items-center text-sm text-[var(--vora-text-muted)]">
                                        <span className="mr-2">Selected {activeTab}:</span>
                                        <LockIcon field={lockField} />
                                    </div>
                                }
                            />
                        );
                    })()}
                </div>

                <div className="p-5 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-raised)] rounded-b-xl flex justify-between">
                    <div>
                        {!collection.systemGenerated && <button type="button" onClick={handleDelete} className="px-4 py-2 bg-[var(--vora-danger-soft)]/30 text-[var(--vora-danger-500)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] font-bold rounded">Delete Collection</button>}
                    </div>
                    <div className="flex gap-3">
                        <button type="button" onClick={onClose} className="px-5 py-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]">Cancel</button>
                        <button type="submit" form="edit-collection-form" onClick={activeTab !== 'general' ? handleSubmit : undefined} disabled={isSaving} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] disabled:opacity-50 text-[var(--vora-text-primary)] font-bold rounded shadow-lg">{isSaving ? 'Saving...' : 'Save Collection'}</button>
                    </div>
                </div>
        </Modal>
    );
}
