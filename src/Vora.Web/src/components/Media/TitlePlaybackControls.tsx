import { useId, type CSSProperties } from 'react';
import type { QualityOption } from '../Client/Primitives/QualityPanel';
import type { TitlePlaybackPreferences } from '../../utils/titlePlaybackPreferences';

interface TitlePlaybackControlsProps {
    videoOptions: QualityOption<string>[];
    audioOptions: QualityOption<string>[];
    subtitleOptions: QualityOption<string>[];
    videoId: string;
    audioId: string;
    subtitleId: string;
    onVideoChange: (id: string) => void;
    onAudioChange: (id: string) => void;
    onSubtitleChange: (id: string) => void;
}

const pillStyle: CSSProperties = {
    background: 'color-mix(in srgb, var(--vora-accent-500) 16%, transparent)',
    border: '1px solid color-mix(in srgb, var(--vora-accent-500) 32%, transparent)',
    color: 'var(--vora-text-primary)',
    backdropFilter: 'blur(12px)',
    WebkitBackdropFilter: 'blur(12px)',
};

function TrackIcon({ kind }: { kind: string }) {
    return (
        <svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 opacity-85">
            {kind === 'Video' ? <><rect x="3" y="5" width="18" height="13" rx="2" /><path d="M8 21h8M12 18v3" /></>
                : kind === 'Audio' ? <><path d="m11 4-5 4H3v8h3l5 4V4Z" /><path d="M15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14" /></>
                    : <><rect x="3" y="5" width="18" height="14" rx="2" /><path d="M6 11h4M14 11h4M6 15h6M15 15h3" /></>}
        </svg>
    );
}

export default function TitlePlaybackControls(props: TitlePlaybackControlsProps) {
    return (
        <div role="group" aria-label="Title playback preferences" className="flex max-w-full flex-wrap items-center gap-2">
            {[
                { label: 'Video', options: props.videoOptions, value: props.videoId, change: props.onVideoChange },
                { label: 'Audio', options: props.audioOptions, value: props.audioId, change: props.onAudioChange },
                { label: 'Subtitles', options: props.subtitleOptions, value: props.subtitleId, change: props.onSubtitleChange },
            ].filter(field => field.options.length > 0).map(field => {
                const disabled = field.options.length <= 1;
                const current = field.options.find(option => option.value === field.value);
                return (
                    <span key={field.label} className="vora-track-pill relative inline-flex max-w-full rounded-full" style={pillStyle}>
                        <TrackIcon kind={field.label} />
                        <select
                            aria-label={field.label}
                            title={current ? `${field.label}: ${current.label}${current.sublabel ? ` · ${current.sublabel}` : ''}${disabled ? ' (only available choice)' : ''}` : field.label}
                            value={field.value} onChange={event => field.change(event.target.value)} disabled={disabled}
                            className={`h-8 max-w-full min-w-0 appearance-none rounded-full bg-transparent pl-9 text-xs font-medium outline-none ${disabled ? 'cursor-default pr-3' : 'cursor-pointer pr-7'}`}
                            style={{ color: 'inherit', opacity: 1, textOverflow: 'ellipsis' }}
                        >
                            {field.options.map(option => <option key={option.value} value={option.value} style={{ background: 'var(--vora-bg-raised)', color: 'var(--vora-text-primary)' }}>{option.label}</option>)}
                        </select>
                        {!disabled && <svg aria-hidden="true" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 opacity-70"><path d="m6 9 6 6 6-6" /></svg>}
                    </span>
                );
            })}
        </div>
    );
}

interface TitlePlaybackAdvancedProps {
    preferences: TitlePlaybackPreferences;
    onPreferencesChange: (preferences: TitlePlaybackPreferences) => void;
    onReset: () => void;
}

export function TitlePlaybackAdvanced({ preferences, onPreferencesChange, onReset }: TitlePlaybackAdvancedProps) {
    const id = useId();
    const fields = [
        { label: 'Video quality', key: 'maxResolution', options: [[0, 'Original (device limit)'], [2160, '4K (2160p)'], [1080, 'HD (1080p)'], [720, 'HD (720p)'], [480, 'SD (480p)']] },
        { label: 'Audio output', key: 'maxAudioChannels', options: [[0, 'Automatic'], [2, 'Stereo (2.0)'], [6, '5.1 surround'], [8, '7.1 surround']] },
        { label: 'Maximum bitrate', key: 'bitrate', options: [[0, 'Original (no limit)'], [40, '40 Mbps'], [20, '20 Mbps'], [10, '10 Mbps'], [4, '4 Mbps']] },
    ] as const;
    return (
        <div className="space-y-4 border-t pt-5" style={{ borderColor: 'var(--vora-border-subtle)' }}>
            {fields.map(field => <div key={field.key}>
                <label htmlFor={`${id}-${field.key}`} className="mb-1.5 block text-xs font-semibold" style={{ color: 'var(--vora-text-muted)' }}>{field.label}</label>
                <select id={`${id}-${field.key}`} value={preferences[field.key] ?? 'global'} onChange={event => onPreferencesChange({ ...preferences, [field.key]: event.target.value === 'global' ? undefined : Number(event.target.value) })} className="w-full cursor-pointer rounded-md p-2.5 text-sm" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-primary)', border: '1px solid var(--vora-border-subtle)' }}>
                    <option value="global">Global default</option>
                    {field.options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                </select>
            </div>)}
            <button type="button" onClick={onReset} disabled={Object.values(preferences).every(value => value === undefined)} className="vora-button-secondary cursor-pointer text-xs disabled:cursor-default disabled:opacity-40">Use global defaults</button>
        </div>
    );
}
