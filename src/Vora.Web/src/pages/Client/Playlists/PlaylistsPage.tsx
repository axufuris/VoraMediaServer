import { useCallback, useEffect, useMemo, useState } from 'react';
import { playlistTileArt } from '../../../utils/playlistArt';
import { useNavigate, useParams } from 'react-router-dom';
import { playlistService, type PlaylistSummaryVM, type SharedPlaylistsVM } from '../../../api/Collections/playlistService';
import { musicService, type GeneratedMixSummaryVM } from '../../../api/Music/musicService';
import { smartPlaylistService, type SmartPlaylistSummaryVM, type PlaylistMediaType } from '../../../api/Music/smartPlaylistService';
import MediaCard from '../../../components/Client/Primitives/MediaCard';
import MediaGrid from '../../../components/Client/Primitives/MediaGrid';
import { useDialog } from '../../../dialogs';
import SmartPlaylistEditorModal from './SmartPlaylistEditorModal';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import Tabs from '../../../components/Client/Primitives/Tabs';
import EmptyState from '../../../components/Client/Primitives/EmptyState';

type TypeFilter = 'video' | 'music';

// Yours or everyone else's. Not persisted: like any library, it opens on your
// own, and remembering the Shared tab would need a new localStorage key.
type Scope = 'yours' | 'shared';

const TAB_STORAGE_KEY = 'playlists_active_tab';

interface PlaylistsPageProps {
    embedded?: boolean;
    // Pins the list to one media type and hides the type tabs, for hosts that
    // are already scoped (the Music page shows only music playlists).
    lockedType?: TypeFilter;
    showMixes?: boolean;
}

