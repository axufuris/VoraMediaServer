import { useState, type SyntheticEvent } from 'react';
import { aiStatsService } from '../../../api/System/aiStatsService';
import { useDialog } from '../../../dialogs';
import AiConnectionFields from './AiConnectionFields';
import { AI_KEYS } from './aiProviders';
import { effectiveEmbeddingModel, useAiSettings } from './useAiSettings';

interface AiSettingsFormProps {
    serverId?: string;
    // Keeps the field ids apart when the form is on a page twice.
    idPrefix: string;
    onSaved?: () => void;
}

// Every AI setting in one form: where the AI runs, the model for each job, and
// the limits. AI Settings and the plugin's panel on the Plugins page both show
// this, so there is one place to learn and one behaviour.
export default function AiSettingsForm({ serverId, idPrefix, onSaved }: AiSettingsFormProps) {
    const dialog = useDialog();
    const form = useAiSettings(serverId);
    const [notice, setNotice] = useState<string | null>(null);
    const { values, set } = form;
    const id = (key: string) => `${idPrefix}-${key}`;

    const handleSave = async (event: SyntheticEvent) => {
        event.preventDefault();
        setNotice(null);
        const before = effectiveEmbeddingModel(form.saved);
        const after = effectiveEmbeddingModel(values, form.isOpenAi);
        if (await form.save()) return;

        setNotice('Saved.');
        onSaved?.();
        if (!after || after === before) return;

        // Vectors from two models cannot be compared, so the server only
        // searches the ones the current model made. Until the library has been
        // read again there are none.
        const startNow = await dialog.confirm({
            title: 'Read the library again now?',
            message: `Recommendations and AI playlists stay empty until every title and song has been read by "${after}". That happens on the nightly run at ${values[AI_KEYS.scheduleTime] || '02:00'}, or you can start it now.`,
            confirmText: 'Start now',
            cancelText: 'Wait for tonight',
        });
        if (!startNow) return;
        try {
            await aiStatsService.triggerAiTask(serverId);
            setNotice('Saved. Reading the library has started; follow it under Tasks.');
        } catch {
            await dialog.alert("Couldn't start it now. It will run tonight.");
        }
    };

    if (form.loadError) {
        return <p role="alert" className="text-sm text-[var(--vora-danger-text)]">Couldn't load the AI settings. Reload the page to try again.</p>;
    }
    if (form.loading) return <div className="vora-skeleton h-40" />;

    return (
        <form onSubmit={handleSave} className="flex flex-col gap-5">
            <AiConnectionFields form={form} idPrefix={idPrefix} />

            <div className="flex flex-col gap-4 border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                <p className="text-sm font-semibold text-[var(--vora-text-primary)]">Limits and rules</p>
                <div className="grid gap-4 sm:grid-cols-2">
                    <div className="min-w-0">
                        <label htmlFor={id(AI_KEYS.monthlyTokenLimit)} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Monthly token limit</label>
                        <input
                            id={id(AI_KEYS.monthlyTokenLimit)}
                            type="number"
                            min={0}
                            value={values[AI_KEYS.monthlyTokenLimit] ?? ''}
                            onChange={e => set(AI_KEYS.monthlyTokenLimit, e.target.value)}
                            className="vora-input"
                        />
                        <p className="mt-1.5 text-xs text-[var(--vora-text-muted)]">Across every AI feature, per calendar month. 0 means no limit. Past it, AI requests are refused until next month.</p>
                    </div>
                    <div className="min-w-0">
                        <label htmlFor={id(AI_KEYS.scheduleTime)} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Nightly run</label>
                        <input
                            id={id(AI_KEYS.scheduleTime)}
                            type="time"
                            value={values[AI_KEYS.scheduleTime] ?? ''}
                            onChange={e => set(AI_KEYS.scheduleTime, e.target.value)}
                            className="vora-input"
                        />
                        <p className="mt-1.5 text-xs text-[var(--vora-text-muted)]">When new titles and songs are read by the embedding model and the weekly AI playlists are made.</p>
                    </div>
                </div>
                <div className="min-w-0">
                    <label htmlFor={id(AI_KEYS.guardrails)} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Guardrails</label>
                    <textarea
                        id={id(AI_KEYS.guardrails)}
                        value={values[AI_KEYS.guardrails] ?? ''}
                        onChange={e => set(AI_KEYS.guardrails, e.target.value)}
                        rows={3}
                        placeholder="Exclude adult content. Prefer official English titles."
                        className="vora-input resize-y text-sm"
                    />
                    <p className="mt-1.5 text-xs text-[var(--vora-text-muted)]">House rules sent with every AI request, in every feature. Leave empty for none.</p>
                </div>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0 flex-1">
                    {form.result && (
                        <p role={form.result.success ? 'status' : 'alert'} className={`text-[13px] ${form.result.success ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-danger-text)]'}`}>
                            {form.result.message}
                        </p>
                    )}
                    {!form.result && notice && <p role="status" className="text-[13px] text-[var(--vora-success-text)]">{notice}</p>}
                </div>
                <div className="flex items-center gap-2">
                    {form.dirty && <button type="button" onClick={() => { form.reset(); setNotice(null); }} className="vora-button-secondary !px-3 !py-1.5 text-xs">Discard changes</button>}
                    <button type="button" onClick={() => { setNotice(null); void form.test(); }} disabled={form.busy} className="vora-button-secondary !px-3 !py-1.5 text-xs">
                        {form.busy ? 'Working…' : 'Test connection'}
                    </button>
                    <button type="submit" disabled={form.busy} className="vora-button-primary !px-3 !py-1.5 text-xs">Save settings</button>
                </div>
            </div>
        </form>
    );
}
