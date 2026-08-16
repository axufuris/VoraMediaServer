import { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { libraryAdminService, type CreateLibraryRequest } from '../../../api/Media/libraryAdminService';
import { pluginAdminService, type PluginOptionVM } from '../../../api/System/pluginAdminService';
import { useDialog } from '../../../dialogs';
import IconSelect, { type IconSelectOption } from '../../../components/Common/IconSelect';
import { renderNavIcon } from '../../../layouts/parts/navIcons';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import FolderPathInput from '../../../components/Admin/FolderBrowser/FolderPathInput';

const MEDIA_TYPE_OPTIONS: IconSelectOption<number>[] = [
    { value: 1, label: 'Movies', icon: renderNavIcon('Movie', 'w-5 h-5') },
    { value: 2, label: 'TV Shows', icon: renderNavIcon('TvShow', 'w-5 h-5') },
    { value: 3, label: 'Music', icon: renderNavIcon('Music', 'w-5 h-5') },
    { value: 4, label: 'Home Video', icon: renderNavIcon('HomeVideo', 'w-5 h-5') },
];

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

export default function CreateLibrary() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [saving, setSaving] = useState(false);
    const [newPath, setNewPath] = useState('');
    const [providers, setProviders] = useState<PluginOptionVM[]>([]);
    const [ratingProviders, setRatingProviders] = useState<PluginOptionVM[]>([]);
    const [artworkProviders, setArtworkProviders] = useState<PluginOptionVM[]>([]);

    const [library, setLibrary] = useState<CreateLibraryRequest>({
        name: '',
        type: 1,
        folderPaths: [],
        excludeFilters: [],
        metadataProviderId: 'tmdb_metadata',
        thirdPartyRating1ProviderId: 'omdb_imdb',
        thirdPartyRating2ProviderId: 'omdb_rotten_tomatoes',
        artworkProviderId: 'tmdb_artwork',
        enableRealTimeWatching: true,
        findExtras: true,
        onlyShowTrailers: false,
        enableVideoPreviewThumbnails: false,
        enableCreditsDetection: false,
        enablePreviewDetection: false,
        minimumCollectionSize: 1,
        episodeSorting: 0,
        episodeOrder: 0,
        useSeasonTitles: true,
        seasonsDisplay: 0,
        enableIntroDetection: false,
    });

    useEffect(() => {
        Promise.all([
            pluginAdminService.getMetadataProviders(serverId),
            pluginAdminService.getRatingsProviders(serverId),
            pluginAdminService.getArtworkProviders(serverId),
        ]).then(([meta, ratings, artwork]) => {
            setProviders(meta);
            setRatingProviders(ratings);
            setArtworkProviders(artwork);
        }).catch(console.error);
    }, [serverId]);

    const handleChange = <K extends keyof CreateLibraryRequest>(field: K, value: CreateLibraryRequest[K]) => {
        setLibrary({ ...library, [field]: value });
    };

    const handleTypeChange = (newType: number) => {
        const updates: Partial<CreateLibraryRequest> = { type: newType };

        if (newType === 1) {
            updates.metadataProviderId = 'tmdb_metadata';
            updates.thirdPartyRating1ProviderId = 'omdb_imdb';
            updates.thirdPartyRating2ProviderId = 'omdb_rotten_tomatoes';
            updates.artworkProviderId = 'tmdb_artwork';
        } else if (newType === 2) {
            updates.metadataProviderId = 'tvdb_metadata';
            updates.thirdPartyRating1ProviderId = 'omdb_imdb';
            updates.thirdPartyRating2ProviderId = 'omdb_rotten_tomatoes';
            updates.artworkProviderId = 'tvdb_artwork';
        } else if (newType === 3 || newType === 4) {
            updates.metadataProviderId = 'local_metadata';
            updates.thirdPartyRating1ProviderId = '';
            updates.thirdPartyRating2ProviderId = '';
            updates.artworkProviderId = '';
        }

        setLibrary(prev => ({ ...prev, ...updates }));
    };

    const handleAddPath = async () => {
        if (!newPath.trim()) return;
        if (library.folderPaths.includes(newPath.trim())) {
            await dialog.alert('This path is already in the library.');
            return;
        }
        handleChange('folderPaths', [...library.folderPaths, newPath.trim()]);
        setNewPath('');
    };

    const handleRemovePath = (pathToRemove: string) => {
        handleChange('folderPaths', library.folderPaths.filter(p => p !== pathToRemove));
    };

    const handleSave = async () => {
        if (!library.name.trim()) {
            await dialog.alert('Library name is required.');
            return;
        }

        setSaving(true);
        try {
            await libraryAdminService.createLibrary(library, serverId);
            await dialog.alert('Library created. The initial scan has started in the background.');
            navigate(serverId ? `/admin/server/${serverId}/libraries` : '/admin/libraries');
        } catch (err) {
            await dialog.alert('Failed to create library.');
            console.error(err);
        } finally {
            setSaving(false);
        }
    };

    const getLibraryTypeString = (type: number) => {
        if (type === 2) return 'TvShow';
        if (type === 3) return 'Music';
        if (type === 4) return 'HomeVideo';
        return 'Movie';
    };

    const currentTypeStr = getLibraryTypeString(library.type);
    const availableMetadataProviders = providers.filter(p => !p.supportedLibraryTypes || p.supportedLibraryTypes.includes(currentTypeStr));
    const availableRatingProviders = ratingProviders.filter(p => !p.supportedLibraryTypes || p.supportedLibraryTypes.includes(currentTypeStr));
    const availableArtworkProviders = artworkProviders.filter(p => !p.supportedLibraryTypes || p.supportedLibraryTypes.includes(currentTypeStr));

    const isTvShow = library.type === 2;
    const showVideoOptions = library.type === 1 || library.type === 2;
    const showVideoPreviewThumbnails = library.type === 1 || library.type === 2 || library.type === 4;
    const backUrl = serverId ? `/admin/server/${serverId}/libraries` : '/admin/libraries';

    return (
        <div data-vora-page="">
            <PageHeader
                title="Create Library"
                breadcrumb={
                    <button type="button" onClick={() => navigate(backUrl)} className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer font-medium">
                        ← Back to libraries
                    </button>
                }
                actions={
                    <button
                        type="button"
                        onClick={handleSave}
                        disabled={saving}
                        className="vora-button-primary"
                    >
                        {saving ? 'Creating…' : 'Create library'}
                    </button>
                }
            />

            <div className="px-8 pb-10 max-w-5xl mx-auto pt-6">
                <div className="vora-card p-6 space-y-6">
                    <SectionHeading>General</SectionHeading>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <FieldLabel>Library Name</FieldLabel>
                            <input
                                type="text"
                                value={library.name}
                                onChange={e => handleChange('name', e.target.value)}
                                placeholder="e.g. Movies, Kids TV"
                                className="vora-input"
                            />
                        </div>
                        <div>
                            <FieldLabel>Media Type</FieldLabel>
                            <IconSelect<number>
                                value={library.type}
                                options={MEDIA_TYPE_OPTIONS}
                                onChange={handleTypeChange}
                            />
                        </div>
                    </div>

                    <div>
                        <FieldLabel>Metadata Provider</FieldLabel>
                        <select
                            value={library.metadataProviderId}
                            onChange={e => handleChange('metadataProviderId', e.target.value)}
                            className="vora-input cursor-pointer"
                        >
                            {availableMetadataProviders.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                        </select>
                    </div>

                    {showVideoOptions && (
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
                    )}

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
                                    className="text-xs font-semibold px-3 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer shrink-0"
                                >
                                    Remove
                                </button>
                            </div>
                        ))}
                        {library.folderPaths.length === 0 && (
                            <p className="text-sm text-[var(--vora-text-muted)] italic">No folder paths added yet. Add at least one path to scan media.</p>
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
                            <button type="button" onClick={handleAddPath} className="vora-button-secondary">
                                Add path
                            </button>
                        </div>
                    </div>

                    {showVideoOptions && (
                        <>
                            <SectionHeading>Exclude Filters</SectionHeading>
                            <div className="space-y-2">
                                <input
                                    type="text"
                                    value={(library.excludeFilters ?? []).join(', ')}
                                    onChange={e => handleChange('excludeFilters', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                                    spellCheck={false}
                                    placeholder="e.g. .TDARR, .sample, WIP"
                                    className="vora-input w-full font-mono text-xs"
                                />
                                <p className="text-xs text-[var(--vora-text-muted)]">
                                    Comma-separated. Any file whose name contains one of these (case-insensitive) is skipped by the scanner — useful for files still being transcoded (e.g. <span className="font-mono">.TDARR</span>).
                                </p>
                            </div>
                        </>
                    )}

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
                        {showVideoOptions && <Checkbox checked={library.findExtras} onChange={v => handleChange('findExtras', v)} label="Find extras" />}
                        {showVideoOptions && <Checkbox checked={library.onlyShowTrailers} onChange={v => handleChange('onlyShowTrailers', v)} label="Only show trailers" />}
                        {showVideoPreviewThumbnails && <Checkbox checked={library.enableVideoPreviewThumbnails} onChange={v => handleChange('enableVideoPreviewThumbnails', v)} label="Enable video preview thumbnails" />}
                        {showVideoOptions && <Checkbox checked={library.enableCreditsDetection} onChange={v => handleChange('enableCreditsDetection', v)} label="Enable credits detection" />}
                        {showVideoOptions && <Checkbox checked={library.enablePreviewDetection} onChange={v => handleChange('enablePreviewDetection', v)} label="Enable next-episode preview skips" />}
                    </div>

                    {showVideoOptions && (
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                            <div>
                                <FieldLabel>Minimum Collection Size</FieldLabel>
                                <select
                                    value={library.minimumCollectionSize}
                                    onChange={e => handleChange('minimumCollectionSize', Number(e.target.value))}
                                    className="vora-input cursor-pointer"
                                >
                                    {Array.from({ length: 25 }, (_, i) => i + 1).map(num => (
                                        <option key={num} value={num}>{num}</option>
                                    ))}
                                </select>
                                <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Collections with fewer items than this will be hidden.</p>
                            </div>
                        </div>
                    )}

                    {isTvShow && (
                        <>
                            <SectionHeading>TV Show Settings</SectionHeading>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                                <div>
                                    <FieldLabel>Episode Sorting</FieldLabel>
                                    <select
                                        value={library.episodeSorting}
                                        onChange={e => handleChange('episodeSorting', Number(e.target.value))}
                                        className="vora-input cursor-pointer"
                                    >
                                        <option value={0}>Oldest first</option>
                                        <option value={1}>Newest first</option>
                                    </select>
                                </div>
                                <div>
                                    <FieldLabel>Episode Ordering</FieldLabel>
                                    <select
                                        value={library.episodeOrder}
                                        onChange={e => handleChange('episodeOrder', Number(e.target.value))}
                                        className="vora-input cursor-pointer"
                                    >
                                        <option value={0}>TheTVDB</option>
                                        <option value={1}>The Movie Database</option>
                                    </select>
                                </div>
                                <div>
                                    <FieldLabel>Seasons</FieldLabel>
                                    <select
                                        value={library.seasonsDisplay}
                                        onChange={e => handleChange('seasonsDisplay', Number(e.target.value))}
                                        className="vora-input cursor-pointer"
                                    >
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
                </div>
            </div>
        </div>
    );
}