export default function PlaylistsPage({ embedded = false, lockedType, showMixes = true }: PlaylistsPageProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [playlists, setPlaylists] = useState<PlaylistSummaryVM[]>([]);
    const [dailyMixes, setDailyMixes] = useState<GeneratedMixSummaryVM[]>([]);
    const [smartPlaylists, setSmartPlaylists] = useState<SmartPlaylistSummaryVM[]>([]);
    const [shared, setShared] = useState<SharedPlaylistsVM>({ manual: [], smart: [] });
    const [scope, setScope] = useState<Scope>('yours');
    const [loading, setLoading] = useState(true);

    const [savedTab, setActiveTab] = useState<TypeFilter>(() => {
        const saved = typeof window !== 'undefined' ? window.localStorage.getItem(TAB_STORAGE_KEY) : null;
        return saved === 'music' ? 'music' : 'video';
    });
    const activeTab = lockedType ?? savedTab;

    const [chooserOpen, setChooserOpen] = useState(false);
    const [smartEditorType, setSmartEditorType] = useState<PlaylistMediaType | null>(null);
    const [manualCreatorType, setManualCreatorType] = useState<PlaylistMediaType | null>(null);
    const [newName, setNewName] = useState('');
    const [newDescription, setNewDescription] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    useEffect(() => {
        try { window.localStorage.setItem(TAB_STORAGE_KEY, savedTab); } catch { /* ignore */ }
    }, [savedTab]);

    const loadSmart = () => {
        smartPlaylistService.list(serverId)
            .then(setSmartPlaylists)
            .catch(err => { console.error(err); setSmartPlaylists([]); });
    };

    useEffect(() => {
        Promise.all([
            playlistService.getPlaylists(serverId),
            musicService.getMixes(serverId).catch(() => [] as GeneratedMixSummaryVM[]),
            smartPlaylistService.list(serverId).catch(() => [] as SmartPlaylistSummaryVM[]),
            // Failing independently: a server that cannot list shared playlists
            // still shows the profile its own.
            playlistService.getShared(serverId).catch((): SharedPlaylistsVM => ({ manual: [], smart: [] }))
        ]).then(([pls, mixes, smarts, sharedByOthers]) => {
            setPlaylists(pls);
            setDailyMixes(mixes);
            setSmartPlaylists(smarts);
            setShared(sharedByOthers);
        }).finally(() => setLoading(false));
    }, [serverId]);

    const handleManualCreate = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!newName.trim() || !manualCreatorType) return;

        setIsSubmitting(true);
        try {
            const { id } = await playlistService.createPlaylist(newName, newDescription, manualCreatorType, serverId);
            navigate(serverId ? `/server/${serverId}/playlist/${id}` : `/playlist/${id}`);
        } catch (error) {
            console.error('Failed to create playlist:', error);
            await dialog.alert('Failed to create playlist. Check console for details.');
            setIsSubmitting(false);
        }
    };

    const handleDelete = async (e: React.MouseEvent, id: string, name: string) => {
        e.stopPropagation();
        if (await dialog.confirm(`Are you absolutely sure you want to delete the playlist "${name}"?`)) {
            try {
                await playlistService.deletePlaylist(id, serverId);
                setPlaylists(prev => prev.filter(p => p.id !== id));
            } catch (error) {
                console.error('Failed to delete playlist', error);
                await dialog.alert('Failed to delete playlist.');
            }
        }
    };

    const matchesTab = useCallback((mediaType: PlaylistMediaType): boolean =>
        activeTab === 'music'
            ? mediaType === 'Music'
            : mediaType === 'Movies' || mediaType === 'Shows' || mediaType === 'Mixed'
    , [activeTab]);

    const visiblePlaylists = useMemo(() => playlists.filter(p => matchesTab(p.mediaType)), [playlists, matchesTab]);

    const visibleSmart = useMemo(() => smartPlaylists.filter(sp => matchesTab(sp.mediaType)), [smartPlaylists, matchesTab]);
    const visibleMixes = useMemo(() => showMixes && activeTab === 'music' ? dailyMixes : [], [dailyMixes, activeTab, showMixes]);

    const visibleSharedManual = useMemo(() => shared.manual.filter(p => matchesTab(p.mediaType)), [shared.manual, matchesTab]);
    const visibleSharedSmart = useMemo(() => shared.smart.filter(sp => matchesTab(sp.mediaType)), [shared.smart, matchesTab]);

    if (loading) {
        return (
            <div className="min-h-full pb-16">
                {!embedded && <PageHeader title="My Playlists" subtitle="Curated by you, by us, and by smart rules." />}
                <div className="px-8">
                    <div className="vora-skeleton mb-6 h-10 w-64" />
                    <div className="grid grid-cols-2 gap-6 md:grid-cols-4 lg:grid-cols-6">
                        {Array.from({ length: 6 }, (_, i) => <div key={i} className="vora-skeleton aspect-square" />)}
                    </div>
                </div>
            </div>
        );
    }

    const emptyState = visiblePlaylists.length === 0 && visibleMixes.length === 0 && visibleSmart.length === 0;

    const newAction = (
        <button
            type="button"
            onClick={() => setChooserOpen(true)}
            className="vora-button-primary cursor-pointer inline-flex items-center gap-1.5"
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.25"><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>
            New
        </button>
    );

    return (
        <div className="min-h-full pb-16">
            {embedded ? (
                <div className="flex justify-end px-8 pt-4">{newAction}</div>
            ) : (
                <PageHeader title="My Playlists" subtitle="Curated by you, by us, and by smart rules." actions={newAction} />
            )}

            <div className="px-8">
                {/* Whose playlists is the primary split; the media type is a filter
                    within it. In the Music tab the type is already fixed, so only
                    the split shows. */}
                <Tabs<Scope>
                    tabs={[
                        { key: 'yours', label: 'Yours' },
                        { key: 'shared', label: 'Shared' },
                    ]}
                    active={scope}
                    onChange={setScope}
                    className="mb-6"
                    actions={lockedType ? undefined : (
                        <div className="flex items-center gap-2" role="group" aria-label="Media type">
                            {([
                                { key: 'video', label: 'Movies & Shows' },
                                { key: 'music', label: 'Music' },
                            ] as const).map(t => (
                                <button
                                    key={t.key}
                                    type="button"
                                    onClick={() => setActiveTab(t.key)}
                                    data-active={activeTab === t.key}
                                    aria-pressed={activeTab === t.key}
                                    className="vora-pill cursor-pointer rounded-full px-3 py-1 text-xs font-medium"
                                >
                                    {t.label}
                                </button>
                            ))}
                        </div>
                    )}
                />

                {scope === 'shared' ? (
                    <SharedPlaylists
                        manual={visibleSharedManual}
                        smart={visibleSharedSmart}
                        openManual={id => navigate(serverId ? `/server/${serverId}/playlist/${id}` : `/playlist/${id}`)}
                        openSmart={id => navigate(serverId ? `/server/${serverId}/smart-playlist/${id}` : `/smart-playlist/${id}`)}
                    />
                ) : (
                <>

                {visibleMixes.length > 0 && (
                    <div className="mb-10">
                        <h2 className="text-xl font-bold text-[var(--vora-text-secondary)] mb-4">For You</h2>
                        <div className="grid grid-cols-3 md:grid-cols-5 lg:grid-cols-8 gap-4">
                            {visibleMixes.map(mix => (
                                <button
                                    key={mix.id}
                                    type="button"
                                    onClick={() => navigate(serverId ? `/server/${serverId}/music?mix=${mix.id}` : `/music?mix=${mix.id}`)}
                                    className="group text-left cursor-pointer"
                                    title={mix.name}
                                >
                                    <div className="w-full aspect-square rounded bg-gradient-to-br from-orange-700 via-purple-900 to-indigo-900 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2 relative">
                                        {mix.artworkUrl ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" /> : null}
                                        <div className="absolute inset-0 flex flex-col justify-end p-3 bg-gradient-to-t from-black/80 via-black/30 to-transparent">
                                            <div className="text-xs uppercase tracking-widest text-orange-300/90 font-bold">Daily Mix {mix.slot}</div>
                                            <div className="text-sm font-bold text-[var(--vora-text-primary)] drop-shadow-md truncate">{mix.descriptionTag ?? 'Mix'}</div>
                                        </div>
                                    </div>
                                    <div className="text-sm font-bold text-[var(--vora-text-secondary)] truncate" title={mix.name}>{mix.name}</div>
                                    <div className="text-xs text-[var(--vora-text-muted)]">{mix.trackCount} tracks</div>
                                </button>
                            ))}
                        </div>
                    </div>
                )}

                {visibleSmart.length > 0 && (
                    <div className="mb-10">
                        <h2 className="text-xl font-bold text-[var(--vora-text-secondary)] mb-4 flex items-center gap-2">
                            <span className="text-fuchsia-400">⚙</span> Smart Playlists
                        </h2>
                        <div className="grid grid-cols-3 md:grid-cols-5 lg:grid-cols-8 gap-4">
                            {visibleSmart.map(sp => (
                                <SmartPlaylistTile
                                    key={sp.id}
                                    playlist={sp}
                                    onOpen={() => navigate(serverId ? `/server/${serverId}/smart-playlist/${sp.id}` : `/smart-playlist/${sp.id}`)}
                                />
                            ))}
                        </div>
                    </div>
                )}

                {emptyState ? (
                    <EmptyState
                        title="No playlists yet"
                        description="Tap New to create one — manual or smart. Music, movies, or shows."
                        action={(
                            <button type="button" onClick={() => setChooserOpen(true)} className="vora-button-primary cursor-pointer">Create a playlist</button>
                        )}
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <line x1="4" y1="6" x2="20" y2="6" />
                                <line x1="4" y1="12" x2="20" y2="12" />
                                <line x1="4" y1="18" x2="14" y2="18" />
                                <polygon points="17 16 22 19 17 22 17 16" fill="currentColor" />
                            </svg>
                        )}
                    />
                ) : visiblePlaylists.length === 0 ? null : (
                    <>
                        {(visibleMixes.length > 0 || visibleSmart.length > 0) && <h2 className="text-xl font-bold text-[var(--vora-text-secondary)] mb-4">Your Playlists</h2>}
                        <MediaGrid>
                            {visiblePlaylists.map(p => (
                                <MediaCard
                                    key={p.id}
                                    item={{ type: 'Playlist', title: p.name, itemCount: p.itemCount, mediaTypeLabel: p.mediaType !== 'Mixed' ? p.mediaType : null, sharedByYou: p.isShared }}
                                    shape="square"
                                    {...playlistTileArt(p)}
                                    onClick={() => navigate(serverId ? `/server/${serverId}/playlist/${p.id}` : `/playlist/${p.id}`)}
                                    onDelete={(e) => handleDelete(e, p.id, p.name)}
                                    fill
                                />
                            ))}
                        </MediaGrid>
                    </>
                )}
                </>
                )}
            </div>

            {chooserOpen && (
                <PlaylistTypeChooser
                    presetType={lockedType === 'music' ? 'Music' : undefined}
                    onCancel={() => setChooserOpen(false)}
                    onPickManual={(t) => { setChooserOpen(false); setManualCreatorType(t); setNewName(''); setNewDescription(''); }}
                    onPickSmart={(t) => { setChooserOpen(false); setSmartEditorType(t); }}
                />
            )}

            {manualCreatorType && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
                    <div className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-xl shadow-2xl max-w-md w-full p-6">
                        <h2 className="text-2xl font-bold text-[var(--vora-text-primary)] mb-2">Create Playlist</h2>
                        <div className="text-xs uppercase tracking-widest text-[var(--vora-accent-500)] font-bold mb-6">{manualCreatorType}</div>
                        <form onSubmit={handleManualCreate} className="space-y-4">
                            <div>
                                <label className="block text-sm font-bold text-[var(--vora-text-muted)] mb-2">Name</label>
                                <input
                                    autoFocus
                                    required
                                    type="text"
                                    value={newName}
                                    onChange={e => setNewName(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]"
                                    placeholder={manualCreatorType === 'Music' ? 'e.g. Workout' : manualCreatorType === 'Movies' ? 'e.g. Comfort movies' : manualCreatorType === 'Shows' ? 'e.g. Sunday binge' : 'e.g. My picks'}
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-bold text-[var(--vora-text-muted)] mb-2">Description (Optional)</label>
                                <textarea
                                    value={newDescription}
                                    onChange={e => setNewDescription(e.target.value)}
                                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-3 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] min-h-[100px] resize-none"
                                />
                            </div>
                            <div className="flex justify-end gap-3 mt-8 pt-4 border-t border-[var(--vora-border-subtle)]">
                                <button type="button" onClick={() => setManualCreatorType(null)} disabled={isSubmitting} className="px-4 py-2 rounded text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-raised)] transition-colors cursor-pointer">Cancel</button>
                                <button type="submit" disabled={isSubmitting} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors cursor-pointer disabled:opacity-50">
                                    {isSubmitting ? 'Creating...' : 'Create'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {smartEditorType && (
                <SmartPlaylistEditorModal
                    serverId={serverId}
                    initialMediaType={smartEditorType}
                    onClose={() => setSmartEditorType(null)}
                    onSaved={(summary) => {
                        setSmartEditorType(null);
                        loadSmart();
                        navigate(serverId ? `/server/${serverId}/smart-playlist/${summary.id}` : `/smart-playlist/${summary.id}`);
                    }}
                />
            )}
        </div>
    );
}

