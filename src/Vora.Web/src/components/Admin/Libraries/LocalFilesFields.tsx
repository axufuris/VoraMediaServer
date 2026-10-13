import { LOCAL_METADATA_MODES } from './localMetadataModes';

interface LocalFilesFieldsProps {
    mode: number;
    preferLocalArtwork: boolean;
    onModeChange: (mode: number) => void;
    onPreferLocalArtworkChange: (prefer: boolean) => void;
}

// Both switches are off for a new library and for every library that existed
// before they did, so nothing is read from the media folders until asked.
export default function LocalFilesFields({ mode, preferLocalArtwork, onModeChange, onPreferLocalArtworkChange }: LocalFilesFieldsProps) {
    const selected = LOCAL_METADATA_MODES.find(option => option.value === mode) ?? LOCAL_METADATA_MODES[0];

    return (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
                <label htmlFor="local-metadata-mode" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Read NFO files</label>
                <select
                    id="local-metadata-mode"
                    value={selected.value}
                    onChange={e => onModeChange(Number(e.target.value))}
                    className="vora-input cursor-pointer"
                >
                    {LOCAL_METADATA_MODES.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
                <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">
                    {selected.help}
                    {selected.value !== 0 && ' A match you set with Fix match is never changed by an NFO.'}
                </p>
            </div>
            <div>
                <span className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Local artwork</span>
                <label className="flex items-center gap-3 cursor-pointer select-none py-2">
                    <input
                        type="checkbox"
                        checked={preferLocalArtwork}
                        onChange={e => onPreferLocalArtworkChange(e.target.checked)}
                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                    />
                    <span className="text-sm text-[var(--vora-text-primary)]">Prefer local artwork</span>
                </label>
                <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">
                    Use the poster, fanart and logo saved beside a title before the artwork provider&rsquo;s. Vora reads them where they are and does not copy them. Every other image stays available in the artwork picker.
                </p>
            </div>
        </div>
    );
}
