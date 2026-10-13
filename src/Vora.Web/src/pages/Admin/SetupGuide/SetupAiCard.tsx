import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { aiSettingsService, type AiStatus } from '../../../api/System/aiSettingsService';
import type { PluginVM } from '../../../api/System/pluginAdminService';
import AiConnectionFields from '../../../components/Admin/Ai/AiConnectionFields';
import { AI_KEYS } from '../../../components/Admin/Ai/aiProviders';
import { useAiSettings } from '../../../components/Admin/Ai/useAiSettings';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import { resolveAdminPath } from '../../../components/Admin/Shell/adminNavData';
import { useStepSaver } from './setupSaver';
import { CardHeading, FieldLabel, Hint, SetupCard, StatusMessage } from './SetupParts';

interface SetupAiCardProps {
    plugin?: PluginVM;
    serverId?: string;
    onChanged: () => void;
    children?: ReactNode;
}

const ENABLED = { is_enabled: 'true' };

// The setup guide's AI step: the same connection form as AI Settings, saved
// with the step and tested on request.
export default function SetupAiCard({ plugin, serverId, onChanged, children }: SetupAiCardProps) {
    const form = useAiSettings(serverId);
    const [status, setStatus] = useState<AiStatus | null>(null);
    const [editing, setEditing] = useState(false);
    const { save, test, values, dirty, problem } = form;

    const loadStatus = useCallback(
        () => aiSettingsService.getStatus(serverId).then(setStatus).catch(() => setStatus(null)),
        [serverId],
    );

    useEffect(() => { void loadStatus(); }, [loadStatus]);

    const configured = !!status?.configured && plugin?.isEnabled !== false;
    const showForm = !configured || editing;

    const saveAndTest = async () => {
        if (await save(ENABLED)) return;
        const outcome = await test();
        await loadStatus();
        if (outcome.success) setEditing(false);
        onChanged();
    };

    useStepSaver(useCallback(async () => {
        if (!dirty || problem) return;
        const failure = await save(ENABLED);
        if (failure) throw new Error(failure);
        onChanged();
    }, [dirty, problem, save, onChanged]));

    const badge = form.result && !form.result.success
        ? <HealthBadge tone="error">Didn't work</HealthBadge>
        : form.result?.success
            ? <HealthBadge tone="ok">Connected</HealthBadge>
            : configured
                ? <HealthBadge tone="ok">Set up</HealthBadge>
                : <HealthBadge tone="neutral" showDot={false}>Not set up</HealthBadge>;

    const where = status?.isOpenAi ? 'OpenAI' : `the server at ${status?.host ?? ''}`;

    return (
        <SetupCard>
            <CardHeading title="AI connection" subtitle="Powers every AI feature. OpenAI charges per use and the monthly limit caps it; a model you run yourself costs nothing per use." badges={badge} />
            {form.loadError && <StatusMessage tone="error">Couldn't load the AI settings. Open AI Settings to set them up.</StatusMessage>}
            {form.loading && <div className="vora-skeleton h-16" />}
            {!form.loading && !form.loadError && !showForm && (
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <StatusMessage tone="ok">{form.result?.success ? form.result.message : `AI is set up and using ${where}.`}</StatusMessage>
                    <button type="button" onClick={() => setEditing(true)} className="vora-button-secondary !px-3 !py-1.5 text-xs">Change</button>
                </div>
            )}
            {!form.loading && !form.loadError && showForm && (
                <div className="flex flex-col gap-5">
                    <AiConnectionFields form={form} idPrefix="setup-ai" />
                    <div className="min-w-0 sm:max-w-xs">
                        <FieldLabel htmlFor="setup-ai-monthly-limit">Monthly token limit</FieldLabel>
                        <input
                            id="setup-ai-monthly-limit"
                            type="number"
                            min={0}
                            value={values[AI_KEYS.monthlyTokenLimit] ?? ''}
                            onChange={e => form.set(AI_KEYS.monthlyTokenLimit, e.target.value)}
                            className="vora-input"
                        />
                        <Hint>Across every AI feature, per calendar month. 0 means no limit.</Hint>
                    </div>
                    <div className="flex flex-wrap items-center justify-between gap-3">
                        <div className="min-w-0 flex-1">
                            {form.result && <StatusMessage tone={form.result.success ? 'ok' : 'error'}>{form.result.message}</StatusMessage>}
                        </div>
                        <div className="flex items-center gap-2">
                            {editing && <button type="button" onClick={() => { setEditing(false); form.reset(); }} className="vora-button-secondary !px-3 !py-1.5 text-xs">Cancel</button>}
                            <button type="button" onClick={saveAndTest} disabled={form.busy} className="vora-button-primary !px-3 !py-1.5 text-xs">
                                {form.busy ? 'Checking…' : 'Save & test'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
            {children}
            <p className="text-xs text-[var(--vora-text-muted)]">
                These settings, with the nightly schedule and guardrails, stay in <Link to={resolveAdminPath('/admin/ai-settings', serverId)} className="font-semibold text-[var(--vora-accent-text)] hover:underline">AI Settings</Link>.
            </p>
        </SetupCard>
    );
}
