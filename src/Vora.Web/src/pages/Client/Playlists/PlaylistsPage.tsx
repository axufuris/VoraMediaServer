import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { playlistService, type PlaylistSummaryVM } from '../../../api/Collections/playlistService';
import { musicService, type GeneratedMixSummaryVM } from '../../../api/Music/musicService';
import { smartPlaylistService, type SmartPlaylistSummaryVM, type PlaylistMediaType } from '../../../api/Music/smartPlaylistService';
import MediaCard from '../../../components/Media/MediaCard';
import { useDialog } from '../../../dialogs';
import SmartPlaylistEditorModal from './SmartPlaylistEditorModal';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import Tabs from '../../../components/Client/Primitives/Tabs';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import { Modal } from '../../../components/Common/Modal';

type TypeFilter = 'all' | 'music' | 'video';

const TAB_STORAGE_KEY = 'playlists_active_tab';

export default function PlaylistsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [playlists, setPlaylists] = useState<PlaylistSummaryVM[]>([]);
    const [dailyMixes, setDailyMixes] = useState<GeneratedMixSummaryVM[]>([]);
    const [smartPlaylists, setSmartPlaylists] = useState<SmartPlaylistSummaryVM[]>([]);
    const [loading, setLoading] = useState(true);

    const [activeTab, setActiveTab] = useState<TypeFilter>(() => {
        const saved = (typeof window !== 'undefined' && window.localStorage.getItem(TAB_STORAGE_KEY)) as TypeFilter | null;
        return saved === 'music' || saved === 'video' ? saved : 'all';
    });

    const [chooserOpen, setChooserOpen] = useState(false);
    const [smartEditorType, setSmartEditorType] = useState<PlaylistMediaType | null>(null);
    const [manualCreatorType, setManualCreatorType] = useState<PlaylistMediaType | null>(null);
    const [newName, setNewName] = useState('');
    const [newDescription, setNewDescription] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    useEffect(() => {
        try { window.localStorage.setItem(TAB_STORAGE_KEY, activeTab); } catch { /* ignore */ }
    }, [activeTab]);

    const loadSmart = () => {
        smartPlaylistService.list(serverId)
            .then(setSmartPlaylists)
            .catch(err => { console.error(err); setSmartPlaylists([]); });
    };

    const loadPlaylists = () => {
        playlistService.getPlaylists(serverId).then(setPlaylists).catch(console.error);
    };

    useEffect(() => {
        Promise.all([
            playlistService.getPlaylists(serverId),
            musicService.getMixes(serverId).catch(() => [] as GeneratedMixSummaryVM[]),
            smartPlaylistService.list(serverId).catch(() => [] as SmartPlaylistSummaryVM[])
        ]).then(([pls, mixes, smarts]) => {
            setPlaylists(pls);
            setDailyMixes(mixes);
            setSmartPlaylists(smarts);
        }).finally(() => setLoading(false));
    }, [serverId]);

    const handleManualCreate = async (e: React.SyntheticEvent<HTMLFormElement>) => {
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

    const matchesTab = (mediaType: PlaylistMediaType): boolean => {
        if (activeTab === 'all') return true;
        if (activeTab === 'music') return mediaType === 'Music';
        if (activeTab === 'video') return mediaType === 'Movies' || mediaType === 'Shows';
        return true;
    };

    const visiblePlaylists = useMemo(() => {
        if (activeTab === 'all') return playlists;
        return playlists.filter(p => matchesTab(p.mediaType));
    }, [playlists, activeTab]);

    const visibleSmart = useMemo(() => smartPlaylists.filter(sp => matchesTab(sp.mediaType)), [smartPlaylists, activeTab]);
    const visibleMixes = useMemo(() => activeTab === 'all' || activeTab === 'music' ? dailyMixes : [], [dailyMixes, activeTab]);

    if (loading) {
        return (
            <div className="min-h-full pb-16">
                <PageHeader title="My Playlists" subtitle="Curated by you, by us, and by smart rules." />
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
            <PageHeader title="My Playlists" subtitle="Curated by you, by us, and by smart rules." actions={newAction} />

            <div className="px-8">
                <Tabs<TypeFilter>
                    tabs={[
                        { key: 'all', label: 'All' },
                        { key: 'music', label: 'Music' },
                        { key: 'video', label: 'Movies & Shows' },
                    ]}
                    active={activeTab}
                    onChange={setActiveTab}
                    className="mb-6"
                />

                {visibleMixes.length > 0 && (
                    <div className="mb-10">
                        <h2 className="text-xl font-bold text-gray-200 mb-4">For You</h2>
                        <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-6">
                            {visibleMixes.map(mix => (
                                <button
                                    key={mix.id}
                                    type="button"
                                    onClick={() => navigate(serverId ? `/server/${serverId}/audio?mix=${mix.id}` : `/audio?mix=${mix.id}`)}
                                    className="group text-left cursor-pointer"
                                    title={mix.name}
                                >
                                    <div className="w-full aspect-square rounded bg-gradient-to-br from-orange-700 via-purple-900 to-indigo-900 border border-gray-800 group-hover:border-orange-500 transition-all overflow-hidden mb-2 relative">
                                        {mix.artworkUrl ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" /> : null}
                                        <div className="absolute inset-0 flex flex-col justify-end p-3 bg-gradient-to-t from-black/80 via-black/30 to-transparent">
                                            <div className="text-xs uppercase tracking-widest text-orange-300/90 font-bold">Daily Mix {mix.slot}</div>
                                            <div className="text-sm font-bold text-white drop-shadow-md truncate">{mix.descriptionTag ?? 'Mix'}</div>
                                        </div>
                                    </div>
                                    <div className="text-sm font-bold text-gray-200 truncate" title={mix.name}>{mix.name}</div>
                                    <div className="text-xs text-gray-500">{mix.trackCount} tracks</div>
                                </button>
                            ))}
                        </div>
                    </div>
                )}

                {visibleSmart.length > 0 && (
                    <div className="mb-10">
                        <h2 className="text-xl font-bold text-gray-200 mb-4 flex items-center gap-2">
                            <span className="text-fuchsia-400">⚙</span> Smart Playlists
                        </h2>
                        <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-6">
                            {visibleSmart.map(sp => {
                                const grad = sp.mediaType === 'Music'
                                    ? 'from-fuchsia-700 via-violet-900 to-indigo-900'
                                    : sp.mediaType === 'Movies'
                                        ? 'from-sky-700 via-blue-900 to-indigo-900'
                                        : 'from-amber-700 via-orange-900 to-red-900';
                                return (
                                    <button
                                        key={sp.id}
                                        type="button"
                                        onClick={() => navigate(serverId ? `/server/${serverId}/smart-playlist/${sp.id}` : `/smart-playlist/${sp.id}`)}
                                        className="group text-left cursor-pointer"
                                        title={sp.name}
                                    >
                                        <div className={`w-full aspect-square rounded bg-gradient-to-br ${grad} border border-gray-800 group-hover:border-fuchsia-400 transition-all overflow-hidden mb-2 relative`}>
                                            {sp.artworkUrl ? <img src={sp.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" /> : <div className="absolute inset-0 flex items-center justify-center text-5xl text-white/50">⚙</div>}
                                            <div className="absolute top-2 right-2 px-2 py-0.5 text-[10px] uppercase tracking-widest font-bold rounded bg-fuchsia-500/30 text-fuchsia-100 border border-fuchsia-400/40">{sp.mediaType}</div>
                                        </div>
                                        <div className="text-sm font-bold text-gray-200 truncate" title={sp.name}>{sp.name}</div>
                                        <div className="text-xs text-gray-500">{sp.trackCount} {sp.mediaType === 'Shows' ? 'episodes' : sp.mediaType === 'Movies' ? 'movies' : 'tracks'}</div>
                                    </button>
                                );
                            })}
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
                        {(visibleMixes.length > 0 || visibleSmart.length > 0) && <h2 className="text-xl font-bold text-gray-200 mb-4">Your Playlists</h2>}
                        <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-6">
                            {visiblePlaylists.map(p => (
                                <MediaCard
                                    key={p.id}
                                    id={p.id}
                                    title={p.name}
                                    subtitle={`${p.itemCount} Items${p.mediaType !== 'Mixed' ? ` · ${p.mediaType}` : ''}`}
                                    type="Playlist"
                                    aspectRatio="square"
                                    multiPosters={p.posterUrls}
                                    imageUrl={p.posterUrls && p.posterUrls.length > 0 ? p.posterUrls[0] : undefined}
                                    onClick={() => navigate(serverId ? `/server/${serverId}/playlist/${p.id}` : `/playlist/${p.id}`)}
                                    onDelete={(e) => handleDelete(e, p.id, p.name)}
                                    isAdmin={true}
                                />
                            ))}
                        </div>
                    </>
                )}
            </div>

            {chooserOpen && (
                <PlaylistTypeChooser
                    onCancel={() => setChooserOpen(false)}
                    onPickManual={(t) => { setChooserOpen(false); setManualCreatorType(t); setNewName(''); setNewDescription(''); }}
                    onPickSmart={(t) => { setChooserOpen(false); setSmartEditorType(t); }}
                />
            )}

            {manualCreatorType && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
                    <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl max-w-md w-full p-6">
                        <h2 className="text-2xl font-bold text-white mb-2">Create Playlist</h2>
                        <div className="text-xs uppercase tracking-widest text-orange-400 font-bold mb-6">{manualCreatorType}</div>
                        <form onSubmit={handleManualCreate} className="space-y-4">
                            <div>
                                <label className="block text-sm font-bold text-gray-400 mb-2">Name</label>
                                <input
                                    autoFocus
                                    required
                                    type="text"
                                    value={newName}
                                    onChange={e => setNewName(e.target.value)}
                                    className="w-full bg-gray-950 border border-gray-700 rounded-md p-3 text-white outline-none focus:border-orange-500"
                                    placeholder={manualCreatorType === 'Music' ? 'e.g. Workout' : manualCreatorType === 'Movies' ? 'e.g. Comfort movies' : manualCreatorType === 'Shows' ? 'e.g. Sunday binge' : 'e.g. My picks'}
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-bold text-gray-400 mb-2">Description (Optional)</label>
                                <textarea
                                    value={newDescription}
                                    onChange={e => setNewDescription(e.target.value)}
                                    className="w-full bg-gray-950 border border-gray-700 rounded-md p-3 text-white outline-none focus:border-orange-500 min-h-[100px] resize-none"
                                />
                            </div>
                            <div className="flex justify-end gap-3 mt-8 pt-4 border-t border-gray-800">
                                <button type="button" onClick={() => setManualCreatorType(null)} disabled={isSubmitting} className="px-4 py-2 rounded text-gray-300 hover:bg-gray-700 transition-colors cursor-pointer">Cancel</button>
                                <button type="submit" disabled={isSubmitting} className="px-6 py-2 bg-orange-600 hover:bg-orange-500 text-white font-bold rounded shadow-lg transition-colors cursor-pointer disabled:opacity-50">
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

interface ChooserProps {
    onCancel: () => void;
    onPickManual: (type: PlaylistMediaType) => void;
    onPickSmart: (type: PlaylistMediaType) => void;
}

function PlaylistTypeChooser({ onCancel, onPickManual, onPickSmart }: ChooserProps) {
    const [pickedType, setPickedType] = useState<PlaylistMediaType | null>(null);

    if (!pickedType) {
        return (
            <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
                <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl max-w-lg w-full p-6">
                    <div className="flex items-center justify-between mb-6">
                        <h2 className="text-xl font-bold text-white">What kind of playlist?</h2>
                        <button onClick={onCancel} className="text-gray-400 hover:text-white cursor-pointer text-2xl leading-none">×</button>
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
            <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl max-w-lg w-full p-6">
                <div className="flex items-center justify-between mb-2">
                    <h2 className="text-xl font-bold text-white">Manual or Smart?</h2>
                    <button onClick={onCancel} className="text-gray-400 hover:text-white cursor-pointer text-2xl leading-none">×</button>
                </div>
                <div className="text-xs uppercase tracking-widest text-orange-400 font-bold mb-6">{pickedType}</div>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <button
                        onClick={() => onPickManual(pickedType)}
                        className="text-left p-4 border border-gray-700 hover:border-orange-500 rounded transition-colors cursor-pointer bg-gray-950/40"
                    >
                        <div className="text-lg font-bold text-white mb-1">Manual</div>
                        <div className="text-xs text-gray-400">Pick items yourself. Order them however you like. Best for one-off mixes.</div>
                    </button>
                    {pickedType !== 'Mixed' ? (
                        <button
                            onClick={() => onPickSmart(pickedType)}
                            className="text-left p-4 border border-gray-700 hover:border-fuchsia-500 rounded transition-colors cursor-pointer bg-gray-950/40"
                        >
                            <div className="text-lg font-bold text-white mb-1 flex items-center gap-2"><span className="text-fuchsia-400">⚙</span> Smart</div>
                            <div className="text-xs text-gray-400">Define rules. Auto-updates as your library changes. Best for living views like "Unwatched comedy" or "Heavy rotation rock."</div>
                        </button>
                    ) : (
                        <div className="p-4 border border-dashed border-gray-800 rounded bg-gray-950/20 opacity-60">
                            <div className="text-lg font-bold text-gray-500 mb-1">Smart</div>
                            <div className="text-xs text-gray-500">Smart playlists need a single media type. Pick Music, Movies, or Shows.</div>
                        </div>
                    )}
                </div>
                <div className="flex justify-end mt-6 pt-4 border-t border-gray-800">
                    <button onClick={() => setPickedType(null)} className="px-4 py-2 rounded text-gray-300 hover:bg-gray-700 transition-colors cursor-pointer">Back</button>
                </div>
            </div>
        </div>
    );
}

function TypeChoice({ label, icon, colorClass, onClick }: { label: string; icon: string; colorClass: string; onClick: () => void }) {
    return (
        <button
            onClick={onClick}
            className={`p-6 rounded border border-gray-700 hover:border-orange-500 transition-all cursor-pointer text-left bg-gradient-to-br ${colorClass}`}
        >
            <div className="text-3xl mb-2">{icon}</div>
            <div className="font-bold text-white text-lg">{label}</div>
        </button>
    );
}
