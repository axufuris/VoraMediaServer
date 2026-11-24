import { SORT_KEYS, SORT_LABELS } from '../../../utils/librarySort';
import { ServerDefault, type SortForm } from './clientSettingsForm';

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none disabled:cursor-not-allowed disabled:opacity-50';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';

const pickedSort = (value: string): SortForm['sortBy'] =>
    SORT_KEYS.find(key => key === value) ?? ServerDefault;

interface LibrarySortFieldsProps {
    form: SortForm;
    onChange: (patch: Partial<SortForm>) => void;
}

export default function LibrarySortFields({ form, onChange }: LibrarySortFieldsProps) {
    const usesServerDefault = form.sortBy === ServerDefault;

    return (
        <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
            <div>
                <label htmlFor="library-sort-by" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Sort by</label>
                <select id="library-sort-by" value={form.sortBy} onChange={e => onChange({ sortBy: pickedSort(e.target.value) })} className={selectClass} style={selectStyle}>
                    <option value={ServerDefault}>Server default (titles A to Z, home videos newest first)</option>
                    {SORT_KEYS.map(key => <option key={key} value={key}>{SORT_LABELS[key]}</option>)}
                </select>
            </div>
            <div>
                <label htmlFor="library-sort-order" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Order</label>
                <select
                    id="library-sort-order"
                    value={!usesServerDefault && form.descending ? 'desc' : 'asc'}
                    onChange={e => onChange({ descending: e.target.value === 'desc' })}
                    disabled={usesServerDefault}
                    className={selectClass}
                    style={selectStyle}
                >
                    <option value="asc">Ascending</option>
                    <option value="desc">Descending</option>
                </select>
            </div>
        </div>
    );
}
