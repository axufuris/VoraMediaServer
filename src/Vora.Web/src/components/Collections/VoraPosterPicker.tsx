import { useState } from 'react';
import type { VoraPosterCatalog } from '../../api/Media/voraPosterService';
import { thumbUrl } from '../../utils/thumbnails';
import { matchesVoraPosterSearch } from '../../utils/voraPosters';

interface VoraPosterPickerProps {
    idPrefix: string;
    catalog: VoraPosterCatalog | null;
    failed: boolean;
    selectedUrl: string;
    suggestedUrl?: string;
    onSelect: (url: string) => void;
}

const ALL = 'all';

export default function VoraPosterPicker({ idPrefix, catalog, failed, selectedUrl, suggestedUrl, onSelect }: VoraPosterPickerProps) {
    const [category, setCategory] = useState(ALL);
    const [search, setSearch] = useState('');

    if (failed) {
        return <p className="py-6 text-sm text-[var(--vora-text-muted)]">Vora posters could not be loaded. Close this and try again in a moment.</p>;
    }
    if (!catalog) {
        return <p className="py-6 text-sm text-[var(--vora-text-muted)]">Loading Vora posters…</p>;
    }

    const shown = catalog.posters
        .filter(poster => (category === ALL || poster.category === category) && matchesVoraPosterSearch(poster, search))
        .sort((a, b) => Number(b.url === suggestedUrl) - Number(a.url === suggestedUrl));

    return (
        <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-2">
                {[{ id: ALL, name: 'All' }, ...catalog.categories].map(entry => (
                    <button
                        key={entry.id}
                        type="button"
                        data-active={category === entry.id}
                        aria-pressed={category === entry.id}
                        onClick={() => setCategory(entry.id)}
                        className="vora-pill cursor-pointer rounded-full px-3 py-1 text-xs font-medium"
                    >
                        {entry.name}
                    </button>
                ))}
                <label htmlFor={`${idPrefix}-search`} className="sr-only">Search Vora posters</label>
                <input
                    id={`${idPrefix}-search`}
                    type="search"
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    placeholder="Search, e.g. Halloween"
                    className="ml-auto w-52 max-md:ml-0 max-md:w-full rounded-md border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-raised)] px-2 py-1.5 text-sm text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]"
                />
            </div>

            <div className="grid gap-3" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(var(--vora-card-min-w-xs), 1fr))' }}>
                {shown.map(poster => {
                    const selected = poster.url === selectedUrl;
                    return (
                        <button
                            key={poster.id}
                            type="button"
                            aria-pressed={selected}
                            aria-label={`${poster.title} poster`}
                            title={selected ? `${poster.title} (click again for no poster)` : poster.title}
                            onClick={() => onSelect(selected ? '' : poster.url)}
                            className={`relative aspect-[2/3] cursor-pointer overflow-hidden rounded-md bg-[var(--vora-bg-canvas)] transition-all ${selected ? 'scale-95 ring-4 ring-[var(--vora-accent-500)]' : 'hover:ring-2 hover:ring-[var(--vora-border-strong)]'}`}
                        >
                            <img src={thumbUrl(poster.url, 360)} alt="" loading="lazy" className="h-full w-full object-cover" />
                            {poster.url === suggestedUrl && !selected && (
                                <span className="absolute left-2 top-2 rounded bg-[var(--vora-accent-500)] px-1.5 py-0.5 text-[10px] font-bold uppercase text-[var(--vora-accent-contrast)] shadow-[var(--vora-shadow-sm)]">Suggested</span>
                            )}
                            {selected && (
                                <span className="absolute right-2 top-2 rounded-full bg-[var(--vora-accent-500)] p-1 text-[var(--vora-accent-contrast)] shadow-[var(--vora-shadow-overlay)]">
                                    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                                </span>
                            )}
                        </button>
                    );
                })}
                {shown.length === 0 && (
                    <p className="col-span-full py-8 text-center text-sm text-[var(--vora-text-muted)]">No Vora posters match “{search}”.</p>
                )}
            </div>
        </div>
    );
}
