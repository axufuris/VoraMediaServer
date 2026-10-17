import { useEffect, useState } from 'react';
import { profileService, type LibrarySortVM } from '../../../api/Users/profileService';
import { useDialog } from '../../../dialogs';
import { errorDetail } from '../../../utils/apiError';
import { fieldOfSortKey, SORT_KEYS, SORT_LABELS, sortKeyOfField, type SortKey } from '../../../utils/librarySort';

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none disabled:cursor-not-allowed disabled:opacity-50';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';

const ServerDefault = 'server';

interface SortForm {
    sortBy: SortKey | typeof ServerDefault;
    descending: boolean;
}

const formFrom = (sort: LibrarySortVM): SortForm => {
    const key = sort.sort ? sortKeyOfField(sort.sort) : null;
    return key ? { sortBy: key, descending: sort.descending } : { sortBy: ServerDefault, descending: false };
};

const pickedSort = (value: string): SortForm['sortBy'] =>
    SORT_KEYS.find(key => key === value) ?? ServerDefault;

export default function LibrarySortSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [form, setForm] = useState<SortForm | null>(null);
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        let cancelled = false;
        profileService.getMyLibrarySort(serverId)
            .then(sort => { if (!cancelled) setForm(formFrom(sort)); })
            .catch(err => console.error('Failed to load the library sort order', err));
        return () => { cancelled = true; };
    }, [serverId]);

    if (!form) {
        return (
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Library sort order</h2>
                <div className="vora-skeleton mt-4 h-16" />
            </section>
        );
    }

    const update = (patch: Partial<SortForm>) => {
        setForm({ ...form, ...patch });
        setDirty(true);
    };

    const save = async () => {
        setSaving(true);
        try {
            const saved = await profileService.saveMyLibrarySort({
                sort: form.sortBy === ServerDefault ? null : fieldOfSortKey(form.sortBy),
                descending: form.sortBy !== ServerDefault && form.descending,
            }, serverId);
            setForm(formFrom(saved));
            setDirty(false);
        } catch (err) {
            await dialog.alert(errorDetail(err, 'Could not save your library sort order'));
        } finally {
            setSaving(false);
        }
    };

    const usesServerDefault = form.sortBy === ServerDefault;

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Library sort order</h2>
            <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                How your movie, TV and home video libraries are sorted when you open them, on every device. Sorting a library from its own page changes just that library, until you change this again.
            </p>
            <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
                <div>
                    <label htmlFor="library-sort-by" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Sort by</label>
                    <select id="library-sort-by" value={form.sortBy} onChange={e => update({ sortBy: pickedSort(e.target.value) })} className={selectClass} style={selectStyle}>
                        <option value={ServerDefault}>Server default (titles A to Z, home videos newest first)</option>
                        {SORT_KEYS.map(key => <option key={key} value={key}>{SORT_LABELS[key]}</option>)}
                    </select>
                </div>
                <div>
                    <label htmlFor="library-sort-order" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Order</label>
                    <select
                        id="library-sort-order"
                        value={!usesServerDefault && form.descending ? 'desc' : 'asc'}
                        onChange={e => update({ descending: e.target.value === 'desc' })}
                        disabled={usesServerDefault}
                        className={selectClass}
                        style={selectStyle}
                    >
                        <option value="asc">Ascending</option>
                        <option value="desc">Descending</option>
                    </select>
                </div>
            </div>
            <div className="mt-5 flex justify-end">
                <button type="button" onClick={save} disabled={saving || !dirty} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : 'Save sort order'}
                </button>
            </div>
        </section>
    );
}
