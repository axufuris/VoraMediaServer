import { useState } from 'react';
import { webhookAdminService, type WebhookPreviewResult } from '../../../api/System/webhookAdminService';
import { FieldLabel, Hint } from '../../../pages/Admin/SetupGuide/SetupParts';
import { changeKindLabel, webhookErrorMessage } from './connectionForm';
import { FOCUS_FILL } from './libraryUpdates';

interface SamplePayloadProps {
    providerId: string;
    settings: Record<string, string>;
    pathMappings: string;
    serverId?: string;
}

function PreviewResult({ result }: { result: WebhookPreviewResult }) {
    if (result.error || !result.ok) {
        return <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{result.error || 'Vora could not read that sample.'}</p>;
    }

    return (
        <div role="status" className="space-y-2 text-sm text-[var(--vora-text-secondary)]">
            <p>
                <span className="font-semibold text-[var(--vora-text-primary)]">Event: {result.event}</span>
                {result.isTest && ' (a test)'}
                {result.instance && <span>, instance {result.instance}</span>}
            </p>
            {result.changes.length === 0 ? (
                <p>No file changes. Nothing would be queued.</p>
            ) : (
                <ul className="space-y-2">
                    {result.changes.map((change, index) => (
                        <li key={index} className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-2.5">
                            <div className="flex flex-wrap items-baseline gap-x-2">
                                <span className="font-semibold text-[var(--vora-text-primary)]">{changeKindLabel(change.kind)}</span>
                                {change.reportedPath && <code className="break-all font-mono text-xs">{change.reportedPath}</code>}
                            </div>
                            {change.mappedPath && (
                                <div className="mt-1 text-xs">Maps to <code className="break-all font-mono">{change.mappedPath}</code></div>
                            )}
                            {change.note && <div className="mt-1 text-xs text-[var(--vora-text-muted)]">{change.note}</div>}
                            {change.libraryName && <div className="mt-1 text-xs">Library: {change.libraryName}</div>}
                            {change.existsOnDisk !== null && change.existsOnDisk !== undefined && (
                                <div className="mt-1 text-xs">{change.existsOnDisk ? 'On disk: yes' : 'On disk: no'}</div>
                            )}
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}

export default function SamplePayload({ providerId, settings, pathMappings, serverId }: SamplePayloadProps) {
    const [body, setBody] = useState('');
    const [running, setRunning] = useState(false);
    const [result, setResult] = useState<WebhookPreviewResult | null>(null);
    const [error, setError] = useState<string | null>(null);

    const run = async () => {
        setRunning(true);
        setError(null);
        setResult(null);
        try {
            setResult(await webhookAdminService.preview({ providerId, settings, pathMappings, body }, serverId));
        } catch (err) {
            setError(webhookErrorMessage(err, 'The sample could not be run.'));
        } finally {
            setRunning(false);
        }
    };

    return (
        <section className="space-y-2">
            <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">Try a sample payload</h3>
            <div>
                <FieldLabel htmlFor="webhook-sample">Sample payload</FieldLabel>
                <textarea
                    id="webhook-sample"
                    rows={6}
                    value={body}
                    onChange={e => setBody(e.target.value)}
                    spellCheck={false}
                    placeholder="Paste what the app sends"
                    className="vora-input w-full font-mono text-xs"
                />
                <Hint>A dry run with the values above, saved or not. Nothing is queued.</Hint>
            </div>
            <button type="button" onClick={run} disabled={running || !body.trim()} className={`vora-button-secondary text-xs disabled:opacity-50 ${FOCUS_FILL}`}>
                {running ? 'Running…' : 'Run'}
            </button>
            {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
            {result && <PreviewResult result={result} />}
        </section>
    );
}
