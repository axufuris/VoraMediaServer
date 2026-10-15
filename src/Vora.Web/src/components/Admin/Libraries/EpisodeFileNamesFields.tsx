import { useState } from 'react';
import { libraryAdminService, type EpisodeNameOutcome, type EpisodeNamePreviewFileVM, type EpisodeNamePreviewVM } from '../../../api/Media/libraryAdminService';
import { errorDetail } from '../../../utils/apiError';

interface EpisodeFileNamesFieldsProps {
    folderPaths: string[];
    excludeFilters: string[];
    scannerRegex?: string;
    onScannerRegexChange: (pattern: string | undefined) => void;
    serverId?: string;
}

const HOW: Record<EpisodeNameOutcome, string> = {
    Name: 'From the name',
    Document: 'From the .nfo',
    Guess: 'Guessed',
    Unreadable: "Can't place",
};

const plural = (count: number, one: string, many: string) => `${count.toLocaleString()} ${count === 1 ? one : many}`;

function episodeLabel(file: EpisodeNamePreviewFileVM): string {
    if (file.season == null || file.episode == null) return '—';
    if (file.episode > 9999) {
        const day = String(file.episode);
        return `S${file.season} · ${day.slice(0, 4)}-${day.slice(4, 6)}-${day.slice(6, 8)}`;
    }
    const pad = (n: number) => String(n).padStart(2, '0');
    const range = file.endEpisode != null && file.endEpisode > file.episode ? `-E${pad(file.endEpisode)}` : '';
    return `S${pad(file.season)}E${pad(file.episode)}${range}`;
}

function summary(preview: EpisodeNamePreviewVM): string {
    const parts = [
        `${plural(preview.readFromName, 'read from its name', 'read from their names')}`,
        preview.readFromDocument > 0 ? `${preview.readFromDocument.toLocaleString()} from the .nfo beside ${preview.readFromDocument === 1 ? 'it' : 'them'}` : null,
        preview.guessed > 0 ? `${preview.guessed.toLocaleString()} guessed` : null,
        preview.unreadable > 0 ? `${preview.unreadable.toLocaleString()} Vora can't place` : null,
    ].filter(Boolean);
    return `Checked ${plural(preview.filesFound, 'file', 'files')}: ${parts.join(', ')}.`;
}

