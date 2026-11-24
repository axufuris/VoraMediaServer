import { useId, type ChangeEvent } from 'react';
import type { DeviceSettingsVM } from '../../../api/Users/profileService';
import { BITRATE_CHOICES, RESOLUTION_CHOICES, type PlaybackChoice } from '../../../utils/playbackChoices';
import { SEEK_STEP_CHOICES, VOLUME_STEP_CHOICES } from '../../../utils/playerKeys';

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';

const seekChoices: readonly PlaybackChoice[] = SEEK_STEP_CHOICES.map(seconds => ({ value: seconds, label: `${seconds} seconds` }));
const volumeChoices: readonly PlaybackChoice[] = VOLUME_STEP_CHOICES.map(percent => ({ value: percent, label: `${percent}%` }));

function withCurrent(choices: readonly PlaybackChoice[], current: number): readonly PlaybackChoice[] {
    return choices.some(choice => choice.value === current) ? choices : [...choices, { value: current, label: String(current) }];
}

interface DeviceSettingsFieldsProps {
    settings: DeviceSettingsVM;
    onChange: (patch: Partial<DeviceSettingsVM>) => void;
}

export default function DeviceSettingsFields({ settings, onChange }: DeviceSettingsFieldsProps) {
    const id = useId();
    const pick = (key: 'bitrate' | 'maxResolution' | 'seekStepSeconds' | 'volumeStepPercent') =>
        (event: ChangeEvent<HTMLSelectElement>) => onChange({ [key]: parseInt(event.target.value, 10) });

    const select = (key: 'bitrate' | 'maxResolution' | 'seekStepSeconds' | 'volumeStepPercent', label: string, choices: readonly PlaybackChoice[]) => (
        <div>
            <label htmlFor={`${id}-${key}`} className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>{label}</label>
            <select id={`${id}-${key}`} value={settings[key]} onChange={pick(key)} className={selectClass} style={selectStyle}>
                {withCurrent(choices, settings[key]).map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
            </select>
        </div>
    );

    const sound = (key: 'soundOnClick' | 'soundOnNavOpen' | 'soundOnMove', label: string) => (
        <label className="flex cursor-pointer items-center gap-3">
            <input type="checkbox" checked={settings[key]} onChange={event => onChange({ [key]: event.target.checked })} className="h-4 w-4 cursor-pointer" />
            <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>{label}</span>
        </label>
    );

    return (
        <div className="space-y-5">
            <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
                {select('bitrate', 'Maximum bitrate', BITRATE_CHOICES)}
                {select('maxResolution', 'Maximum resolution', RESOLUTION_CHOICES)}
                {select('seekStepSeconds', 'Left and Right skip', seekChoices)}
                {select('volumeStepPercent', 'Up and Down change volume by', volumeChoices)}
            </div>
            <div className="flex flex-wrap gap-x-6 gap-y-3">
                {sound('soundOnClick', 'Sound on select')}
                {sound('soundOnNavOpen', 'Sound on opening a menu')}
                {sound('soundOnMove', 'Sound on moving')}
            </div>
        </div>
    );
}
