import { useEffect, useState, type FormEvent } from 'react';
import { Modal } from '../Common/Modal';
import ArtImage from '../Client/Primitives/ArtImage';
import { libraryAdminService, type MediaMatchCandidate, type MediaMatchResult } from '../../api/Media/libraryAdminService';
import { errorDetail } from '../../utils/apiError';

// Re-identifies a movie or show whose folder carried no usable external id, so
// metadata can find it. Search results come from the library's own metadata
// provider; pasting an IMDb or TMDB link looks that title up directly.
interface FixMatchModalProps {
    mediaItemId: string;
    mediaType: 'Movie' | 'TvShow';
    currentYear?: number;
    serverId?: string;
    onClose: () => void;
    onMatched: (result: MediaMatchResult) => void;
}

interface SearchRequest {
    query: string;
    year: string;
    attempt: number;
}

interface SearchResponse {
    key: string;
    candidates: MediaMatchCandidate[];
    error: string | null;
}

const candidateKey = (candidate: MediaMatchCandidate) => `${candidate.source}:${candidate.externalId}`;

const parseYear = (value: string): number | undefined => {
    const year = Number.parseInt(value, 10);
    return Number.isInteger(year) && year > 1800 && year < 3000 ? year : undefined;
};

export default function FixMatchModal({ mediaItemId, mediaType, currentYear, serverId, onClose, onMatched }: FixMatchModalProps) {
    const [query, setQuery] = useState('');
    const [year, setYear] = useState(currentYear ? String(currentYear) : '');
    const [request, setRequest] = useState<SearchRequest>({ query: '', year: currentYear ? String(currentYear) : '', attempt: 0 });
    const [response, setResponse] = useState<SearchResponse | null>(null);
    const [selectedKey, setSelectedKey] = useState<string | null>(null);
    const [applying, setApplying] = useState(false);
    const [applyError, setApplyError] = useState<string | null>(null);

    const requestKey = `${request.query}|${request.year}|${request.attempt}`;
    const current = response?.key === requestKey ? response : null;
    const searching = current === null;
    const candidates = current?.candidates ?? [];
    const selected = candidates.find(c => candidateKey(c) === selectedKey);
    const kindLabel = mediaType === 'TvShow' ? 'show' : 'movie';

    useEffect(() => {
        let cancelled = false;
        libraryAdminService.searchMatchCandidates(mediaItemId, request.query, parseYear(request.year), serverId)
            .then(results => { if (!cancelled) setResponse({ key: requestKey, candidates: results, error: null }); })
            .catch(err => { if (!cancelled) setResponse({ key: requestKey, candidates: [], error: errorDetail(err, 'Search failed') }); });
        return () => { cancelled = true; };
    }, [mediaItemId, serverId, request.query, request.year, requestKey]);

    const submitSearch = (e: FormEvent) => {
        e.preventDefault();
        setSelectedKey(null);
        setRequest(prev => ({ query, year, attempt: prev.attempt + 1 }));
    };

    const applyMatch = async () => {
        if (!selected) return;
        setApplying(true);
        setApplyError(null);
        try {
            const result = await libraryAdminService.applyMatch(mediaItemId, selected.source, selected.externalId, serverId);
            onMatched(result);
        } catch (err) {
            setApplyError(errorDetail(err, 'Could not apply the match'));
            setApplying(false);
        }
    };

    return (
        <Modal isOpen onClose={applying ? () => { } : onClose} size="2xl" surface="gray-900" cardClassName="flex max-h-[85vh] flex-col overflow-hidden">
            <div className="flex min-h-0 flex-1 flex-col">
                <div className="shrink-0 border-b p-6" style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-bg-canvas)' }}>
                    <h2 className="m-0 text-2xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Fix match</h2>
                    <p className="mt-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                        Pick the {kindLabel} this really is. Vora stores its id and refreshes the metadata. Search by title, or paste an IMDb or TMDB link.
                    </p>

                    <form onSubmit={submitSearch} className="mt-4 flex flex-col gap-2 sm:flex-row">
                        <input
                            type="search"
                            value={query}
                            onChange={e => setQuery(e.target.value)}
                            placeholder="Title, tt1234567, or a link — blank uses this item's title"
                            aria-label="Search title or id"
                            className="vora-input min-w-0 flex-1 rounded-lg px-3 py-2 text-sm"
                        />
                        <input
                            type="number"
                            inputMode="numeric"
                            value={year}
                            onChange={e => setYear(e.target.value)}
                            placeholder="Year"
                            aria-label="Year"
                            className="vora-input w-full rounded-lg px-3 py-2 text-sm sm:w-24"
                        />
                        <button type="submit" className="vora-button-secondary rounded-lg px-4 py-2 text-sm font-semibold">Search</button>
                    </form>
                </div>

                <div className="min-h-0 flex-1 overflow-y-auto p-4" aria-busy={searching}>
                    {searching ? (
                        <p className="py-12 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>Searching…</p>
                    ) : current?.error ? (
                        <p className="py-12 text-center text-sm" role="alert" style={{ color: 'var(--vora-danger-text)' }}>{current.error}</p>
                    ) : candidates.length === 0 ? (
                        <p className="py-12 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                            No matches found. Try a different title, clear the year, or paste an IMDb or TMDB link.
                        </p>
                    ) : (
                        <ul className="m-0 list-none space-y-2 p-0" aria-label="Matches">
                            {candidates.map(candidate => {
                                const key = candidateKey(candidate);
                                const isSelected = key === selectedKey;
                                return (
                                    <li key={key}>
                                        <button
                                            type="button"
                                            aria-pressed={isSelected}
                                            onClick={() => setSelectedKey(key)}
                                            className="vora-row-interactive flex w-full cursor-pointer items-start gap-4 rounded-lg border p-3 text-left"
                                            style={{
                                                background: isSelected ? 'var(--vora-accent-soft)' : undefined,
                                                borderColor: isSelected ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)',
                                            }}
                                        >
                                            <div className="h-24 w-16 shrink-0 overflow-hidden rounded">
                                                <ArtImage src={candidate.posterUrl} alt={candidate.title} />
                                            </div>
                                            <div className="min-w-0 flex-1">
                                                <div className="flex flex-wrap items-baseline gap-x-2">
                                                    <span className="font-semibold" style={{ color: isSelected ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}>{candidate.title}</span>
                                                    {candidate.year && <span className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>{candidate.year}</span>}
                                                </div>
                                                <div className="mt-0.5 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                                    {candidate.providerName} · {candidate.source.toUpperCase()} {candidate.externalId}
                                                </div>
                                                {candidate.overview && (
                                                    <p className="mt-1.5 line-clamp-2 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>{candidate.overview}</p>
                                                )}
                                            </div>
                                        </button>
                                    </li>
                                );
                            })}
                        </ul>
                    )}
                </div>

                <div className="flex shrink-0 flex-col gap-3 border-t p-5 sm:flex-row sm:items-center sm:justify-between" style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-bg-canvas)' }}>
                    <p className="m-0 text-sm" role={applyError ? 'alert' : undefined} style={{ color: applyError ? 'var(--vora-danger-text)' : 'var(--vora-text-muted)' }}>
                        {applyError ?? (selected ? `Match to ${selected.title}${selected.year ? ` (${selected.year})` : ''}` : 'Select a match to apply.')}
                    </p>
                    <div className="flex gap-3">
                        <button type="button" onClick={onClose} disabled={applying} className="vora-button-secondary rounded-lg px-5 py-2.5 text-sm font-semibold disabled:opacity-50">Cancel</button>
                        <button type="button" onClick={applyMatch} disabled={!selected || applying} className="vora-button-primary rounded-lg px-5 py-2.5 text-sm font-semibold disabled:cursor-not-allowed disabled:opacity-50">
                            {applying ? 'Applying…' : 'Apply match'}
                        </button>
                    </div>
                </div>
            </div>
        </Modal>
    );
}