export default function EpisodeFileNamesFields({ folderPaths, excludeFilters, scannerRegex, onScannerRegexChange, serverId }: EpisodeFileNamesFieldsProps) {
    const custom = (scannerRegex ?? '').trim().length > 0;
    const [draft, setDraft] = useState(scannerRegex ?? '');
    const [usingCustom, setUsingCustom] = useState(custom);
    const [preview, setPreview] = useState<EpisodeNamePreviewVM | null>(null);
    const [checkedPattern, setCheckedPattern] = useState<string | null>(null);
    const [checking, setChecking] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [onlyUnplaced, setOnlyUnplaced] = useState(false);

    const pattern = usingCustom ? draft.trim() : '';
    const stale = preview !== null && checkedPattern !== pattern;

    const chooseAutomatic = () => {
        setUsingCustom(false);
        onScannerRegexChange(undefined);
    };

    const chooseCustom = () => {
        setUsingCustom(true);
        onScannerRegexChange(draft.trim() || undefined);
    };

    const editPattern = (value: string) => {
        setDraft(value);
        onScannerRegexChange(value.trim() || undefined);
    };

    const check = async () => {
        setChecking(true);
        setError(null);
        try {
            const result = await libraryAdminService.previewEpisodeFileNames({ folderPaths, scannerRegex: pattern || null, excludeFilters }, serverId);
            setPreview(result);
            setCheckedPattern(pattern);
            setOnlyUnplaced(result.unreadable > 0);
        } catch (err) {
            setError(errorDetail(err, "Couldn't check the files"));
        } finally {
            setChecking(false);
        }
    };

    const shown = preview ? preview.files.filter(file => !onlyUnplaced || file.outcome === 'Unreadable') : [];
    const automaticHelps = preview != null && checkedPattern
        && ((preview.automaticUnreadable ?? preview.unreadable) < preview.unreadable || (preview.automaticReadsDifferently ?? 0) > 0);

    return (
        <div className="space-y-4">
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2" role="radiogroup" aria-label="Episode file names">
                <label className={'flex cursor-pointer gap-3 rounded-[var(--vora-radius-md)] border p-3 ' + (usingCustom ? 'border-[var(--vora-border-subtle)]' : 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)]')}>
                    <input type="radio" name="episode-file-names" checked={!usingCustom} onChange={chooseAutomatic} className="mt-1 h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                    <span>
                        <span className="block text-sm font-semibold text-[var(--vora-text-primary)]">Automatic (recommended)</span>
                        <span className="mt-0.5 block text-xs text-[var(--vora-text-muted)]">
                            Reads Sonarr, Plex, Kodi and scene names: <span className="font-mono">S01E01</span>, <span className="font-mono">1x01</span>, <span className="font-mono">Season 1 Episode 1</span>, multi-episode ranges, air dates and absolute numbers. A name with no episode in it is placed from the .nfo beside it and its Season folder.
                        </span>
                    </span>
                </label>
                <label className={'flex cursor-pointer gap-3 rounded-[var(--vora-radius-md)] border p-3 ' + (usingCustom ? 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)]' : 'border-[var(--vora-border-subtle)]')}>
                    <input type="radio" name="episode-file-names" checked={usingCustom} onChange={chooseCustom} className="mt-1 h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                    <span>
                        <span className="block text-sm font-semibold text-[var(--vora-text-primary)]">Custom pattern</span>
                        <span className="mt-0.5 block text-xs text-[var(--vora-text-muted)]">
                            Your own regular expression, tried before the automatic reading. Named groups: <span className="font-mono">Season</span>, <span className="font-mono">Episode</span>, <span className="font-mono">EndEpisode</span>, <span className="font-mono">AirDate</span>, <span className="font-mono">EpisodeTitle</span>.
                        </span>
                    </span>
                </label>
            </div>

            {usingCustom && (
                <div className="space-y-1.5">
                    <label htmlFor="episode-file-name-pattern" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Pattern</label>
                    <textarea
                        id="episode-file-name-pattern"
                        value={draft}
                        onChange={e => editPattern(e.target.value)}
                        rows={3}
                        spellCheck={false}
                        placeholder="(?<Season>\d+)x(?<Episode>\d+) - (?<EpisodeTitle>.*)"
                        className="vora-input w-full font-mono text-xs"
                    />
                    <p className="text-xs text-[var(--vora-text-muted)]">A name your pattern doesn't read still goes through the automatic reading, so nothing is lost by trying one.</p>
                </div>
            )}

            <div className="flex flex-wrap items-center gap-3">
                <button type="button" onClick={check} disabled={checking || folderPaths.length === 0} className="vora-button-secondary disabled:cursor-not-allowed disabled:opacity-50">
                    {checking ? 'Checking…' : preview ? 'Check again' : 'Check my files'}
                </button>
                <span className="text-xs text-[var(--vora-text-muted)]">
                    {folderPaths.length === 0 ? 'Add a folder first.' : 'Lists the episode files in this library and which episode Vora reads each one as. Nothing is changed.'}
                </span>
            </div>

            {error && <p className="text-sm text-[var(--vora-danger-text)]">{error}</p>}

            {preview && (
                <div className={'space-y-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-4' + (stale ? ' opacity-60' : '')}>
                    {stale && <p className="text-xs font-semibold text-[var(--vora-warning-text)]">The pattern changed since this check. Check again to see what it does.</p>}
                    {preview.patternProblem && (
                        <p className="text-sm text-[var(--vora-danger-text)]">{preview.patternProblem} Until it's fixed, files are read automatically.</p>
                    )}
                    <p className="text-sm text-[var(--vora-text-primary)]">{summary(preview)}</p>
                    {preview.moreFilesThanChecked && <p className="text-xs text-[var(--vora-text-muted)]">Only the first 50,000 files were checked.</p>}
                    {preview.unreadable > 0 && (
                        <p className="text-xs text-[var(--vora-text-muted)]">
                            A file Vora can't place isn't added to the library. Rename it with the season and episode (<span className="font-mono">S01E05</span> or <span className="font-mono">1x05</span>), or put an .nfo with <span className="font-mono">&lt;season&gt;</span> and <span className="font-mono">&lt;episode&gt;</span> beside it.
                        </p>
                    )}
                    {automaticHelps && (
                        <div className="flex flex-wrap items-center gap-3 text-sm text-[var(--vora-text-primary)]">
                            <span>
                                Automatic would leave out {plural(preview.automaticUnreadable ?? 0, 'file', 'files')}
                                {(preview.automaticReadsDifferently ?? 0) > 0 && ` and read ${plural(preview.automaticReadsDifferently ?? 0, 'file', 'files')} as a different episode`}.
                            </span>
                            <button type="button" onClick={chooseAutomatic} className="vora-button-secondary">Use Automatic</button>
                        </div>
                    )}
                    {preview.unreadable > 0 && (
                        <label className="flex w-fit cursor-pointer items-center gap-2 text-xs text-[var(--vora-text-secondary)]">
                            <input type="checkbox" checked={onlyUnplaced} onChange={e => setOnlyUnplaced(e.target.checked)} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                            Only files Vora can't place
                        </label>
                    )}
                    {shown.length > 0 && (
                        <div className="max-h-80 overflow-auto rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                            <table className="w-full text-left text-xs">
                                <thead className="sticky top-0 bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)]">
                                    <tr>
                                        <th className="px-3 py-2 font-semibold">File</th>
                                        <th className="px-3 py-2 font-semibold">Episode</th>
                                        <th className="px-3 py-2 font-semibold">Title</th>
                                        <th className="px-3 py-2 font-semibold">How</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {shown.map(file => (
                                        <tr key={file.path} className="border-t border-[var(--vora-border-subtle)]">
                                            <td className="break-all px-3 py-1.5 font-mono text-[var(--vora-text-secondary)]">{file.path}</td>
                                            <td className="whitespace-nowrap px-3 py-1.5 text-[var(--vora-text-primary)]">{episodeLabel(file)}</td>
                                            <td className="px-3 py-1.5 text-[var(--vora-text-secondary)]">{file.title ?? ''}</td>
                                            <td className={'whitespace-nowrap px-3 py-1.5 ' + (file.outcome === 'Unreadable' ? 'font-semibold text-[var(--vora-danger-text)]' : 'text-[var(--vora-text-muted)]')}>{HOW[file.outcome]}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                    {preview.files.length < preview.filesFound && (
                        <p className="text-xs text-[var(--vora-text-muted)]">Showing {preview.files.length.toLocaleString()} of {preview.filesFound.toLocaleString()}, the ones Vora can't place first.</p>
                    )}
                </div>
            )}
        </div>
    );
}
