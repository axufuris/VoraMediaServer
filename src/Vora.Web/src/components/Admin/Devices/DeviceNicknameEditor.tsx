import { useState } from 'react';
import { resolveReason } from '../../../utils/apiError';
import { NICKNAME_MAX_LENGTH } from './deviceList';

interface DeviceNicknameEditorProps {
    label: string;
    initial: string;
    onSave: (nickname: string) => Promise<void>;
    onCancel: () => void;
}

export default function DeviceNicknameEditor({ label, initial, onSave, onCancel }: DeviceNicknameEditorProps) {
    const [value, setValue] = useState(initial);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async () => {
        setSaving(true);
        setError(null);
        try {
            await onSave(value);
        } catch (err: unknown) {
            setError(resolveReason(err) ?? 'Could not save the nickname.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <form
            onSubmit={event => { event.preventDefault(); void submit(); }}
            onKeyDown={event => { if (event.key === 'Escape') onCancel(); }}
            className="min-w-[16rem] space-y-1.5"
        >
            <div className="flex items-center gap-2">
                <input
                    type="text"
                    value={value}
                    onChange={event => setValue(event.target.value)}
                    maxLength={NICKNAME_MAX_LENGTH}
                    placeholder="e.g. Living room"
                    aria-label={`Nickname for ${label}`}
                    disabled={saving}
                    autoFocus
                    className="vora-input text-sm"
                />
                <button type="submit" disabled={saving} className="vora-button-primary shrink-0 cursor-pointer text-xs">{saving ? 'Saving…' : 'Save'}</button>
                <button type="button" onClick={onCancel} disabled={saving} className="vora-button-secondary shrink-0 cursor-pointer text-xs">Cancel</button>
            </div>
            <p className="m-0 text-[11px] text-[var(--vora-text-muted)]">Only admins see it. Leave it empty to go back to the device's own name.</p>
            {error && <p role="alert" className="m-0 text-xs text-[var(--vora-danger-text)]">{error}</p>}
        </form>
    );
}
