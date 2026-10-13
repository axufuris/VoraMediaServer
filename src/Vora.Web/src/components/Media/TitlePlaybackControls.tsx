import { useId } from 'react';
import type { QualityOption } from '../Client/Primitives/QualityPanel';
import type { TitlePlaybackPreferences } from '../../utils/titlePlaybackPreferences';

interface TitlePlaybackControlsProps {
    videoOptions: QualityOption<string>[];
    audioOptions: QualityOption<string>[];
    subtitleOptions: QualityOption<string>[];
    videoId: string;
    audioId: string;
    subtitleId: string;
    preferences: TitlePlaybackPreferences;
    isShow: boolean;
    onVideoChange: (id: string) => void;
    onAudioChange: (id: string) => void;
    onSubtitleChange: (id: string) => void;
    onPreferencesChange: (preferences: TitlePlaybackPreferences) => void;
    onReset: () => void;
}

export default function TitlePlaybackControls(props: TitlePlaybackControlsProps) {
    const id = useId();
    const selectClass = 'w-full min-w-0 cursor-pointer rounded-md p-2.5 text-sm';
    const selectStyle = { background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-primary)', border: '1px solid var(--vora-border-subtle)' };
    const labelClass = 'mb-1.5 block text-xs font-semibold';
    const saved = Object.values(props.preferences).some(value => value !== undefined);
    return (
        <section aria-label="Title playback preferences" className="mx-12 mb-8 rounded-xl p-5 max-md:mx-4" style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-subtle)' }}>
            <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
                <div>
                    <h2 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Playback preferences</h2>
                    <p className="mb-0 mt-1 text-xs" style={{ color: 'var(--vora-text-muted)' }}>Selections are saved for this {props.isShow ? 'show, including its episodes' : 'movie'} on this browser and override your global defaults.</p>
                </div>
                <button type="button" onClick={props.onReset} disabled={!saved} className="vora-button-secondary cursor-pointer text-xs disabled:cursor-default disabled:opacity-40">Use global defaults</button>
            </div>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                {([
                    { label: 'Video', options: props.videoOptions, value: props.videoId, change: props.onVideoChange },
                    { label: 'Audio', options: props.audioOptions, value: props.audioId, change: props.onAudioChange },
                    { label: 'Subtitles', options: props.subtitleOptions, value: props.subtitleId, change: props.onSubtitleChange },
                ]).map(field => (
                    <div key={field.label}>
                        <label htmlFor={`${id}-${field.label}`} className={labelClass}>{field.label}</label>
                        <select id={`${id}-${field.label}`} value={field.value} onChange={e => field.change(e.target.value)} disabled={field.options.length === 0} className={selectClass} style={selectStyle}>
                            {!field.options.length && <option value="">No tracks available</option>}
                            {field.options.map(option => <option key={option.value} value={option.value}>{option.label}{option.sublabel ? ` · ${option.sublabel}` : ''}</option>)}
                        </select>
                    </div>
                ))}
                <div>
                    <label htmlFor={`${id}-resolution`} className={labelClass}>Video quality</label>
                    <select id={`${id}-resolution`} value={props.preferences.maxResolution ?? 'global'} onChange={e => props.onPreferencesChange({ ...props.preferences, maxResolution: e.target.value === 'global' ? undefined : Number(e.target.value) })} className={selectClass} style={selectStyle}>
                        <option value="global">Global default</option><option value={0}>Original (device limit)</option><option value={2160}>4K (2160p)</option><option value={1080}>HD (1080p)</option><option value={720}>HD (720p)</option><option value={480}>SD (480p)</option>
                    </select>
                </div>
                <div>
                    <label htmlFor={`${id}-output`} className={labelClass}>Audio output</label>
                    <select id={`${id}-output`} value={props.preferences.maxAudioChannels ?? 'global'} onChange={e => props.onPreferencesChange({ ...props.preferences, maxAudioChannels: e.target.value === 'global' ? undefined : Number(e.target.value) })} className={selectClass} style={selectStyle}>
                        <option value="global">Global default</option><option value={0}>Automatic</option><option value={2}>Stereo (2.0)</option><option value={6}>5.1 surround</option><option value={8}>7.1 surround</option>
                    </select>
                </div>
                <div>
                    <label htmlFor={`${id}-bitrate`} className={labelClass}>Maximum bitrate</label>
                    <select id={`${id}-bitrate`} value={props.preferences.bitrate ?? 'global'} onChange={e => props.onPreferencesChange({ ...props.preferences, bitrate: e.target.value === 'global' ? undefined : Number(e.target.value) })} className={selectClass} style={selectStyle}>
                        <option value="global">Global default</option><option value={0}>Original (no limit)</option><option value={40}>40 Mbps</option><option value={20}>20 Mbps</option><option value={10}>10 Mbps</option><option value={4}>4 Mbps</option>
                    </select>
                </div>
            </div>
        </section>
    );
}
