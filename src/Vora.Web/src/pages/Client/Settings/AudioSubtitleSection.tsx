import { useEffect, useState } from 'react';
import { profileService, type SubtitleMode, type TrackPreferencesVM } from '../../../api/Users/profileService';
import { useDialog } from '../../../dialogs';
import { errorDetail } from '../../../utils/apiError';
import { METADATA_LANGUAGES } from '../../../utils/serverSettingOptions';

const Automatic = '';

const SUBTITLE_MODES: ReadonlyArray<{ mode: SubtitleMode; label: string; detail: string }> = [
    { mode: 'Default', label: 'Default', detail: 'Shows the subtitles the file marks as forced or default.' },
    { mode: 'Smart', label: 'Smart', detail: "Also turns subtitles on when the audio isn't in your subtitle language." },
    { mode: 'ForcedOnly', label: 'Only forced', detail: 'Shows only subtitles marked as forced, such as signs and lines in another language.' },
    { mode: 'Always', label: 'Always', detail: 'Always shows full subtitles in your subtitle language.' },
    { mode: 'Off', label: 'Off', detail: 'Shows no subtitles unless you pick one.' },
];

interface TrackForm {
    audioLanguage: string;
    subtitleLanguage: string;
    subtitleMode: SubtitleMode | typeof Automatic;
    preferSdh: boolean;
}

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';
const hintClass = 'mb-0 mt-2 text-xs';

function formFrom(preferences: TrackPreferencesVM): TrackForm {
    return {
        audioLanguage: preferences.audioLanguage ?? Automatic,
        subtitleLanguage: preferences.subtitleLanguage ?? Automatic,
        subtitleMode: preferences.subtitleMode ?? Automatic,
        preferSdh: preferences.preferHearingImpairedSubtitles,
    };
}

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

export default function AudioSubtitleSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [loaded, setLoaded] = useState<TrackPreferencesVM | null>(null);
    const [form, setForm] = useState<TrackForm | null>(null);
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        let cancelled = false;
        profileService.getMyTrackPreferences(serverId)
            .then(preferences => {
                if (cancelled) return;
                setLoaded(preferences);
                setForm(formFrom(preferences));
            })
            .catch(err => console.error('Failed to load audio and subtitle preferences', err));
        return () => { cancelled = true; };
    }, [serverId]);

    const update = (patch: Partial<TrackForm>) => {
        setForm(current => (current ? { ...current, ...patch } : current));
        setDirty(true);
    };

    if (!loaded || !form) {
        return (
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Audio &amp; subtitles</h2>
                <div className="vora-skeleton mt-4 h-24" />
            </section>
        );
    }

    const save = async () => {
        setSaving(true);
        try {
            const saved = await profileService.saveMyTrackPreferences({
                audioLanguage: form.audioLanguage === Automatic ? null : form.audioLanguage,
                subtitleLanguage: form.subtitleLanguage === Automatic ? null : form.subtitleLanguage,
                subtitleMode: form.subtitleMode === Automatic ? null : form.subtitleMode,
                preferHearingImpairedSubtitles: form.preferSdh,
            }, serverId);
            setLoaded(saved);
            setForm(formFrom(saved));
            setDirty(false);
        } catch (err) {
            await dialog.alert(errorDetail(err, 'Could not save your audio and subtitle preferences'));
        } finally {
            setSaving(false);
        }
    };

    const serverMode = modeEntry(loaded.serverSubtitleMode);
    const activeMode = form.subtitleMode === Automatic ? serverMode : modeEntry(form.subtitleMode);

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Audio &amp; subtitles</h2>
            <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Saved to your profile, so every device you watch on uses them. A track you pick on a title's page or in the player is remembered for that title and takes priority.
            </p>
            <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                <div>
                    <label htmlFor="track-audio-language" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Audio language</label>
                    <select id="track-audio-language" value={form.audioLanguage} onChange={e => update({ audioLanguage: e.target.value })} className={selectClass} style={selectStyle}>
                        <option value={Automatic}>Automatic</option>
                        <LanguageOptions saved={form.audioLanguage} />
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>Automatic plays the file's default audio track, and so does a language the file doesn't have.</p>
                </div>
                <div>
                    <label htmlFor="track-subtitle-mode" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Subtitles</label>
                    <select id="track-subtitle-mode" value={form.subtitleMode} onChange={e => update({ subtitleMode: pickedMode(e.target.value) })} className={selectClass} style={selectStyle}>
                        <option value={Automatic}>Automatic ({serverMode.label}, set by the server)</option>
                        {SUBTITLE_MODES.map(entry => <option key={entry.mode} value={entry.mode}>{entry.label}</option>)}
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>{activeMode.detail}</p>
                </div>
                <div>
                    <label htmlFor="track-subtitle-language" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Subtitle language</label>
                    <select id="track-subtitle-language" value={form.subtitleLanguage} onChange={e => update({ subtitleLanguage: e.target.value })} className={selectClass} style={selectStyle}>
                        <option value={Automatic}>Automatic ({languageName(loaded.serverLanguage)})</option>
                        <LanguageOptions saved={form.subtitleLanguage} />
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>Automatic uses the server's language.</p>
                </div>
            </div>
            <label className="mt-5 flex cursor-pointer items-center gap-3">
                <input type="checkbox" checked={form.preferSdh} onChange={e => update({ preferSdh: e.target.checked })} className="h-4 w-4 cursor-pointer" />
                <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>Prefer subtitles for the deaf and hard of hearing (SDH)</span>
            </label>
            <div className="mt-5 flex justify-end">
                <button type="button" onClick={save} disabled={saving || !dirty} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : 'Save audio & subtitles'}
                </button>
            </div>
        </section>
    );
}
