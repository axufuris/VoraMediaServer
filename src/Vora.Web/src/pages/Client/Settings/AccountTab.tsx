import { useState, useEffect } from 'react';
import { profileService, type EpisodeLayout, type UpNextOrder } from '../../../api/Users/profileService';
import { useDialog } from '../../../dialogs';
import { StorageKeys } from '../../../utils/storageKeys';
import LibrarySortSection from './LibrarySortSection';

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';
const hintClass = 'mb-0 mt-2 text-xs';

const UP_NEXT_ORDERS: { value: UpNextOrder; label: string; detail: string }[] = [
    { value: 'InOrder', label: 'The next unwatched episode, in order', detail: 'Starts at the first episode you haven\'t finished, and resumes it if you stopped part way.' },
    { value: 'WhereILeftOff', label: 'Where I left off', detail: 'Starts at the episode you played most recently: resumes it if you stopped part way, or moves on to the one after it if you finished it.' },
];

const EPISODE_LAYOUTS: { value: EpisodeLayout; label: string; detail: string }[] = [
    { value: 'Row', label: 'A row', detail: 'Episodes scroll across, like on a TV. Pick one to see its details at the top.' },
    { value: 'List', label: 'A list', detail: 'Episodes are listed down the page, each with its picture and plot.' },
];

const pickedOrder = (value: string): UpNextOrder => UP_NEXT_ORDERS.find(entry => entry.value === value)?.value ?? 'InOrder';
const pickedLayout = (value: string): EpisodeLayout => EPISODE_LAYOUTS.find(entry => entry.value === value)?.value ?? 'Row';

export default function AccountTab({ serverId }: { serverId?: string }) {
    const profileName = localStorage.getItem(StorageKeys.profileName) || 'You';
    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Signed in as</h2>
                <p className="m-0 text-2xl font-semibold" style={{ color: 'var(--vora-accent-text)' }}>{profileName}</p>
            </section>
            <LibrarySortSection serverId={serverId} />
            <ShowtimesLocationSection serverId={serverId} />
            <PlaybackPreferencesSection serverId={serverId} />
        </div>
    );
}

function ShowtimesLocationSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [value, setValue] = useState('');
    const [originalValue, setOriginalValue] = useState('');
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const result = await profileService.getMyShowtimesLocation(serverId);
                if (cancelled) return;
                setValue(result.location ?? '');
                setOriginalValue(result.location ?? '');
            } catch (err) {
                console.error('Failed to load showtimes location', err);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [serverId]);

    const trimmed = value.trim();
    const dirty = trimmed !== (originalValue ?? '').trim();

    const handleSave = async () => {
        setSaving(true);
        try {
            const result = await profileService.saveMyShowtimesLocation(trimmed === '' ? null : trimmed, serverId);
            setValue(result.location ?? '');
            setOriginalValue(result.location ?? '');
        } catch {
            await dialog.alert('Could not save your showtimes location. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    const handleClear = async () => {
        setSaving(true);
        try {
            const result = await profileService.saveMyShowtimesLocation(null, serverId);
            setValue(result.location ?? '');
            setOriginalValue(result.location ?? '');
        } catch {
            await dialog.alert('Could not clear your showtimes location. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Movie showtimes location</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                Set a ZIP code or city to find theaters near you when browsing showtimes. Leave blank to use the server default set by your admin.
            </p>
            {loading ? (
                <div className="vora-skeleton h-12" />
            ) : (
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                    <input
                        type="text"
                        value={value}
                        onChange={e => setValue(e.target.value)}
                        placeholder="e.g. 90210, or Austin TX"
                        className="vora-input flex-1"
                        maxLength={120}
                    />
                    <div className="flex gap-2">
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || !dirty}
                            className="vora-button-primary cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            {saving ? 'Saving…' : 'Save'}
                        </button>
                        {originalValue && (
                            <button
                                type="button"
                                onClick={handleClear}
                                disabled={saving}
                                className="vora-button-secondary cursor-pointer disabled:opacity-50"
                                title="Clear and use the server default"
                            >
                                Clear
                            </button>
                        )}
                    </div>
                </div>
            )}
        </section>
    );
}

function PlaybackPreferencesSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [autoSkipIntro, setAutoSkipIntro] = useState(false);
    const [autoSkipCredits, setAutoSkipCredits] = useState(false);
    const [autoPlayNext, setAutoPlayNext] = useState(true);
    const [upNextOrder, setUpNextOrder] = useState<UpNextOrder>('InOrder');
    const [episodeLayout, setEpisodeLayout] = useState<EpisodeLayout>('Row');
    const [minSceneSeconds, setMinSceneSeconds] = useState(15);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [dirty, setDirty] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const prefs = await profileService.getMyPlaybackPreferences(serverId);
                if (cancelled) return;
                setAutoSkipIntro(prefs.autoSkipIntro);
                setAutoSkipCredits(prefs.autoSkipCredits);
                setAutoPlayNext(prefs.autoPlayNext !== false);
                setUpNextOrder(prefs.upNextOrder ?? 'InOrder');
                setEpisodeLayout(prefs.episodeLayout ?? 'Row');
                setMinSceneSeconds(prefs.minimumCreditsSceneSeconds);
            } catch (err) {
                console.error('Failed to load playback preferences', err);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [serverId]);

    const handleSave = async () => {
        setSaving(true);
        try {
            const saved = await profileService.saveMyPlaybackPreferences({
                autoSkipIntro,
                autoSkipCredits,
                autoPlayNext,
                upNextOrder,
                episodeLayout,
                minimumCreditsSceneSeconds: minSceneSeconds
            }, serverId);
            setAutoSkipIntro(saved.autoSkipIntro);
            setAutoSkipCredits(saved.autoSkipCredits);
            setAutoPlayNext(saved.autoPlayNext !== false);
            setUpNextOrder(saved.upNextOrder ?? 'InOrder');
            setEpisodeLayout(saved.episodeLayout ?? 'Row');
            setMinSceneSeconds(saved.minimumCreditsSceneSeconds);
            setDirty(false);
        } catch {
            await dialog.alert('Could not save your playback preferences. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Playback preferences</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                Choose what gets skipped automatically during playback, whether the next episode starts by itself, where Play on a show starts, and how a season's episodes are laid out. Credit scenes (mid-credits and post-credits stingers) are never auto-skipped — you'll always get a button to jump to the next one.
            </p>
            {loading ? (
                <div className="vora-skeleton h-24" />
            ) : (
                <div className="flex flex-col gap-4">
                    <label className="flex items-center gap-3 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={autoSkipIntro}
                            onChange={e => { setAutoSkipIntro(e.target.checked); setDirty(true); }}
                            className="h-4 w-4 cursor-pointer"
                        />
                        <span style={{ color: 'var(--vora-text-primary)' }}>Auto-skip intros and recaps</span>
                    </label>
                    <label className="flex items-center gap-3 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={autoSkipCredits}
                            onChange={e => { setAutoSkipCredits(e.target.checked); setDirty(true); }}
                            className="h-4 w-4 cursor-pointer"
                        />
                        <span style={{ color: 'var(--vora-text-primary)' }}>Auto-skip end credits (only when no credit scene follows)</span>
                    </label>
                    <label className="flex items-center gap-3 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={autoPlayNext}
                            onChange={e => { setAutoPlayNext(e.target.checked); setDirty(true); }}
                            className="h-4 w-4 cursor-pointer"
                        />
                        <span style={{ color: 'var(--vora-text-primary)' }}>Play the next episode automatically (also moves on through a playlist)</span>
                    </label>
                    <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
                        <div>
                            <label htmlFor="playback-up-next-order" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Play on a show or season starts at</label>
                            <select id="playback-up-next-order" value={upNextOrder} onChange={e => { setUpNextOrder(pickedOrder(e.target.value)); setDirty(true); }} className={selectClass} style={selectStyle}>
                                {UP_NEXT_ORDERS.map(entry => <option key={entry.value} value={entry.value}>{entry.label}</option>)}
                            </select>
                            <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>{UP_NEXT_ORDERS.find(entry => entry.value === upNextOrder)?.detail}</p>
                        </div>
                        <div>
                            <label htmlFor="playback-episode-layout" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Show a season's episodes as</label>
                            <select id="playback-episode-layout" value={episodeLayout} onChange={e => { setEpisodeLayout(pickedLayout(e.target.value)); setDirty(true); }} className={selectClass} style={selectStyle}>
                                {EPISODE_LAYOUTS.map(entry => <option key={entry.value} value={entry.value}>{entry.label}</option>)}
                            </select>
                            <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>{EPISODE_LAYOUTS.find(entry => entry.value === episodeLayout)?.detail}</p>
                        </div>
                    </div>
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
                        <label className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                            Hide "Skip to scene" button if next scene is within
                        </label>
                        <input
                            type="number"
                            min={0}
                            max={600}
                            value={minSceneSeconds}
                            onChange={e => { setMinSceneSeconds(Math.max(0, Math.min(600, parseInt(e.target.value || '0', 10)))); setDirty(true); }}
                            className="vora-input w-24"
                        />
                        <span className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>seconds</span>
                    </div>
                    <div className="flex justify-end">
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || !dirty}
                            className="vora-button-primary cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            {saving ? 'Saving…' : 'Save'}
                        </button>
                    </div>
                </div>
            )}
        </section>
    );
}
