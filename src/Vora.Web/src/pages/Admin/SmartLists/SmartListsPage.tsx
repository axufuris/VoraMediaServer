import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { smartListService, type SmartListAdminDto, type SmartListDefaultDto } from '../../../api/Collections/smartListService';
import { collectionService, type CollectionSummary } from '../../../api/Collections/collectionService';
import { libraryService, type LibrarySummary } from '../../../api/Media/libraryService';
import { useDialog } from '../../../dialogs';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import SmartListEditorModal from './SmartListEditorModal';
import { describeList, emptyForm, formFromList, requestFromForm, type SmartListForm } from './smartListForm';

export default function SmartListsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();

    const [lists, setLists] = useState<SmartListAdminDto[]>([]);
    const [defaults, setDefaults] = useState<SmartListDefaultDto[]>([]);
    const [collections, setCollections] = useState<CollectionSummary[]>([]);
    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);

    const [editor, setEditor] = useState<{ id: string | null; form: SmartListForm } | null>(null);
    const [saving, setSaving] = useState(false);
    const [restoring, setRestoring] = useState(false);
    const [draggedId, setDraggedId] = useState<string | null>(null);

    const refreshLists = useCallback(() => {
        Promise.all([smartListService.getAllLists(serverId), smartListService.getDefaults(serverId)])
            .then(([listsData, defaultsData]) => {
                setLists(listsData);
                setDefaults(defaultsData);
            })
            .catch(console.error);
    }, [serverId]);

    useEffect(() => {
        let isMounted = true;
        Promise.all([
            smartListService.getAllLists(serverId),
            smartListService.getDefaults(serverId).catch(() => [] as SmartListDefaultDto[]),
            collectionService.getAllCollections(serverId).catch(() => [] as CollectionSummary[]),
            libraryService.getLibraries(serverId).catch(() => [] as LibrarySummary[]),
        ]).then(([listsData, defaultsData, collData, libData]) => {
            if (!isMounted) return;
            setLists(listsData);
            setDefaults(defaultsData);
            setCollections(collData);
            setLibraries(libData);
        }).catch(console.error);
        return () => { isMounted = false; };
    }, [serverId]);

    const missingDefaults = defaults.filter(d => !d.isPresent);

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

    const handleDelete = async (list: SmartListAdminDto) => {
        const ok = await dialog.confirm({
            title: 'Delete this smart list?',
            message: list.defaultKey
                ? `"${list.title}" is one of the default rows. You can bring it back later with Restore default lists.`
                : `"${list.title}" will be removed from every home screen.`,
            confirmText: 'Delete',
            tone: 'danger',
        });
        if (!ok) return;
        try {
            await smartListService.deleteList(list.id, serverId);
        } catch {
            await dialog.alert({ title: 'Could not delete', message: 'The smart list was not deleted. Please try again.', tone: 'danger' });
        }
        refreshLists();
    };

    const handleRestoreDefaults = async () => {
        const ok = await dialog.confirm({
            title: 'Restore default lists?',
            message: `These rows will be added back at the end of the list: ${missingDefaults.map(d => d.title).join(', ')}.`,
            confirmText: 'Restore',
        });
        if (!ok) return;
        setRestoring(true);
        try {
            await smartListService.restoreDefaults(serverId);
            refreshLists();
        } catch {
            await dialog.alert({ title: 'Could not restore', message: 'The default lists were not restored. Please try again.', tone: 'danger' });
        } finally {
            setRestoring(false);
        }
    };

    const handleSave = async (form: SmartListForm) => {
        if (!editor) return;
        const existing = lists.find(l => l.id === editor.id);
        const payload = requestFromForm(form, existing ? existing.displayOrder : lists.length);

        setSaving(true);
        try {
            if (editor.id) await smartListService.updateList(editor.id, payload, serverId);
            else await smartListService.createList(payload, serverId);
            setEditor(null);
            refreshLists();
        } catch {
            await dialog.alert({ title: 'Could not save', message: 'Failed to save the smart list. Please try again.', tone: 'danger' });
        } finally {
            setSaving(false);
        }
    };

    const collectionTitle = (id: string) => collections.find(c => c.id === id)?.title;
    const libraryName = (id: string) => libraries.find(l => l.id === id)?.name;

    const openCreate = () => setEditor({ id: null, form: emptyForm() });

    return (
        <div data-vora-page="">
            <PageHeader
                title="Smart Lists"
                description="The rows on everyone's home screen. Reorder them by dragging. A row with nothing to show for a profile is hidden for that profile."
                actions={
                    <div className="flex flex-wrap items-center gap-2">
                        {missingDefaults.length > 0 && (
                            <button type="button" onClick={handleRestoreDefaults} disabled={restoring} className="vora-button-secondary disabled:cursor-not-allowed disabled:opacity-70">
                                {restoring ? 'Restoring…' : `Restore default lists (${missingDefaults.length})`}
                            </button>
                        )}
                        <button type="button" onClick={openCreate} className="vora-button-primary flex items-center gap-2">
                            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" /></svg>
                            Create list
                        </button>
                    </div>
                }
            />

            <div className="mx-auto max-w-6xl px-8 pb-10 pt-6">
                {lists.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No smart lists yet"
                            description="Create a list to populate one of the rows on the home screen."
                            actionLabel="Create list"
                            onAction={openCreate}
                        />
                    </div>
                ) : (
                    <div className="vora-card overflow-x-auto">
                        <table className="w-full text-left">
                            <thead className="border-b border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] text-[11px] uppercase tracking-wider text-[var(--vora-text-muted)]">
                                <tr>
                                    <th className="w-10" />
                                    <th className="px-4 py-3 font-semibold">Row title</th>
                                    <th className="px-4 py-3 font-semibold">Shows</th>
                                    <th className="px-4 py-3 font-semibold">Visibility</th>
                                    <th className="px-4 py-3 text-right font-semibold">Actions</th>
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
                                        className={`cursor-grab transition-colors active:cursor-grabbing ${draggedId === list.id ? 'bg-[var(--vora-accent-soft)] opacity-50' : 'hover:bg-[var(--vora-bg-sunken)]/50'}`}
                                    >
                                        <td className="px-3 py-3 text-[var(--vora-text-disabled)]">
                                            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                                        </td>
                                        <td className="px-4 py-3">
                                            <div className="flex flex-wrap items-center gap-2">
                                                <span className="font-semibold text-[var(--vora-text-primary)]">{list.title}</span>
                                                {list.defaultKey && <HealthBadge tone="neutral" showDot={false}>Default</HealthBadge>}
                                            </div>
                                        </td>
                                        <td className="px-4 py-3 text-sm text-[var(--vora-text-secondary)]">{describeList(list, collectionTitle, libraryName)}</td>
                                        <td className="px-4 py-3">
                                            <div className="flex flex-col gap-1 text-xs">
                                                <span className={list.showOnHomepage ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-text-disabled)]'}>
                                                    {list.showOnHomepage ? 'On home screen' : 'Turned off'}
                                                </span>
                                                <span className={list.showToFriends ? 'text-[var(--vora-text-secondary)]' : 'text-[var(--vora-text-disabled)]'}>
                                                    {list.showToFriends ? 'Everyone' : 'Admins only'}
                                                </span>
                                            </div>
                                        </td>
                                        <td className="px-4 py-3 text-right">
                                            <div className="flex justify-end gap-3 text-xs font-semibold">
                                                <button type="button" onClick={() => setEditor({ id: list.id, form: formFromList(list) })} className="cursor-pointer text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)]">Edit</button>
                                                <button type="button" onClick={() => handleDelete(list)} className="cursor-pointer text-[var(--vora-danger-text)] hover:text-[var(--vora-danger-500)]">Delete</button>
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {editor && (
                <SmartListEditorModal
                    key={editor.id ?? 'new'}
                    isOpen
                    isEditing={editor.id !== null}
                    initial={editor.form}
                    collections={collections}
                    libraries={libraries}
                    saving={saving}
                    onClose={() => setEditor(null)}
                    onSave={handleSave}
                />
            )}
        </div>
    );
}
