import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { libraryService, type MediaLibrary } from '../../../api/Media/libraryService';
import { libraryAdminService } from '../../../api/Media/libraryAdminService';
import { pluginAdminService, type PluginOptionVM } from '../../../api/System/pluginAdminService';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import FolderPathInput from '../../../components/Admin/FolderBrowser/FolderPathInput';

function SectionHeading({ children }: { children: React.ReactNode }) {
    return <h2 className="text-base font-semibold text-[var(--vora-text-primary)] pb-2 border-b border-[var(--vora-border-subtle)]">{children}</h2>;
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function Checkbox({ checked, onChange, label }: { checked: boolean, onChange: (v: boolean) => void, label: string }) {
    return (
        <label className="flex items-center gap-3 cursor-pointer select-none">
            <input
                type="checkbox"
                checked={checked}
                onChange={e => onChange(e.target.checked)}
                className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
            />
            <span className="text-sm text-[var(--vora-text-primary)]">{label}</span>
        </label>
    );
}

export default function ManageLibrary() {
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();

    const [library, setLibrary] = useState<MediaLibrary | null>(null);
    const [saving, setSaving] = useState(false);
    const [newPath, setNewPath] = useState('');
    const [providers, setProviders] = useState<PluginOptionVM[]>([]);
    const [ratingProviders, setRatingProviders] = useState<PluginOptionVM[]>([]);
    const [artworkProviders, setArtworkProviders] = useState<PluginOptionVM[]>([]);

    const [alertModal, setAlertModal] = useState({ isOpen: false, title: '', message: '' });
    const [confirmModal, setConfirmModal] = useState<{ isOpen: boolean, title: string, message: string, onConfirm: () => void }>({ isOpen: false, title: '', message: '', onConfirm: () => { } });

    useEffect(() => {
        if (id) {
            Promise.all([
                libraryService.getLibraryById(id, serverId),
                pluginAdminService.getMetadataProviders(serverId),
                pluginAdminService.getRatingsProviders(serverId),
                pluginAdminService.getArtworkProviders(serverId),
            ]).then(([lib, meta, ratings, artwork]) => {
                setLibrary(lib);
                setProviders(meta);
                setRatingProviders(ratings);
                setArtworkProviders(artwork);
            }).catch(console.error);
        }
    }, [id, serverId]);

    if (!library) {
        return (
            <div data-vora-page="">
                <PageHeader title="Manage Library" />
                <div className="p-8 max-w-5xl mx-auto"><div className="vora-skeleton h-64" /></div>
            </div>
        );
    }

    const showAlert = (title: string, message: string) => setAlertModal({ isOpen: true, title, message });
    const showConfirm = (title: string, message: string, onConfirm: () => void) => setConfirmModal({ isOpen: true, title, message, onConfirm });
    const closeAlert = () => setAlertModal({ ...alertModal, isOpen: false });
    const closeConfirm = () => setConfirmModal({ ...confirmModal, isOpen: false });

    const handleChange = <K extends keyof MediaLibrary>(field: K, value: MediaLibrary[K]) => {
        setLibrary({ ...library, [field]: value });
    };

    const handleAddPath = () => {
        if (!newPath.trim()) return;
        if (library.folderPaths.includes(newPath.trim())) {
            showAlert('Duplicate path', 'This path is already in the library.');
            return;
        }
        handleChange('folderPaths', [...library.folderPaths, newPath.trim()]);
        setNewPath('');
    };

    const handleRemovePath = (pathToRemove: string) => {
        handleChange('folderPaths', library.folderPaths.filter(p => p !== pathToRemove));
    };

    const handleSave = async () => {
        setSaving(true);
        try {
            await libraryAdminService.updateLibrary(library.id, library, serverId);
            showAlert('Success', 'Settings saved.');
        } catch (err) {
            showAlert('Error', 'Failed to save settings.');
            console.error(err);
        } finally {
            setSaving(false);
        }
    };

    const handleDelete = () => {
        showConfirm(
            'Delete library',
            'Are you absolutely sure you want to delete this library?\n\nThis will remove all associated media records from Vora (your physical files on disk will NOT be touched).',
            async () => {
                try {
                    await libraryAdminService.deleteLibrary(library.id, serverId);
                    navigate(serverId ? `/server/${serverId}/admin/libraries` : '/admin/libraries');
                } catch (err) {
                    showAlert('Error', 'Failed to delete library. Please check the console.');
                    console.error(err);
                }
            },
        );
    };

    const handleToggleWatch = async () => {
        const newState = !library.isBeingWatched;
        try {
            await libraryAdminService.toggleWatch(library.id, newState, serverId);
            setLibrary({ ...library, isBeingWatched: newState, enableRealTimeWatching: newState });
        } catch (err) {
            showAlert('Error', 'Failed to toggle folder watching.');
            console.error(err);
        }
    };

    const currentTypeStr = library.type.toString();
    const availableMetadataProviders = providers.filter(p => !p.supportedLibraryTypes || p.supportedLibraryTypes.includes(currentTypeStr));
    const availableRatingProviders = ratingProviders.filter(p => !p.supportedLibraryTypes || p.supportedLibraryTypes.includes(currentTypeStr));
    const availableArtworkProviders = artworkProviders.filter(p => !p.supportedLibraryTypes || p.supportedLibraryTypes.includes(currentTypeStr));
    const isTvShow = library.type.toLowerCase() === 'tvshow';
    const showVideoOptions = library.type.toLowerCase() === 'movie' || library.type.toLowerCase() === 'tvshow';
    const backUrl = serverId ? `/server/${serverId}/admin/libraries` : '/admin/libraries';

    return (
        <div data-vora-page="">
            <PageHeader
                title={`Manage: ${library.name}`}
                breadcrumb={
                    <button type="button" onClick={() => navigate(backUrl)} className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer font-medium">
                        ← Back to libraries
                    </button>
                }
                actions={
                    <button type="button" onClick={handleSave} disabled={saving} className="vora-button-primary">
                        {saving ? 'Saving…' : 'Save changes'}
                    </button>
                }
            />

            <div className="px-8 pb-10 max-w-5xl mx-auto pt-6 space-y-6">
                <div className="vora-card p-5 flex flex-wrap gap-2">
                    <button
                        type="button"
                        onClick={async () => {
                            try {
                                await libraryAdminService.triggerScan(library.id, serverId);
                                showAlert('Scan started', 'Scan triggered. Check the server console.');
                            } catch (err) {
                                console.error(err);
                                showAlert('Error', 'Failed to trigger scan.');
                            }
                        }}
                        className="vora-button-secondary text-xs"
                    >
                        Run scan
                    </button>
                    <button
                        type="button"
                        onClick={async () => {
                            try {
                                await libraryAdminService.refreshRatings(library.id, true, serverId);
                                showAlert('Refresh started', 'Ratings refresh triggered.');
                            } catch (err) {
                                console.error(err);
                                showAlert('Error', 'Failed to trigger ratings refresh.');
                            }
                        }}
                        className="vora-button-secondary text-xs"
                    >
                        Refresh ratings
                    </button>
                    <button
                        type="button"
                        onClick={async () => {
                            try {
                                await libraryAdminService.refreshMetadata(library.id, true, serverId);
                                showAlert('Refresh started', 'Metadata refresh triggered.');
                            } catch (err) {
                                console.error(err);
                                showAlert('Error', 'Failed to trigger metadata refresh.');
                            }
                        }}
                        className="vora-button-secondary text-xs"
                    >
                        Refresh metadata
                    </button>
                    <div className="ml-auto">
                        <button
                            type="button"
                            onClick={handleToggleWatch}
                            className={`px-4 py-2 rounded-[var(--vora-radius-md)] text-xs font-semibold transition-colors cursor-pointer ${library.isBeingWatched
                                ? 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-500)] hover:text-white'
                                : 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)] hover:bg-[var(--vora-success-500)] hover:text-white'}`}
                        >
                            {library.isBeingWatched ? 'Stop watching' : 'Start watching'}
                        </button>
                    </div>
                </div>

                <div className="vora-card p-6 space-y-6">
                    <SectionHeading>General</SectionHeading>

                    <div>
                        <FieldLabel>Library Name</FieldLabel>
                        <input
                            type="text"
                            value={library.name}
                            onChange={e => handleChange('name', e.target.value)}
                            className="vora-input"
                        />
                    </div>

                    <div>
                        <FieldLabel>Metadata Provider</FieldLabel>
                        <select
                            value={library.metadataProviderId || 'tmdb_metadata'}
                            onChange={e => handleChange('metadataProviderId', e.target.value)}
                            className="vora-input cursor-pointer"
                        >
                            {availableMetadataProviders.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                        </select>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <FieldLabel>Third Party Rating 1</FieldLabel>
                            <select
                                value={library.thirdPartyRating1ProviderId || ''}
                                onChange={e => handleChange('thirdPartyRating1ProviderId', e.target.value)}
                                className="vora-input cursor-pointer"
                            >
                                <option value="">None</option>
                                {availableRatingProviders.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                            </select>
                        </div>
                        <div>
                            <FieldLabel>Third Party Rating 2</FieldLabel>
                            <select
                                value={library.thirdPartyRating2ProviderId || ''}
                                onChange={e => handleChange('thirdPartyRating2ProviderId', e.target.value)}
                                className="vora-input cursor-pointer"
                            >
                                <option value="">None</option>
                                {availableRatingProviders.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                            </select>
                        </div>
                    </div>

                    <div>
                        <FieldLabel>Artwork Provider</FieldLabel>
                        <select
                            value={library.artworkProviderId || ''}
                            onChange={e => handleChange('artworkProviderId', e.target.value)}
                            className="vora-input cursor-pointer"
                        >
                            {availableArtworkProviders.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                        </select>
                    </div>

                    <SectionHeading>Folder Paths</SectionHeading>
                    <div className="space-y-3">
                        {library.folderPaths.map((path, idx) => (
                            <div key={idx} className="flex justify-between items-center bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                                <span className="text-[var(--vora-text-secondary)] font-mono text-sm break-all">{path}</span>
                                <button
                                    type="button"
                                    onClick={() => handleRemovePath(path)}
                                    className="text-xs font-semibold px-3 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-white transition-colors cursor-pointer shrink-0"
                                >
                                    Remove
                                </button>
                            </div>
                        ))}
                        {library.folderPaths.length === 0 && (
                            <p className="text-sm text-[var(--vora-text-muted)] italic">No folder paths added yet.</p>
                        )}

                        <div className="flex gap-2 mt-3 flex-wrap">
                            <div className="flex-1 min-w-[260px]">
                                <FolderPathInput
                                    value={newPath}
                                    onChange={setNewPath}
                                    onEnter={handleAddPath}
                                    placeholder="Pick or paste a folder (e.g. /media/movies)"
                                    serverId={serverId}
                                    modalTitle="Add library folder"
                                />
                            </div>
                            <button type="button" onClick={handleAddPath} className="vora-button-secondary">Add path</button>
                        </div>
                    </div>

                    <SectionHeading>Options</SectionHeading>
                    <label className="flex items-center gap-3 cursor-pointer select-none bg-[var(--vora-info-soft)] border border-[var(--vora-info-500)]/30 p-3 rounded-[var(--vora-radius-md)] transition-colors hover:bg-[var(--vora-info-soft)]/70">
                        <input
                            type="checkbox"
                            checked={library.enableRealTimeWatching}
                            onChange={e => handleChange('enableRealTimeWatching', e.target.checked)}
                            className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                        />
                        <span className="text-sm font-semibold text-[var(--vora-info-text)]">Enable Real-Time Folder Watching</span>
                    </label>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                        <Checkbox checked={library.useLocalAssets} onChange={v => handleChange('useLocalAssets', v)} label="Use local assets" />
                        {showVideoOptions && <Checkbox checked={library.findExtras} onChange={v => handleChange('findExtras', v)} label="Find extras" />}
                        {showVideoOptions && <Checkbox checked={library.onlyShowTrailers} onChange={v => handleChange('onlyShowTrailers', v)} label="Only show trailers" />}
                        <Checkbox checked={library.enableVideoPreviewThumbnails} onChange={v => handleChange('enableVideoPreviewThumbnails', v)} label="Enable video preview thumbnails" />
                        {showVideoOptions && <Checkbox checked={library.enableCreditsDetection} onChange={v => handleChange('enableCreditsDetection', v)} label="Enable credits detection" />}
                        {showVideoOptions && <Checkbox checked={library.enableVoiceActivityDetection} onChange={v => handleChange('enableVoiceActivityDetection', v)} label="Enable voice activity detection" />}
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <FieldLabel>Collections Display</FieldLabel>
                            <select
                                value={library.collectionDisplay}
                                onChange={e => handleChange('collectionDisplay', Number(e.target.value))}
                                className="vora-input cursor-pointer"
                            >
                                <option value={0}>Show collections and their items</option>
                                <option value={1}>Hide items which are in collections</option>
                                <option value={2}>Hide collections but show their items</option>
                            </select>
                        </div>
                        <div>
                            <FieldLabel>Minimum Collection Size</FieldLabel>
                            <select
                                value={library.minimumCollectionSize || 1}
                                onChange={e => handleChange('minimumCollectionSize', Number(e.target.value))}
                                className="vora-input cursor-pointer"
                            >
                                {Array.from({ length: 25 }, (_, i) => i + 1).map(num => (
                                    <option key={num} value={num}>{num}</option>
                                ))}
                            </select>
                            <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Collections with fewer items than this will be hidden from the library.</p>
                        </div>
                    </div>

                    {isTvShow && (
                        <>
                            <SectionHeading>TV Show Settings</SectionHeading>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                                <div>
                                    <FieldLabel>Episode Sorting</FieldLabel>
                                    <select value={library.episodeSorting} onChange={e => handleChange('episodeSorting', Number(e.target.value))} className="vora-input cursor-pointer">
                                        <option value={0}>Oldest first</option>
                                        <option value={1}>Newest first</option>
                                    </select>
                                </div>
                                <div>
                                    <FieldLabel>Episode Ordering</FieldLabel>
                                    <select value={library.episodeOrder} onChange={e => handleChange('episodeOrder', Number(e.target.value))} className="vora-input cursor-pointer">
                                        <option value={0}>TheTVDB</option>
                                        <option value={1}>The Movie Database</option>
                                    </select>
                                </div>
                                <div>
                                    <FieldLabel>Seasons</FieldLabel>
                                    <select value={library.seasonsDisplay} onChange={e => handleChange('seasonsDisplay', Number(e.target.value))} className="vora-input cursor-pointer">
                                        <option value={0}>Show</option>
                                        <option value={1}>Hide for single-season series</option>
                                        <option value={2}>Hide</option>
                                    </select>
                                </div>
                            </div>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                <Checkbox checked={library.useSeasonTitles} onChange={v => handleChange('useSeasonTitles', v)} label="Use season titles" />
                                <Checkbox checked={library.enableIntroDetection} onChange={v => handleChange('enableIntroDetection', v)} label="Enable intro detection" />
                            </div>
                        </>
                    )}

                    <div className="pt-6 border-t border-[var(--vora-border-subtle)] flex justify-between items-center">
                        <button
                            type="button"
                            onClick={handleDelete}
                            className="px-5 py-2 rounded-[var(--vora-radius-md)] text-sm font-semibold text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-white transition-colors cursor-pointer"
                        >
                            Delete library
                        </button>

                        <button type="button" onClick={handleSave} disabled={saving} className="vora-button-primary">
                            {saving ? 'Saving…' : 'Save changes'}
                        </button>
                    </div>
                </div>
            </div>

            {alertModal.isOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-[var(--vora-bg-overlay)] backdrop-blur-sm p-4" onClick={closeAlert}>
                    <div className="vora-card shadow-[var(--vora-shadow-overlay)] p-6 max-w-md w-full" onClick={e => e.stopPropagation()}>
                        <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-2">{alertModal.title}</h3>
                        <p className="text-sm text-[var(--vora-text-secondary)] mb-6 whitespace-pre-wrap">{alertModal.message}</p>
                        <div className="flex justify-end">
                            <button type="button" onClick={closeAlert} className="vora-button-primary">OK</button>
                        </div>
                    </div>
                </div>
            )}

            {confirmModal.isOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-[var(--vora-bg-overlay)] backdrop-blur-sm p-4" onClick={closeConfirm}>
                    <div className="vora-card shadow-[var(--vora-shadow-overlay)] p-6 max-w-md w-full" onClick={e => e.stopPropagation()}>
                        <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-2">{confirmModal.title}</h3>
                        <p className="text-sm text-[var(--vora-text-secondary)] mb-6 whitespace-pre-wrap">{confirmModal.message}</p>
                        <div className="flex justify-end gap-3">
                            <button type="button" onClick={closeConfirm} className="vora-button-secondary">Cancel</button>
                            <button
                                type="button"
                                onClick={() => { confirmModal.onConfirm(); closeConfirm(); }}
                                className="px-5 py-2 rounded-[var(--vora-radius-md)] text-sm font-semibold text-white bg-[var(--vora-danger-500)] hover:bg-[var(--vora-danger-text)] transition-colors cursor-pointer"
                            >
                                Confirm delete
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