// One smart playlist tile, for your own and for other people's shared ones. Its
// markup used to be written inline in the page; the Shared tab would have been
// the second copy.
function SmartPlaylistTile({ playlist: sp, onOpen }: { playlist: SmartPlaylistSummaryVM; onOpen: () => void }) {
    const grad = sp.mediaType === 'Music'
        ? 'from-fuchsia-700 via-violet-900 to-indigo-900'
        : sp.mediaType === 'Movies'
            ? 'from-sky-700 via-blue-900 to-indigo-900'
            : 'from-amber-700 via-orange-900 to-red-900';
    const unit = sp.mediaType === 'Shows' ? 'episodes' : sp.mediaType === 'Movies' ? 'movies' : 'tracks';

    return (
        <button type="button" onClick={onOpen} className="group text-left cursor-pointer" title={sp.name}>
            <div className={`w-full aspect-square rounded bg-gradient-to-br ${grad} border border-[var(--vora-border-subtle)] group-hover:border-fuchsia-400 transition-all overflow-hidden mb-2 relative`}>
                {sp.artworkUrl ? <img src={sp.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" /> : <div className="absolute inset-0 flex items-center justify-center text-5xl text-[var(--vora-text-primary)]/50">⚙</div>}
                <div className="absolute top-2 right-2 px-2 py-0.5 text-[10px] uppercase tracking-widest font-bold rounded bg-fuchsia-500/30 text-fuchsia-100 border border-fuchsia-400/40">{sp.mediaType}</div>
            </div>
            <div className="text-sm font-bold text-[var(--vora-text-secondary)] truncate" title={sp.name}>{sp.name}</div>
            <div className="text-xs text-[var(--vora-text-muted)]">
                {sp.trackCount} {unit}{sp.isOwner && sp.isShared ? ' · Shared' : ''}
            </div>
            {!sp.isOwner && sp.ownerName && (
                <div className="text-xs text-[var(--vora-text-muted)] truncate">by {sp.ownerName}</div>
            )}
        </button>
    );
}

interface SharedPlaylistsProps {
    manual: PlaylistSummaryVM[];
    smart: SmartPlaylistSummaryVM[];
    openManual: (id: string) => void;
    openSmart: (id: string) => void;
}

// Everything other profiles on this server have shared. Read-only from here:
// no delete control, because none of these are yours to delete. Opening one
// offers Play and Save a copy.
function SharedPlaylists({ manual, smart, openManual, openSmart }: SharedPlaylistsProps) {
    if (manual.length === 0 && smart.length === 0) {
        return (
            <EmptyState
                title="Nothing shared yet"
                description="When someone on this server shares a playlist, it shows up here. To share one of yours, open it and turn on Share."
                icon={(
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
                        <circle cx="18" cy="5" r="3" />
                        <circle cx="6" cy="12" r="3" />
                        <circle cx="18" cy="19" r="3" />
                        <line x1="8.59" y1="13.51" x2="15.42" y2="17.49" />
                        <line x1="15.41" y1="6.51" x2="8.59" y2="10.49" />
                    </svg>
                )}
            />
        );
    }

    return (
        <>
            {smart.length > 0 && (
                <div className="mb-10">
                    <h2 className="text-xl font-bold text-[var(--vora-text-secondary)] mb-4">Smart Playlists</h2>
                    <div className="grid grid-cols-3 md:grid-cols-5 lg:grid-cols-8 gap-4">
                        {smart.map(sp => <SmartPlaylistTile key={sp.id} playlist={sp} onOpen={() => openSmart(sp.id)} />)}
                    </div>
                </div>
            )}

            {manual.length > 0 && (
                <>
                    {smart.length > 0 && <h2 className="text-xl font-bold text-[var(--vora-text-secondary)] mb-4">Playlists</h2>}
                    <MediaGrid>
                        {manual.map(p => (
                            <MediaCard
                                key={p.id}
                                item={{ type: 'Playlist', title: p.name, itemCount: p.itemCount, mediaTypeLabel: p.mediaType !== 'Mixed' ? p.mediaType : null, ownerName: p.ownerName }}
                                shape="square"
                                {...playlistTileArt(p)}
                                onClick={() => openManual(p.id)}
                                fill
                            />
                        ))}
                    </MediaGrid>
                </>
            )}
        </>
    );
}

interface ChooserProps {
    // Set when the page is embedded somewhere that already fixes the kind — the
    // Music tab, where asking "what kind of playlist?" and offering Movies is a
    // question the user has already answered by being there.
    presetType?: PlaylistMediaType;
    onCancel: () => void;
    onPickManual: (type: PlaylistMediaType) => void;
    onPickSmart: (type: PlaylistMediaType) => void;
}

function PlaylistTypeChooser({ presetType, onCancel, onPickManual, onPickSmart }: ChooserProps) {
    const [pickedType, setPickedType] = useState<PlaylistMediaType | null>(presetType ?? null);

    if (!pickedType) {
        return (
            <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
                <div className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-xl shadow-2xl max-w-lg w-full p-6">
                    <div className="flex items-center justify-between mb-6">
                        <h2 className="text-xl font-bold text-[var(--vora-text-primary)]">What kind of playlist?</h2>
                        <button onClick={onCancel} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] cursor-pointer text-2xl leading-none">×</button>
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                        <TypeChoice label="Music" icon="🎵" colorClass="from-fuchsia-700 to-violet-900" onClick={() => setPickedType('Music')} />
                        <TypeChoice label="Movies" icon="🎬" colorClass="from-sky-700 to-blue-900" onClick={() => setPickedType('Movies')} />
                        <TypeChoice label="Shows" icon="📺" colorClass="from-amber-700 to-orange-900" onClick={() => setPickedType('Shows')} />
                        <TypeChoice label="Mixed" icon="🎨" colorClass="from-gray-700 to-gray-900" onClick={() => setPickedType('Mixed')} />
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
            <div className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-xl shadow-2xl max-w-lg w-full p-6">
                <div className="flex items-center justify-between mb-2">
                    <h2 className="text-xl font-bold text-[var(--vora-text-primary)]">Manual or Smart?</h2>
                    <button onClick={onCancel} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] cursor-pointer text-2xl leading-none">×</button>
                </div>
                <div className="text-xs uppercase tracking-widest text-[var(--vora-accent-500)] font-bold mb-6">{pickedType}</div>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <button
                        onClick={() => onPickManual(pickedType)}
                        className="text-left p-4 border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] rounded transition-colors cursor-pointer bg-[var(--vora-bg-canvas)]/40"
                    >
                        <div className="text-lg font-bold text-[var(--vora-text-primary)] mb-1">Manual</div>
                        <div className="text-xs text-[var(--vora-text-muted)]">Pick items yourself. Order them however you like. Best for one-off mixes.</div>
                    </button>
                    {pickedType !== 'Mixed' ? (
                        <button
                            onClick={() => onPickSmart(pickedType)}
                            className="text-left p-4 border border-[var(--vora-border-subtle)] hover:border-fuchsia-500 rounded transition-colors cursor-pointer bg-[var(--vora-bg-canvas)]/40"
                        >
                            <div className="text-lg font-bold text-[var(--vora-text-primary)] mb-1 flex items-center gap-2"><span className="text-fuchsia-400">⚙</span> Smart</div>
                            <div className="text-xs text-[var(--vora-text-muted)]">Define rules. Auto-updates as your library changes. Best for living views like "Unwatched comedy" or "Heavy rotation rock."</div>
                        </button>
                    ) : (
                        <div className="p-4 border border-dashed border-[var(--vora-border-subtle)] rounded bg-[var(--vora-bg-canvas)]/20 opacity-60">
                            <div className="text-lg font-bold text-[var(--vora-text-muted)] mb-1">Smart</div>
                            <div className="text-xs text-[var(--vora-text-muted)]">Smart playlists need a single media type. Pick Music, Movies, or Shows.</div>
                        </div>
                    )}
                </div>
                <div className="flex justify-end mt-6 pt-4 border-t border-[var(--vora-border-subtle)]">
                    <button onClick={() => setPickedType(null)} className="px-4 py-2 rounded text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-raised)] transition-colors cursor-pointer">Back</button>
                </div>
            </div>
        </div>
    );
}

function TypeChoice({ label, icon, colorClass, onClick }: { label: string; icon: string; colorClass: string; onClick: () => void }) {
    return (
        <button
            onClick={onClick}
            className={`p-6 rounded border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] transition-all cursor-pointer text-left bg-gradient-to-br ${colorClass}`}
        >
            <div className="text-3xl mb-2">{icon}</div>
            <div className="font-bold text-[var(--vora-text-primary)] text-lg">{label}</div>
        </button>
    );
}
