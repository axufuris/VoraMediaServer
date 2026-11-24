import type { SubtitleMode } from '../../../api/Users/profileService';
import { METADATA_LANGUAGES } from '../../../utils/serverSettingOptions';
import { Automatic, type TrackForm } from './clientSettingsForm';

const SUBTITLE_MODES: ReadonlyArray<{ mode: SubtitleMode; label: string; detail: string }> = [
    { mode: 'Default', label: 'Default', detail: 'Shows the subtitles the file marks as forced or default.' },
    { mode: 'Smart', label: 'Smart', detail: "Also turns subtitles on when the audio isn't in your subtitle language." },
    { mode: 'ForcedOnly', label: 'Only forced', detail: 'Shows only subtitles marked as forced, such as signs and lines in another language.' },
    { mode: 'Always', label: 'Always', detail: 'Always shows full subtitles in your subtitle language.' },
    { mode: 'Off', label: 'Off', detail: 'Shows no subtitles unless you pick one.' },
];

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';
const hintClass = 'mb-0 mt-2 text-xs';

function languageName(code: string): string {
    return METADATA_LANGUAGES.find(([value]) => value === code)?.[1] ?? code;
}

function modeEntry(mode: SubtitleMode) {
    return SUBTITLE_MODES.find(entry => entry.mode === mode) ?? SUBTITLE_MODES[0];
}

function pickedMode(value: string): SubtitleMode | typeof Automatic {
    return SUBTITLE_MODES.find(entry => entry.mode === value)?.mode ?? Automatic;
}

function LanguageOptions({ saved }: { saved: string }) {
    const known = METADATA_LANGUAGES.some(([code]) => code === saved);
    return (
        <>
            {saved !== Automatic && !known && <option value={saved}>{saved}</option>}
            {METADATA_LANGUAGES.map(([code, label]) => <option key={code} value={code}>{label}</option>)}
        </>
    );
}

interface TrackPreferenceFieldsProps {
    form: TrackForm;
    serverLanguage: string;
    serverSubtitleMode: SubtitleMode;
    onChange: (patch: Partial<TrackForm>) => void;
}

export default function TrackPreferenceFields({ form, serverLanguage, serverSubtitleMode, onChange }: TrackPreferenceFieldsProps) {
    const serverMode = modeEntry(serverSubtitleMode);
    const activeMode = form.subtitleMode === Automatic ? serverMode : modeEntry(form.subtitleMode);

    return (
        <>
            <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                <div>
                    <label htmlFor="track-audio-language" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Audio language</label>
                    <select id="track-audio-language" value={form.audioLanguage} onChange={e => onChange({ audioLanguage: e.target.value })} className={selectClass} style={selectStyle}>
                        <option value={Automatic}>Automatic</option>
                        <LanguageOptions saved={form.audioLanguage} />
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>Automatic plays the file's default audio track, and so does a language the file doesn't have.</p>
                </div>
                <div>
                    <label htmlFor="track-subtitle-mode" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Subtitles</label>
                    <select id="track-subtitle-mode" value={form.subtitleMode} onChange={e => onChange({ subtitleMode: pickedMode(e.target.value) })} className={selectClass} style={selectStyle}>
                        <option value={Automatic}>Automatic ({serverMode.label}, set by the server)</option>
                        {SUBTITLE_MODES.map(entry => <option key={entry.mode} value={entry.mode}>{entry.label}</option>)}
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>{activeMode.detail}</p>
                </div>
                <div>
                    <label htmlFor="track-subtitle-language" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Subtitle language</label>
                    <select id="track-subtitle-language" value={form.subtitleLanguage} onChange={e => onChange({ subtitleLanguage: e.target.value })} className={selectClass} style={selectStyle}>
                        <option value={Automatic}>Automatic ({languageName(serverLanguage)})</option>
                        <LanguageOptions saved={form.subtitleLanguage} />
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>Automatic uses the server's language.</p>
                </div>
            </div>
            <label className="mt-5 flex cursor-pointer items-center gap-3">
                <input type="checkbox" checked={form.preferSdh} onChange={e => onChange({ preferSdh: e.target.checked })} className="h-4 w-4 cursor-pointer" />
                <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>Prefer subtitles for the deaf and hard of hearing (SDH)</span>
            </label>
        </>
    );
}
