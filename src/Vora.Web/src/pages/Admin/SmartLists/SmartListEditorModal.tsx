import { useState, type ReactNode } from 'react';
import type { CollectionSummary } from '../../../api/Collections/collectionService';
import type { LibrarySummary } from '../../../api/Media/libraryService';
import { Modal } from '../../../components/Common/Modal';
import {
    DECADES,
    MAX_ITEMS,
    MEDIA_TYPES,
    MIN_ITEMS,
    MONTHS,
    SOURCE_OPTIONS,
    daysInMonth,
    sortOptionsFor,
    withSource,
    type SmartListForm,
} from './smartListForm';

interface SmartListEditorModalProps {
    isOpen: boolean;
    isEditing: boolean;
    initial: SmartListForm;
    collections: CollectionSummary[];
    libraries: LibrarySummary[];
    saving: boolean;
    onClose: () => void;
    onSave: (form: SmartListForm) => void;
}

function FieldLabel({ htmlFor, children }: { htmlFor?: string; children: ReactNode }) {
    return <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">{children}</label>;
}

function Check({ id, checked, onChange, children }: { id: string; checked: boolean; onChange: (checked: boolean) => void; children: ReactNode }) {
    return (
        <label htmlFor={id} className="flex cursor-pointer select-none items-center gap-3">
            <input id={id} type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
            <span className="text-sm font-medium text-[var(--vora-text-primary)]">{children}</span>
        </label>
    );
}

function MonthDay({ idPrefix, month, day, onChange }: { idPrefix: string; month: number; day: number; onChange: (month: number, day: number) => void }) {
    return (
        <div className="flex gap-2">
            <select id={`${idPrefix}-month`} aria-label="Month" value={month} onChange={e => { const m = Number(e.target.value); onChange(m, Math.min(day, daysInMonth(m))); }} className="vora-input cursor-pointer">
                {MONTHS.map((name, i) => <option key={name} value={i + 1}>{name}</option>)}
            </select>
            <select id={`${idPrefix}-day`} aria-label="Day" value={day} onChange={e => onChange(month, Number(e.target.value))} className="vora-input w-24 cursor-pointer">
                {Array.from({ length: daysInMonth(month) }, (_, i) => i + 1).map(d => <option key={d} value={d}>{d}</option>)}
            </select>
        </div>
    );
}

