import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { systemSettingsAdminService, type PluginConnectionTestResult, type PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import type { PluginVM } from '../../../api/System/pluginAdminService';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import { resolveAdminPath } from '../../../components/Admin/Shell/adminNavData';
import { linkify } from '../../../utils/linkify';
import { useStepSaver } from './setupSaver';
import { CardHeading, FieldLabel, RecommendedBadge, SetupCard, StatusMessage } from './SetupParts';

interface SetupPluginCardProps {
    pluginId: string;
    title: string;
    subtitle: string;
    fieldKeys: readonly string[];
    plugin?: PluginVM;
    recommended?: string;
    note?: ReactNode;
    serverId?: string;
    onChanged: () => void;
    children?: ReactNode;
}

type Values = Record<string, string>;

const sameValues = (a: Values, b: Values, keys: readonly string[]) => keys.every(k => (a[k] ?? '') === (b[k] ?? ''));

export default function SetupPluginCard({ pluginId, title, subtitle, fieldKeys, plugin, recommended, note, serverId, onChanged, children }: SetupPluginCardProps) {
    const [fields, setFields] = useState<PluginSettingField[] | null>(null);
    const [values, setValues] = useState<Values>({});
    const [saved, setSaved] = useState<Values>({});
    const [editing, setEditing] = useState(false);
    const [busy, setBusy] = useState(false);
    const [result, setResult] = useState<PluginConnectionTestResult | null>(null);
    const [loadError, setLoadError] = useState(false);

    useEffect(() => {
        let cancelled = false;
        systemSettingsAdminService.getPluginSettings(pluginId, serverId)
            .then(loaded => {
                if (cancelled) return;
                const shown = fieldKeys.map(k => loaded.find(f => f.key === k)).filter((f): f is PluginSettingField => !!f);
                const initial: Values = {};
                shown.forEach(f => { initial[f.key] = f.value ?? ''; });
                setFields(shown);
                setValues(initial);
                setSaved(initial);
            })
            .catch(() => { if (!cancelled) setLoadError(true); });
        return () => { cancelled = true; };
    }, [pluginId, serverId, fieldKeys]);

    const primaryKey = fieldKeys[0];
    const hasKey = (saved[primaryKey] ?? '').trim().length > 0 && plugin?.isEnabled !== false;
    const dirty = fields !== null && !sameValues(values, saved, fieldKeys);
    const showForm = !hasKey || editing;

    const payload = useCallback((): Values => {
        const body: Values = {};
        fieldKeys.forEach(k => { body[k] = values[k] ?? ''; });
        body.is_enabled = 'true';
        return body;
    }, [fieldKeys, values]);

    const saveAndTest = async () => {
        if (!(values[primaryKey] ?? '').trim()) {
            setResult({ success: false, message: 'Paste the key first.' });
            return;
        }
        setBusy(true);
        setResult(null);
        try {
            const body = payload();
            await systemSettingsAdminService.updatePluginSettings(pluginId, body, serverId);
            setSaved(values);
            const outcome = plugin?.supportsConnectionTest
                ? await systemSettingsAdminService.testPluginConnection(pluginId, body, serverId)
                : { success: true, message: 'Saved.' };
            setResult(outcome);
            if (outcome.success) setEditing(false);
            onChanged();
        } catch {
            setResult({ success: false, message: `Couldn't save the ${title} settings. Try again in a moment.` });
        } finally {
            setBusy(false);
        }
    };

    useStepSaver(useCallback(async () => {
        if (!dirty || !(values[primaryKey] ?? '').trim()) return;
        await systemSettingsAdminService.updatePluginSettings(pluginId, payload(), serverId);
        setSaved(values);
        onChanged();
    }, [dirty, values, primaryKey, pluginId, payload, serverId, onChanged]));

    const status = result && !result.success
        ? <HealthBadge tone="error">Didn't work</HealthBadge>
        : result?.success && plugin?.supportsConnectionTest
            ? <HealthBadge tone="ok">Connected</HealthBadge>
            : hasKey
                ? <HealthBadge tone="ok">Key saved</HealthBadge>
                : <HealthBadge tone="neutral" showDot={false}>Not set up</HealthBadge>;

    return (
        <SetupCard highlighted={!!recommended && !hasKey}>
            <CardHeading title={title} subtitle={subtitle} badges={<>{recommended && <RecommendedBadge>{recommended}</RecommendedBadge>}{status}</>} />
            {note}
            {loadError && <StatusMessage tone="error">Couldn't load this plugin's settings. Open Plugins to set it up.</StatusMessage>}
            {fields === null && !loadError && <div className="vora-skeleton h-16" />}
            {fields !== null && !showForm && (
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <StatusMessage tone="ok">{result?.success ? result.message : `${title} is set up and in use.`}</StatusMessage>
                    <button type="button" onClick={() => setEditing(true)} className="vora-button-secondary !px-3 !py-1.5 text-xs">Change key</button>
                </div>
            )}
            {fields !== null && showForm && (
                <div className="flex flex-col gap-4">
                    {fields.map(field => {
                        const id = `setup-${pluginId}-${field.key}`;
                        return (
                            <div key={field.key} className="min-w-0">
                                <FieldLabel htmlFor={id}>{field.label}</FieldLabel>
                                {field.description && <p className="mb-2 max-w-[78ch] text-[13px] text-[var(--vora-text-secondary)]">{linkify(field.description)}</p>}
                                {field.type === 'select' ? (
                                    <select id={id} value={values[field.key] ?? ''} onChange={e => setValues(v => ({ ...v, [field.key]: e.target.value }))} className="vora-input cursor-pointer">
                                        {field.options.map(o => <option key={o} value={o}>{o}</option>)}
                                    </select>
                                ) : (
                                    <input
                                        id={id}
                                        type={field.type === 'password' ? 'password' : field.type === 'number' ? 'number' : 'text'}
                                        value={values[field.key] ?? ''}
                                        onChange={e => { setValues(v => ({ ...v, [field.key]: e.target.value })); setResult(null); }}
                                        placeholder={field.placeholder || undefined}
                                        className={`vora-input ${field.type === 'password' ? 'font-mono text-sm' : ''}`}
                                        autoComplete="off"
                                    />
                                )}
                            </div>
                        );
                    })}
                    <div className="flex flex-wrap items-center justify-between gap-3">
                        <div className="min-w-0 flex-1">
                            {result && <StatusMessage tone={result.success ? 'ok' : 'error'}>{result.message}</StatusMessage>}
                        </div>
                        <div className="flex items-center gap-2">
                            {editing && <button type="button" onClick={() => { setEditing(false); setValues(saved); setResult(null); }} className="vora-button-secondary !px-3 !py-1.5 text-xs">Cancel</button>}
                            <button type="button" onClick={saveAndTest} disabled={busy} className="vora-button-primary !px-3 !py-1.5 text-xs">
                                {busy ? 'Checking…' : plugin?.supportsConnectionTest ? 'Save & test' : 'Save'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
            {children}
            <p className="text-xs text-[var(--vora-text-muted)]">
                More options for {title} are in <Link to={resolveAdminPath('/admin/plugins', serverId)} className="font-semibold text-[var(--vora-accent-text)] hover:underline">Plugins</Link>.
            </p>
        </SetupCard>
    );
}
