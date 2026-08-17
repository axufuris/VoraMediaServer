import { useState, useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { libraryService, type MediaLibrary } from '../../../api/Media/libraryService';
import { libraryAdminService, type MarkerCoverageVM, type ThumbnailCoverageVM } from '../../../api/Media/libraryAdminService';
import { pluginAdminService, type PluginOptionVM } from '../../../api/System/pluginAdminService';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import FolderPathInput from '../../../components/Admin/FolderBrowser/FolderPathInput';
import { useDialog } from '../../../dialogs';

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

function MarkerCoverageCard({ libraryId, serverId }: { libraryId: string, serverId?: string }) {
    const [coverage, setCoverage] = useState<MarkerCoverageVM | null>(null);
    const [loading, setLoading] = useState(false);

    const load = async () => {
        setLoading(true);
        try {
            const data = await libraryAdminService.getLibraryMarkerCoverage(libraryId, serverId);
            setCoverage(data);
        } catch (err) {
            console.error('Failed to load marker coverage', err);
        } finally {
            setLoading(false);
        }
    };

    const loadRef = useRef(load);
    useEffect(() => {
        loadRef.current = load;
    });

    useEffect(() => {
        void loadRef.current();
    }, [libraryId, serverId]);

    if (!coverage && !loading) return null;

    const pct = (n: number) => coverage && coverage.totalItems > 0
        ? Math.round((n / coverage.totalItems) * 100)
        : 0;

    return (
        <div className="vora-card p-5 space-y-4">
            <div className="flex items-center justify-between">
                <h2 className="text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Marker coverage</h2>
                <button
                    type="button"
                    onClick={load}
                    disabled={loading}
                    className="vora-button-secondary text-xs disabled:opacity-50"
                >
                    {loading ? 'Refreshing…' : 'Refresh'}
                </button>
            </div>
            {coverage && coverage.totalItems === 0 ? (
                <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>This library has no movies or episodes yet.</p>
            ) : coverage ? (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    <CoverageStat label="Total items" value={`${coverage.totalItems}`} />
                    <CoverageStat label="With any marker" value={`${coverage.itemsWithAnyMarker} (${pct(coverage.itemsWithAnyMarker)}%)`} />
                    <CoverageStat label="Intro" value={`${coverage.itemsWithIntro} (${pct(coverage.itemsWithIntro)}%)`} />
                    <CoverageStat label="Credits" value={`${coverage.itemsWithCredits} (${pct(coverage.itemsWithCredits)}%)`} />
                    <CoverageStat label="Credits scene" value={`${coverage.itemsWithCreditsScene}`} />
                    <CoverageStat label="Recap" value={`${coverage.itemsWithRecap}`} />
                    <CoverageStat label="Preview" value={`${coverage.itemsWithPreview}`} />
                    <CoverageStat label="Missing duration" value={`${coverage.itemsMissingDuration}`} warn={coverage.itemsMissingDuration > 0} />
                </div>
            ) : (
                <div className="vora-skeleton h-24" />
            )}
            <p className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                Counts are over movies + episodes only. Items with locked markers are skipped by automatic re-analysis and keep whatever markers were last saved.
            </p>
        </div>
    );
}

function ThumbnailCoverageCard({ libraryId, libraryType, enabled, serverId }: { libraryId: string, libraryType: string, enabled: boolean, serverId?: string }) {
    const [coverage, setCoverage] = useState<ThumbnailCoverageVM | null>(null);
    const [loading, setLoading] = useState(false);
    const [regenerating, setRegenerating] = useState(false);
    const dialog = useDialog();

    const isVideoType = ['movie', 'tvshow', 'homevideo'].includes(libraryType.toLowerCase());

    const load = async () => {
        if (!isVideoType) return;
        setLoading(true);
        try {
            const data = await libraryAdminService.getLibraryThumbnailCoverage(libraryId, serverId);
            setCoverage(data);
        } catch (err) {
            console.error('Failed to load thumbnail coverage', err);
        } finally {
            setLoading(false);
        }
    };

    const loadRef = useRef(load);
    useEffect(() => {
        loadRef.current = load;
    });

    useEffect(() => {
        void loadRef.current();
    }, [libraryId, serverId, isVideoType]);

    const regenerate = async (force: boolean) => {
        if (force) {
            const ok = await dialog.confirm('Regenerate ALL thumbnails for this library? This redoes every item, including ones that already have thumbnails.');
            if (!ok) return;
        }
        setRegenerating(true);
        try {
            await libraryAdminService.regenerateLibraryThumbnails(libraryId, force, serverId);
            await dialog.alert('Video thumbnail generation started in the background!');
            void load();
        } catch (err) {
            console.error('Failed to queue thumbnail regeneration', err);
            await dialog.alert('Failed to queue thumbnail regeneration.');
        } finally {
            setRegenerating(false);
        }
    };

    if (!isVideoType) return null;
    if (!coverage && !loading) return null;

    const pct = coverage && coverage.total > 0 ? Math.round((coverage.withThumbnails / coverage.total) * 100) : 0;

    return (
        <div className="vora-card p-5 space-y-4">
            <div className="flex items-center justify-between">
                <h2 className="text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Video preview thumbnails</h2>
                <div className="flex items-center gap-2">
                    <button type="button" onClick={load} disabled={loading} className="vora-button-secondary text-xs disabled:opacity-50">
                        {loading ? 'Refreshing…' : 'Refresh'}
                    </button>
                    <button type="button" onClick={() => regenerate(false)} disabled={regenerating || !enabled} className="vora-button-secondary text-xs disabled:opacity-50" title={enabled ? '' : 'Enable the checkbox in library settings first'}>
                        {regenerating ? 'Queued…' : 'Regenerate missing'}
                    </button>
                    <button type="button" onClick={() => regenerate(true)} disabled={regenerating || !enabled} className="vora-button-secondary text-xs disabled:opacity-50" title={enabled ? 'Redo every item, including ones that already have thumbnails' : 'Enable the checkbox in library settings first'}>
                        {regenerating ? 'Queued…' : 'Regenerate all'}
                    </button>
                </div>
            </div>
            {!enabled && (
                <p className="text-xs" style={{ color: 'var(--vora-warning-text)' }}>
                    Thumbnails are disabled for this library. Turn on "Enable video preview thumbnails" above to start generation on the next scheduled pass.
                </p>
            )}
            {coverage && coverage.total === 0 ? (
                <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>This library has no movies or episodes yet.</p>
            ) : coverage ? (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                    <CoverageStat label="Total items" value={`${coverage.total}`} />
                    <CoverageStat label="With thumbnails" value={`${coverage.withThumbnails} (${pct}%)`} />
                    <CoverageStat label="Missing" value={`${coverage.total - coverage.withThumbnails}`} warn={coverage.total - coverage.withThumbnails > 0} />
                </div>
            ) : (
                <div className="vora-skeleton h-20" />
            )}
            <p className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                Generation runs daily at the time set in System Settings → Video Preview Thumbnails. Items with locked thumbnails are skipped. "Regenerate missing" fills the gaps (and resumes an interrupted run); "Regenerate all" redoes every item.
            </p>
        </div>
    );
}

function CoverageStat({ label, value, warn = false }: { label: string, value: string, warn?: boolean }) {
    return (
        <div className="rounded-md p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
            <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>{label}</div>
            <div className="mt-1 text-sm font-semibold" style={{ color: warn ? 'var(--vora-danger-text)' : 'var(--vora-text-primary)' }}>{value}</div>
        </div>
    );
}

export default function ManageLibrary() {
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();

    const [library, setLibrary] = useState<MediaLibrary | null>(null);
    const [saving, setSaving] = useState(false);
    const [newPath, setNewPath] = useState('');
    const [providers, setProviders] = useState<PluginOptionVM[]>([]);
    const [ratingProviders, setRatingProviders] = useState<PluginOptionVM[]>([]);
    const [artworkProviders, setArtworkProviders] = useState<PluginOptionVM[]>([]);


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

    const showAlert = (title: string, message: string) => { void dialog.alert({ title, message }); };
    const showConfirm = (title: string, message: string, onConfirm: () => void) => {
        void dialog.confirm({ title, message, tone: 'danger', confirmText: 'Confirm delete' }).then((ok) => { if (ok) onConfirm(); });
    };

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
            'Are you absolutely sure you want to delete this library?\n\nThis will remove all associated media records from Vora (your physical files on disk will NOT be touched).\n\nDeletion runs in the background — for a large library it may take a little while to fully disappear. You can watch its progress under Background Tasks.',
            async () => {
                try {
                    await libraryAdminService.deleteLibrary(library.id, serverId);
                    navigate(serverId ? `/admin/server/${serverId}/libraries` : '/admin/libraries');
                } catch (err) {
                    showAlert('Error', 'Failed to start library deletion. Please check the console.');
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
    const showVideoPreviewThumbnails = library.type.toLowerCase() === 'movie' || library.type.toLowerCase() === 'tvshow' || library.type.toLowerCase() === 'homevideo';
    const backUrl = serverId ? `/admin/server/${serverId}/libraries` : '/admin/libraries';

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
                    <button
                        type="button"
                        onClick={async () => {
                            try {
                                await libraryAdminService.analyzeLibrary(library.id, false, serverId);
                                showAlert('Analysis started', 'Marker detection queued for items not yet analyzed. Use the coverage card below to track progress.');
                            } catch (err) {
                                console.error(err);
                                showAlert('Error', 'Failed to trigger library analysis.');
                            }
                        }}
                        className="vora-button-secondary text-xs"
                    >
                        Analyze library
                    </button>
                    <button
                        type="button"
                        onClick={async () => {
                            const ok = await dialog.confirm({
                                title: 'Re-analyze everything?',
                                message: 'This re-runs marker detection on every item in this library, including ones already analyzed — it discards existing detected markers and can take a long time. Manually locked markers are kept. Continue?',
                                confirmText: 'Re-analyze all',
                            });
                            if (!ok) return;
                            try {
                                await libraryAdminService.analyzeLibrary(library.id, true, serverId);
                                showAlert('Re-analysis started', 'Marker detection queued for every item in this library.');
                            } catch (err) {
                                console.error(err);
                                showAlert('Error', 'Failed to trigger library re-analysis.');
                            }
                        }}
                        className="vora-button-secondary text-xs"
                    >
                        Re-analyze all
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

                <MarkerCoverageCard libraryId={library.id} serverId={serverId} />

                <ThumbnailCoverageCard libraryId={library.id} libraryType={library.type} enabled={library.enableVideoPreviewThumbnails} serverId={serverId} />

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

                    <SectionHeading>Filename Scanner Regex</SectionHeading>
                    <div className="space-y-2">
                        <textarea
                            value={library.scannerRegex ?? ''}
                            onChange={e => handleChange('scannerRegex', e.target.value)}
                            rows={3}
                            spellCheck={false}
                            placeholder="Leave blank to restore the default parser for this library type"
                            className="vora-input w-full font-mono text-xs"
                        />
                        <p className="text-xs text-[var(--vora-text-muted)]">
                            How the local scanner parses filenames. Named groups it reads —
                            Movies: <span className="font-mono">Title</span>, <span className="font-mono">Year</span>, <span className="font-mono">Provider</span>, <span className="font-mono">ProviderId</span>;
                            TV: <span className="font-mono">Season</span>, <span className="font-mono">Episode</span>, <span className="font-mono">AirDate</span>, <span className="font-mono">EpisodeTitle</span>.
                            Leave blank to restore the default. Resolution and edition (including Radarr/Sonarr <span className="font-mono">{'{edition-...}'}</span> tags) are detected separately; audio/video codecs come from file analysis.
                        </p>
                    </div>

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
                            Comma-separated. Any file whose name contains one of these (case-insensitive) is skipped by the scanner — useful for files still being transcoded (e.g. <span className="font-mono">.TDARR</span>). Rename the file to have it picked up.
                        </p>
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
                    )}

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

        </div>
    );
}
