import { useState } from 'react';
import {
    subtitleSearchService,
    defaultSubtitleLanguage,
    SUBTITLE_LANGUAGES,
    type SubtitleSearchResult,
} from '../../../api/Media/subtitleSearchService';

interface FindSubtitlesPanelProps {
    mediaItemId: string;
    serverId?: string;
    onBack: () => void;
    onDownloaded: (subtitleTrackId: string) => void | Promise<void>;
}

const selectStyle: React.CSSProperties = {
    background: 'var(--vora-bg-sunken)',
    border: '1px solid var(--vora-border-subtle)',
    color: 'var(--vora-text-primary)',
};
const labelStyle: React.CSSProperties = { color: 'var(--vora-text-muted)' };
const badgeStyle: React.CSSProperties = {
    background: 'var(--vora-bg-sunken)',
    border: '1px solid var(--vora-border-subtle)',
    color: 'var(--vora-text-muted)',
};

export default function FindSubtitlesPanel({ mediaItemId, serverId, onBack, onDownloaded }: FindSubtitlesPanelProps) {
    const [language, setLanguage] = useState(defaultSubtitleLanguage);
    const [results, setResults] = useState<SubtitleSearchResult[] | null>(null);
    const [isSearching, setIsSearching] = useState(false);
    const [downloadingId, setDownloadingId] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const search = async () => {
        setIsSearching(true);
        setError(null);
        try {
            setResults(await subtitleSearchService.search(mediaItemId, [language], serverId));
        } catch {
            setError('The subtitle search failed. Check the provider settings and try again.');
            setResults(null);
        } finally {
            setIsSearching(false);
        }
    };

    const download = async (result: SubtitleSearchResult) => {
        setDownloadingId(result.providerFileId);
        setError(null);
        try {
            const track = await subtitleSearchService.download(mediaItemId, result.providerFileId, result.language, serverId);
            await onDownloaded(track.id);
        } catch {
            setError('That subtitle could not be downloaded. The provider may have hit its daily limit.');
        } finally {
            setDownloadingId(null);
        }
    };

    return (
        <div className="space-y-5">
            <div className="flex items-end gap-2">
                <div className="flex-1">
                    <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={labelStyle}>Language</label>
                    <select
                        value={language}
                        onChange={e => { setLanguage(e.target.value); setResults(null); }}
                        className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none"
                        style={selectStyle}
                    >
                        {SUBTITLE_LANGUAGES.map(l => (
                            <option key={l.code} value={l.code}>{l.label}</option>
                        ))}
                    </select>
                </div>
                <button
                    type="button"
                    onClick={search}
                    disabled={isSearching}
                    className="vora-button-primary cursor-pointer disabled:cursor-default disabled:opacity-60"
                >
                    {isSearching ? 'Searching…' : 'Search'}
                </button>
            </div>

            {error && (
                <p className="m-0 text-sm" style={{ color: 'var(--vora-danger-500)' }}>{error}</p>
            )}

            <div className="max-h-72 overflow-y-auto pr-1">
                {isSearching && (
                    <p className="m-0 py-8 text-center text-sm" style={labelStyle}>Searching…</p>
                )}

                {!isSearching && results?.length === 0 && (
                    <p className="m-0 py-8 text-center text-sm" style={labelStyle}>No subtitles found for this language.</p>
                )}

                {!isSearching && results === null && !error && (
                    <p className="m-0 py-8 text-center text-sm" style={labelStyle}>Pick a language and search.</p>
                )}

                {!isSearching && results && results.length > 0 && (
                    <ul className="m-0 list-none space-y-2 p-0">
                        {results.map(result => (
                            <li
                                key={`${result.providerId}-${result.providerFileId}`}
                                className="flex items-center gap-3 rounded-md p-3"
                                style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                            >
                                <div className="min-w-0 flex-1">
                                    <p className="m-0 truncate text-sm" style={{ color: 'var(--vora-text-primary)' }} title={result.releaseName}>
                                        {result.releaseName}
                                    </p>
                                    <div className="mt-1 flex flex-wrap items-center gap-1.5">
                                        {result.language && (
                                            <span className="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase" style={badgeStyle}>{result.language}</span>
                                        )}
                                        {result.format && (
                                            <span className="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase" style={badgeStyle}>{result.format}</span>
                                        )}
                                        {result.forced && (
                                            <span className="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase" style={badgeStyle}>Forced</span>
                                        )}
                                        {result.hearingImpaired && (
                                            <span className="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase" style={badgeStyle}>SDH</span>
                                        )}
                                        {result.downloadCount != null && (
                                            <span className="text-[11px]" style={labelStyle}>{result.downloadCount.toLocaleString()} downloads</span>
                                        )}
                                    </div>
                                </div>
                                <button
                                    type="button"
                                    onClick={() => download(result)}
                                    disabled={downloadingId !== null}
                                    className="vora-button-secondary shrink-0 cursor-pointer disabled:cursor-default disabled:opacity-60"
                                >
                                    {downloadingId === result.providerFileId ? 'Adding…' : 'Download'}
                                </button>
                            </li>
                        ))}
                    </ul>
                )}
            </div>

            <div className="mt-7 flex justify-end border-t pt-5" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                <button type="button" onClick={onBack} className="vora-button-secondary cursor-pointer">Back</button>
            </div>
        </div>
    );
}
