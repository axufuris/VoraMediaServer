import type { EpisodeLayout, UpNextOrder } from '../../../api/Users/profileService';
import type { PlaybackForm } from './clientSettingsForm';

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

interface PlaybackBehaviourFieldsProps {
    form: PlaybackForm;
    onChange: (patch: Partial<PlaybackForm>) => void;
}

export default function PlaybackBehaviourFields({ form, onChange }: PlaybackBehaviourFieldsProps) {
    return (
        <div className="flex flex-col gap-4">
            <label className="flex items-center gap-3 cursor-pointer">
                <input
                    type="checkbox"
                    checked={form.autoSkipIntro}
                    onChange={e => onChange({ autoSkipIntro: e.target.checked })}
                    className="h-4 w-4 cursor-pointer"
                />
                <span style={{ color: 'var(--vora-text-primary)' }}>Auto-skip intros and recaps</span>
            </label>
            <label className="flex items-center gap-3 cursor-pointer">
                <input
                    type="checkbox"
                    checked={form.autoSkipCredits}
                    onChange={e => onChange({ autoSkipCredits: e.target.checked })}
                    className="h-4 w-4 cursor-pointer"
                />
                <span style={{ color: 'var(--vora-text-primary)' }}>Auto-skip end credits (only when no credit scene follows)</span>
            </label>
            <label className="flex items-center gap-3 cursor-pointer">
                <input
                    type="checkbox"
                    checked={form.autoPlayNext}
                    onChange={e => onChange({ autoPlayNext: e.target.checked })}
                    className="h-4 w-4 cursor-pointer"
                />
                <span style={{ color: 'var(--vora-text-primary)' }}>Play the next episode automatically (also moves on through a playlist)</span>
            </label>
            <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
                <div>
                    <label htmlFor="playback-up-next-order" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Play on a show or season starts at</label>
                    <select id="playback-up-next-order" value={form.upNextOrder} onChange={e => onChange({ upNextOrder: pickedOrder(e.target.value) })} className={selectClass} style={selectStyle}>
                        {UP_NEXT_ORDERS.map(entry => <option key={entry.value} value={entry.value}>{entry.label}</option>)}
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>{UP_NEXT_ORDERS.find(entry => entry.value === form.upNextOrder)?.detail}</p>
                </div>
                <div>
                    <label htmlFor="playback-episode-layout" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Show a season's episodes as</label>
                    <select id="playback-episode-layout" value={form.episodeLayout} onChange={e => onChange({ episodeLayout: pickedLayout(e.target.value) })} className={selectClass} style={selectStyle}>
                        {EPISODE_LAYOUTS.map(entry => <option key={entry.value} value={entry.value}>{entry.label}</option>)}
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>{EPISODE_LAYOUTS.find(entry => entry.value === form.episodeLayout)?.detail}</p>
                </div>
            </div>
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
                <label htmlFor="playback-min-scene-seconds" className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                    Hide "Skip to scene" button if next scene is within
                </label>
                <input
                    id="playback-min-scene-seconds"
                    type="number"
                    min={0}
                    max={600}
                    value={form.minimumCreditsSceneSeconds}
                    onChange={e => onChange({ minimumCreditsSceneSeconds: Math.max(0, Math.min(600, parseInt(e.target.value || '0', 10))) })}
                    className="vora-input w-24"
                />
                <span className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>seconds</span>
            </div>
        </div>
    );
}