export default function SmartListEditorModal({ isOpen, isEditing, initial, collections, libraries, saving, onClose, onSave }: SmartListEditorModalProps) {
    const [form, setForm] = useState<SmartListForm>(initial);
    const update = (patch: Partial<SmartListForm>) => setForm(prev => ({ ...prev, ...patch }));

    const isLibrary = form.source === 'Library';
    const usesRules = isLibrary && form.mode === 'rules';
    const sourceInfo = SOURCE_OPTIONS.find(o => o.value === form.source);
    const libraryChoices = form.source === 'RecentlyAddedMusic'
        ? libraries.filter(l => l.type === 'Music')
        : libraries.filter(l => l.type !== 'Music' && l.type !== 'LiveTv');

    const toggleMediaType = (type: string) =>
        update({ mediaTypes: form.mediaTypes.includes(type) ? form.mediaTypes.filter(t => t !== type) : [...form.mediaTypes, type] });

    const submit = (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!saving) onSave(form);
    };

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="xl" surface="light">
            <form onSubmit={submit} className="flex max-h-[85vh] flex-col">
                <div className="space-y-4 overflow-y-auto p-6">
                    <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">{isEditing ? 'Edit smart list' : 'Create smart list'}</h2>

                    <div>
                        <FieldLabel htmlFor="smartlist-title">Row title</FieldLabel>
                        <input id="smartlist-title" required type="text" value={form.title} onChange={e => update({ title: e.target.value })} className="vora-input" />
                    </div>

                    <div>
                        <FieldLabel htmlFor="smartlist-source">What the row shows</FieldLabel>
                        <select id="smartlist-source" value={form.source} onChange={e => setForm(prev => withSource(prev, e.target.value as SmartListForm['source']))} className="vora-input cursor-pointer">
                            {SOURCE_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                        </select>
                        {sourceInfo && <p className="mt-1.5 text-[13px] text-[var(--vora-text-muted)]">{sourceInfo.description}{!isLibrary && ' A profile with nothing to show here does not see the row.'}</p>}
                    </div>

                    {isLibrary && (
                        <div role="radiogroup" aria-label="Pick titles by" className="grid grid-cols-2 gap-1 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-1">
                            {(['rules', 'collection'] as const).map(mode => (
                                <button
                                    key={mode}
                                    type="button"
                                    role="radio"
                                    aria-checked={form.mode === mode}
                                    onClick={() => update({ mode })}
                                    className={`cursor-pointer rounded-[var(--vora-radius-sm)] py-1.5 text-sm font-semibold transition-colors ${form.mode === mode ? 'bg-[var(--vora-accent-500)] text-[var(--vora-accent-contrast)]' : 'text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)]'}`}
                                >
                                    {mode === 'rules' ? 'Rule-based' : 'Collection-based'}
                                </button>
                            ))}
                        </div>
                    )}

                    {isLibrary && form.mode === 'collection' && (
                        <div>
                            <FieldLabel htmlFor="smartlist-collection">Source collection</FieldLabel>
                            <select id="smartlist-collection" required value={form.collectionId} onChange={e => update({ collectionId: e.target.value })} className="vora-input cursor-pointer">
                                <option value="">Select a collection…</option>
                                {collections.map(c => <option key={c.id} value={c.id}>{c.title}</option>)}
                            </select>
                        </div>
                    )}

                    {usesRules && (
                        <>
                            <fieldset>
                                <legend className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Media types</legend>
                                <div className="flex flex-wrap gap-x-4 gap-y-2">
                                    {MEDIA_TYPES.map(type => (
                                        <Check key={type.value} id={`smartlist-type-${type.value}`} checked={form.mediaTypes.includes(type.value)} onChange={() => toggleMediaType(type.value)}>{type.label}</Check>
                                    ))}
                                </div>
                                <p className="mt-1.5 text-[13px] text-[var(--vora-text-muted)]">None ticked means movies and shows.</p>
                            </fieldset>
                            <div className="grid gap-4 sm:grid-cols-2">
                                <div>
                                    <FieldLabel htmlFor="smartlist-decade">Decade</FieldLabel>
                                    <select id="smartlist-decade" value={form.decade} onChange={e => update({ decade: e.target.value })} className="vora-input cursor-pointer">
                                        <option value="">Any</option>
                                        {DECADES.map(d => <option key={d} value={String(d)}>{d}s</option>)}
                                    </select>
                                </div>
                                <div className="flex items-end pb-2">
                                    <Check id="smartlist-unwatched" checked={form.unwatchedOnly} onChange={unwatchedOnly => update({ unwatchedOnly })}>Only what the profile hasn't watched</Check>
                                </div>
                            </div>
                        </>
                    )}

                    {(usesRules || form.source === 'RecentlyAddedMusic') && (
                        <div>
                            <FieldLabel htmlFor="smartlist-library">Library</FieldLabel>
                            <select id="smartlist-library" value={form.libraryId} onChange={e => update({ libraryId: e.target.value })} className="vora-input cursor-pointer">
                                <option value="">{form.source === 'RecentlyAddedMusic' ? 'All music libraries' : 'All libraries'}</option>
                                {libraryChoices.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}
                            </select>
                        </div>
                    )}

                    {form.source === 'NewPodcastEpisodes' && (
                        <Check id="smartlist-unplayed" checked={form.unwatchedOnly} onChange={unwatchedOnly => update({ unwatchedOnly })}>Only episodes the profile hasn't finished</Check>
                    )}

                    {(form.source === 'NewPodcastEpisodes' || form.source === 'RecentRecordings') && (
                        <div>
                            <FieldLabel htmlFor="smartlist-days">{form.source === 'NewPodcastEpisodes' ? 'Published in the last' : 'Recorded in the last'}</FieldLabel>
                            <div className="flex items-center gap-2">
                                <input id="smartlist-days" type="number" min={1} max={365} placeholder="Any" value={form.days} onChange={e => update({ days: e.target.value })} className="vora-input w-28" />
                                <span className="text-sm text-[var(--vora-text-secondary)]">days</span>
                            </div>
                        </div>
                    )}

                    <div className="grid gap-4 sm:grid-cols-2">
                        <div>
                            <FieldLabel htmlFor="smartlist-sort">Sort by</FieldLabel>
                            <select id="smartlist-sort" value={form.sortBy} onChange={e => update({ sortBy: e.target.value as SmartListForm['sortBy'] })} className="vora-input cursor-pointer">
                                {sortOptionsFor(form.source).map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                            </select>
                        </div>
                        <div>
                            <FieldLabel htmlFor="smartlist-max">Max items</FieldLabel>
                            <input id="smartlist-max" type="number" min={MIN_ITEMS} max={MAX_ITEMS} value={form.maxItems} onChange={e => update({ maxItems: Number(e.target.value) })} className="vora-input" />
                        </div>
                    </div>

                    <div className="space-y-3 border-t border-[var(--vora-border-subtle)] pt-4">
                        <Check id="smartlist-seasonal" checked={form.seasonal} onChange={seasonal => update({ seasonal })}>Only show during part of the year</Check>
                        {form.seasonal && (
                            <div className="grid gap-3 sm:grid-cols-2">
                                <div>
                                    <FieldLabel>From</FieldLabel>
                                    <MonthDay idPrefix="smartlist-start" month={form.startMonth} day={form.startDay} onChange={(startMonth, startDay) => update({ startMonth, startDay })} />
                                </div>
                                <div>
                                    <FieldLabel>Until</FieldLabel>
                                    <MonthDay idPrefix="smartlist-end" month={form.endMonth} day={form.endDay} onChange={(endMonth, endDay) => update({ endMonth, endDay })} />
                                </div>
                            </div>
                        )}
                    </div>

                    <div className="space-y-2 border-t border-[var(--vora-border-subtle)] pt-4">
                        <Check id="smartlist-home" checked={form.showOnHomepage} onChange={showOnHomepage => update({ showOnHomepage })}>Show on the home screen</Check>
                        <Check id="smartlist-friends" checked={form.showToFriends} onChange={showToFriends => update({ showToFriends })}>Show to users who aren't admins</Check>
                    </div>
                </div>

                <div className="flex justify-end gap-3 border-t border-[var(--vora-border-subtle)] px-6 py-4">
                    <button type="button" onClick={onClose} className="vora-button-secondary">Cancel</button>
                    <button type="submit" disabled={saving} className="vora-button-primary disabled:cursor-not-allowed disabled:opacity-70">{saving ? 'Saving…' : 'Save list'}</button>
                </div>
            </form>
        </Modal>
    );
}
