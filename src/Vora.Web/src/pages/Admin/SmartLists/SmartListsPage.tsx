import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { smartListService, type SmartListAdminDto, type SmartListRulesDto, type CreateSmartListRequest } from '../../../api/Collections/smartListService';
import { collectionService, type CollectionSummary } from '../../../api/Collections/collectionService';
import { useDialog } from '../../../dialogs';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

export default function SmartListsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();

    const [lists, setLists] = useState<SmartListAdminDto[]>([]);
    const [collections, setCollections] = useState<CollectionSummary[]>([]);

    const [isModalOpen, setIsModalOpen] = useState(false);
    const [saving, setSaving] = useState(false);
    const [editingId, setEditingId] = useState<string | null>(null);
    const [draggedId, setDraggedId] = useState<string | null>(null);

    const [title, setTitle] = useState('');
    const [listMode, setListMode] = useState<'rules' | 'collection'>('rules');
    const [collectionId, setCollectionId] = useState('');
    const [mediaTypes, setMediaTypes] = useState<string[]>([]);
    const [decade, setDecade] = useState('');
    const [sortBy, setSortBy] = useState(0);
    const [maxItems, setMaxItems] = useState(20);
    const [showOnHomepage, setShowOnHomepage] = useState(true);
    const [isSpotlight, setIsSpotlight] = useState(false);
    const [showToFriends, setShowToFriends] = useState(true);

    useEffect(() => {
        let isMounted = true;
        Promise.all([
            smartListService.getAllLists(serverId),
            collectionService.getAllCollections(serverId),
        ]).then(([listsData, collData]) => {
            if (isMounted) {
                setLists(listsData);
                setCollections(collData);
            }
        }).catch(console.error);
        return () => { isMounted = false; };
    }, [serverId]);

    const refreshLists = async () => {
        try {
            const data = await smartListService.getAllLists(serverId);
            setLists(data);
        } catch (err) {
            console.error(err);
        }
    };

    const handleDrop = async (targetId: string) => {
        if (!draggedId || draggedId === targetId) return;

        const oldIndex = lists.findIndex(l => l.id === draggedId);
        const newIndex = lists.findIndex(l => l.id === targetId);

        const newLists = [...lists];
        const [removed] = newLists.splice(oldIndex, 1);
        newLists.splice(newIndex, 0, removed);

        const updatedLists = newLists.map((l, idx) => ({ ...l, displayOrder: idx }));
        setLists(updatedLists);

        try {
            await smartListService.reorderLists(updatedLists.map(l => l.id), serverId);
        } catch (err) {
            console.error('Failed to reorder', err);
            refreshLists();
        }
    };

    const handleDelete = async (id: string) => {
        if (!await dialog.confirm('Delete this smart list?')) return;
        await smartListService.deleteList(id, serverId);
        refreshLists();
    };

    const handleToggleSpotlight = async (list: SmartListAdminDto) => {
        try {
            await smartListService.setSpotlight(list.id, !list.isSpotlight, serverId);
            await refreshLists();
        } catch (err) {
            console.error('Failed to update spotlight', err);
        }
    };

    const toggleMediaType = (type: string) => {
        if (listMode === 'collection') return;
        setMediaTypes(prev => prev.includes(type) ? prev.filter(t => t !== type) : [...prev, type]);
    };

    const openCreateModal = () => {
        setEditingId(null); setTitle(''); setListMode('rules');
        setCollectionId(''); setMediaTypes([]); setDecade(''); setSortBy(0);
        setMaxItems(20); setShowOnHomepage(true); setShowToFriends(true); setIsSpotlight(false); setIsModalOpen(true);
    };

    const openEditModal = (list: SmartListAdminDto) => {
        setEditingId(list.id); setTitle(list.title);
        setSortBy(list.sortBy); setMaxItems(list.maxItems); setShowOnHomepage(list.showOnHomepage);
        setShowToFriends(list.showToFriends); setIsSpotlight(list.isSpotlight); setListMode(list.collectionId ? 'collection' : 'rules');
        setCollectionId(list.collectionId || '');

        try {
            const rules = JSON.parse(list.filterRulesJson) as SmartListRulesDto;
            setMediaTypes(rules.mediaTypes || []); setDecade(rules.decade ? rules.decade.toString() : '');
        } catch { setMediaTypes([]); setDecade(''); }

        setIsModalOpen(true);
    };

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (saving) return;

        const rules: SmartListRulesDto = {};
        if (mediaTypes.length > 0) rules.mediaTypes = mediaTypes;
        if (decade) rules.decade = parseInt(decade);

        const existingList = lists.find(l => l.id === editingId);

        const payload: CreateSmartListRequest = {
            title,
            filterRulesJson: listMode === 'rules' && Object.keys(rules).length > 0 ? JSON.stringify(rules) : '{}',
            collectionId: listMode === 'collection' && collectionId ? collectionId : undefined,
            sortBy, maxItems, showOnHomepage, showToFriends, isSpotlight,
            displayOrder: existingList ? existingList.displayOrder : lists.length,
        };

        setSaving(true);
        try {
            if (editingId) await smartListService.updateList(editingId, payload, serverId);
            else await smartListService.createList(payload, serverId);

            setIsModalOpen(false);
            refreshLists();
        } catch {
            await dialog.alert('Failed to save the smart list. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    const getListType = (list: SmartListAdminDto) => {
        if (list.collectionId) {
            const col = collections.find(c => c.id === list.collectionId);
            return col ? `Collection: ${col.title}` : 'Collection (unknown)';
        }

        try {
            const rules = JSON.parse(list.filterRulesJson) as SmartListRulesDto;
            const parts = [];
            if (rules.mediaTypes && rules.mediaTypes.length > 0) parts.push(rules.mediaTypes.join(' & '));
            if (rules.decade) parts.push(`${rules.decade}s`);
            return parts.length > 0 ? parts.join(', ') : 'Mixed / Global';
        } catch { return 'Invalid rules'; }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Smart Lists"
                description="Reorderable home-screen rows powered by rules or hand-curated collections."
                actions={
                    <button type="button" onClick={openCreateModal} className="vora-button-primary flex items-center gap-2">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" /></svg>
                        Create list
                    </button>
                }
            />

            <div className="px-8 pb-10 max-w-6xl mx-auto pt-6">
                {lists.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No smart lists yet"
                            description="Create a list to populate one of the rows on the home screen."
                            actionLabel="Create list"
                            onAction={openCreateModal}
                        />
                    </div>
                ) : (
                    <div className="vora-card overflow-hidden">
                        <table className="w-full text-left">
                            <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[11px] uppercase tracking-wider text-[var(--vora-text-muted)]">
                                <tr>
                                    <th className="w-10" />
                                    <th className="px-4 py-3 font-semibold">List title</th>
                                    <th className="px-4 py-3 font-semibold">Rules / type</th>
                                    <th className="px-4 py-3 font-semibold">Visibility</th>
                                    <th className="px-4 py-3 font-semibold text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {lists.map(list => (
                                    <tr
                                        key={list.id}
                                        draggable
                                        onDragStart={() => setDraggedId(list.id)}
                                        onDragOver={(e) => e.preventDefault()}
                                        onDrop={() => handleDrop(list.id)}
                                        onDragEnd={() => setDraggedId(null)}
                                        className={`transition-colors cursor-grab active:cursor-grabbing ${draggedId === list.id ? 'opacity-50 bg-[var(--vora-accent-soft)]' : 'hover:bg-[var(--vora-bg-sunken)]/50'}`}
                                    >
                                        <td className="px-3 py-3 text-[var(--vora-text-disabled)]">
                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                                        </td>
                                        <td className="px-4 py-3">
                                            <span className="font-semibold text-[var(--vora-text-primary)]">{list.title}</span>
                                            {list.isSpotlight && (
                                                <span
                                                    className="ml-2 inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider"
                                                    style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)', border: '1px solid var(--vora-accent-500)' }}
                                                    title="This list powers the home page spotlight hero"
                                                >
                                                    <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                                        <path d="M12 2l2.4 7.4H22l-6.2 4.5 2.4 7.4L12 16.8 5.8 21.3l2.4-7.4L2 9.4h7.6L12 2z" />
                                                    </svg>
                                                    Spotlight
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-[var(--vora-text-secondary)]">{getListType(list)}</td>
                                        <td className="px-4 py-3">
                                            <div className="flex flex-col gap-1 text-xs">
                                                <span className={list.showOnHomepage ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-text-disabled)]'}>
                                                    {list.showOnHomepage ? '✓' : '✗'} Homepage
                                                </span>
                                                <span className={list.showToFriends ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-text-disabled)]'}>
                                                    {list.showToFriends ? '✓' : '✗'} Friends
                                                </span>
                                            </div>
                                        </td>
                                        <td className="px-4 py-3 text-right">
                                            <div className="flex justify-end gap-3 text-xs font-semibold">
                                                <button
                                                    type="button"
                                                    onClick={() => handleToggleSpotlight(list)}
                                                    className={`cursor-pointer ${list.isSpotlight ? 'text-[var(--vora-accent-text)]' : 'text-[var(--vora-text-muted)] hover:text-[var(--vora-text-secondary)]'}`}
                                                    title={list.isSpotlight ? 'Remove from the Home spotlight' : 'Use as the Home spotlight (replaces any current one)'}
                                                >
                                                    {list.isSpotlight ? '★ Spotlight' : '☆ Spotlight'}
                                                </button>
                                                <button type="button" onClick={() => openEditModal(list)} className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer">Edit</button>
                                                <button type="button" onClick={() => handleDelete(list.id)} className="text-[var(--vora-danger-text)] hover:text-[var(--vora-danger-500)] cursor-pointer">Delete</button>
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {isModalOpen && (
                <div className="fixed inset-0 bg-[var(--vora-bg-overlay)] backdrop-blur-sm flex items-center justify-center z-[200] p-4" onClick={() => setIsModalOpen(false)}>
                    <div className="vora-card shadow-[var(--vora-shadow-overlay)] p-6 w-full max-w-lg" onClick={e => e.stopPropagation()}>
                        <h2 className="text-base font-semibold text-[var(--vora-text-primary)] mb-4">
                            {editingId ? 'Edit smart list' : 'Create smart list'}
                        </h2>

                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <FieldLabel>List title</FieldLabel>
                                <input
                                    required
                                    type="text"
                                    value={title}
                                    onChange={e => setTitle(e.target.value)}
                                    className="vora-input"
                                />
                            </div>

                            <div className="grid grid-cols-2 gap-1 p-1 bg-[var(--vora-bg-sunken)] rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                                <button
                                    type="button"
                                    onClick={() => setListMode('rules')}
                                    className={`py-1.5 text-sm font-semibold rounded-[var(--vora-radius-sm)] cursor-pointer transition-colors ${listMode === 'rules' ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)]'}`}
                                >
                                    Rule-based
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setListMode('collection')}
                                    className={`py-1.5 text-sm font-semibold rounded-[var(--vora-radius-sm)] cursor-pointer transition-colors ${listMode === 'collection' ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)]'}`}
                                >
                                    Collection-based
                                </button>
                            </div>

                            {listMode === 'collection' && (
                                <div className="p-4 bg-[var(--vora-info-soft)] border border-[var(--vora-info-500)]/30 rounded-[var(--vora-radius-md)]">
                                    <FieldLabel>Source collection</FieldLabel>
                                    <select required value={collectionId} onChange={e => setCollectionId(e.target.value)} className="vora-input cursor-pointer">
                                        <option value="">Select a collection…</option>
                                        {collections.map(c => <option key={c.id} value={c.id}>{c.title}</option>)}
                                    </select>
                                </div>
                            )}

                            {listMode === 'rules' && (
                                <>
                                    <div>
                                        <FieldLabel>Media types</FieldLabel>
                                        <div className="flex flex-wrap gap-3">
                                            {['Movie', 'TvShow', 'Season', 'Episode', 'Track'].map(type => (
                                                <label key={type} className="flex items-center gap-2 text-sm text-[var(--vora-text-primary)] cursor-pointer">
                                                    <input
                                                        type="checkbox"
                                                        checked={mediaTypes.includes(type)}
                                                        onChange={() => toggleMediaType(type)}
                                                        className="w-4 h-4 accent-[var(--vora-accent-500)]"
                                                    />
                                                    {type === 'TvShow' ? 'TV Show' : type}
                                                </label>
                                            ))}
                                        </div>
                                    </div>

                                    <div>
                                        <FieldLabel>Decade rule</FieldLabel>
                                        <select value={decade} onChange={e => setDecade(e.target.value)} className="vora-input cursor-pointer">
                                            <option value="">Any</option>
                                            <option value="2020">2020s</option>
                                            <option value="2010">2010s</option>
                                            <option value="1990">1990s</option>
                                            <option value="1980">1980s</option>
                                        </select>
                                    </div>
                                </>
                            )}

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <FieldLabel>Sort by</FieldLabel>
                                    <select value={sortBy} onChange={e => setSortBy(Number(e.target.value))} className="vora-input cursor-pointer">
                                        <option value={0}>Recently added</option>
                                        <option value={1}>Recently released</option>
                                        <option value={3}>Random (shuffle)</option>
                                        <option value={4}>Top rated</option>
                                        <option value={5}>Most watched</option>
                                    </select>
                                </div>
                                <div>
                                    <FieldLabel>Max items</FieldLabel>
                                    <input
                                        type="number"
                                        min={1}
                                        max={100}
                                        value={maxItems}
                                        onChange={e => setMaxItems(Number(e.target.value))}
                                        className="vora-input"
                                    />
                                </div>
                            </div>

                            <div className="space-y-2 pt-3 border-t border-[var(--vora-border-subtle)]">
                                <label className="flex items-center gap-3 cursor-pointer select-none">
                                    <input
                                        type="checkbox"
                                        checked={showOnHomepage}
                                        onChange={e => setShowOnHomepage(e.target.checked)}
                                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                    />
                                    <span className="text-sm font-medium text-[var(--vora-text-primary)]">Show on homepage</span>
                                </label>
                                <label className="flex items-center gap-3 cursor-pointer select-none">
                                    <input
                                        type="checkbox"
                                        checked={showToFriends}
                                        onChange={e => setShowToFriends(e.target.checked)}
                                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                    />
                                    <span className="text-sm font-medium text-[var(--vora-text-primary)]">Show to friends</span>
                                </label>
                                <p className="text-xs text-[var(--vora-text-muted)]">Use the <span className="font-semibold text-[var(--vora-text-secondary)]">★ Spotlight</span> button on the list row to make a list power the Home hero — only one can be the spotlight at a time.</p>
                            </div>

                            <div className="flex justify-end gap-3 mt-4 pt-4 border-t border-[var(--vora-border-subtle)]">
                                <button type="button" onClick={() => setIsModalOpen(false)} className="vora-button-secondary">Cancel</button>
                                <button type="submit" disabled={saving} className="vora-button-primary disabled:opacity-70 disabled:cursor-not-allowed">{saving ? 'Saving…' : 'Save list'}</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
