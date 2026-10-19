import { useState } from 'react';
import type { AudioPassthrough, DesktopSettings, HardwareDecoding } from '../../../desktop/desktopBridge';
import { updateDesktopSettings } from '../../../desktop/desktopPlayback';
import { useDesktopPlayback } from '../../../desktop/useDesktopPlayback';
import { useDialog } from '../../../dialogs';

const PASSTHROUGH_CHOICES: ReadonlyArray<{ value: AudioPassthrough; label: string }> = [
    { value: 'off', label: 'Off' },
    { value: 'core', label: 'AC-3 and DTS' },
    { value: 'all', label: 'All, including TrueHD, DTS-HD and E-AC-3' },
];

const DECODING_CHOICES: ReadonlyArray<{ value: HardwareDecoding; label: string }> = [
    { value: 'auto', label: 'Automatic' },
    { value: 'off', label: 'Off' },
];

const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;
const labelClass = 'mb-1.5 block text-xs font-semibold uppercase tracking-wider';
const hintClass = 'mb-0 mt-2 text-xs';

function passthroughFrom(value: string): AudioPassthrough {
    return PASSTHROUGH_CHOICES.find(choice => choice.value === value)?.value ?? 'off';
}

function decodingFrom(value: string): HardwareDecoding {
    return DECODING_CHOICES.find(choice => choice.value === value)?.value ?? 'auto';
}

export default function DesktopPlayerSection() {
    const dialog = useDialog();
    const desktop = useDesktopPlayback();
    const [form, setForm] = useState<DesktopSettings | null>(null);
    const [saving, setSaving] = useState(false);

    const bridge = desktop.bridge;
    if (!bridge) return null;

    const shown = form ?? desktop.settings;
    if (!shown) {
        return (
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Desktop player</h2>
                <div className="vora-skeleton mt-4 h-24" />
            </section>
        );
    }

    const update = (patch: Partial<DesktopSettings>) => setForm({ ...shown, ...patch });

    const save = async () => {
        if (!form) return;
        setSaving(true);
        try {
            await updateDesktopSettings(form);
            setForm(null);
        } catch (error: unknown) {
            console.error('Could not save the desktop player settings', error);
            await dialog.alert('Could not save the desktop player settings.');
        } finally {
            setSaving(false);
        }
    };

    const openAdvanced = () => {
        bridge.settings.openAdvancedFolder()
            .catch((error: unknown) => console.error('Could not open the mpv settings folder', error));
    };

    const mpv = desktop.mpv;
    const mpvLine = mpv?.available
        ? `Videos play in the Vora player${mpv.version ? ` (mpv ${mpv.version})` : ''}.`
        : `Videos play in the built-in player. ${mpv?.reason ?? 'mpv was not found.'}`;

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Desktop player</h2>
            <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Saved on this computer. {mpvLine} Changes apply to the next video you play.
            </p>
            <label className="mb-5 flex cursor-pointer items-center gap-3">
                <input id="desktop-use-mpv" type="checkbox" checked={shown.useMpvForVideo} onChange={e => update({ useMpvForVideo: e.target.checked })} className="h-4 w-4 cursor-pointer" />
                <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>Use the Vora player (mpv) for videos</span>
            </label>
            <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                <div>
                    <label htmlFor="desktop-passthrough" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Audio passthrough</label>
                    <select id="desktop-passthrough" value={shown.audioPassthrough} onChange={e => update({ audioPassthrough: passthroughFrom(e.target.value) })} className={selectClass} style={selectStyle}>
                        {PASSTHROUGH_CHOICES.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>Sends the original audio to a receiver or soundbar to decode. Leave it off for speakers and headphones.</p>
                </div>
                <div>
                    <label htmlFor="desktop-hardware-decoding" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Hardware decoding</label>
                    <select id="desktop-hardware-decoding" value={shown.hardwareDecoding} onChange={e => update({ hardwareDecoding: decodingFrom(e.target.value) })} className={selectClass} style={selectStyle}>
                        {DECODING_CHOICES.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
                    </select>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>Automatic decodes on the graphics card where it can. Turn it off if a video shows artefacts.</p>
                </div>
                <div>
                    <span className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Advanced</span>
                    <button type="button" onClick={openAdvanced} className="vora-button-secondary cursor-pointer">Open mpv settings folder</button>
                    <p className={hintClass} style={{ color: 'var(--vora-text-muted)' }}>An mpv.conf there is read after Vora's own settings.</p>
                </div>
            </div>
            <label className="mt-5 flex cursor-pointer items-center gap-3">
                <input id="desktop-hdr-output" type="checkbox" checked={shown.hdrOutput} onChange={e => update({ hdrOutput: e.target.checked })} className="h-4 w-4 cursor-pointer" />
                <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>Send HDR to an HDR display</span>
            </label>
            <div className="mt-5 flex justify-end">
                <button type="button" onClick={save} disabled={saving || !form} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : 'Save desktop player'}
                </button>
            </div>
        </section>
    );
}
