import { useState } from 'react';
import { webhookAdminService, type WebhookRequestServerVM } from '../../../api/System/webhookAdminService';
import type { ConfirmOptions } from '../../../dialogs';
import { FieldLabel } from '../../../pages/Admin/SetupGuide/SetupParts';
import { webhookErrorMessage } from './connectionForm';
import { FOCUS_FILL, SELECT_FOCUS } from './libraryUpdates';

export interface RequestServerFillResult {
    name?: string;
    mappings?: string;
}

interface RequestServerFillProps {
    servers: WebhookRequestServerVM[];
    serverId?: string;
    mappings: string;
    nameIsUntouched: boolean;
    confirm: (options: ConfirmOptions) => Promise<boolean>;
    onFill: (fill: RequestServerFillResult) => void;
}

interface Status {
    tone: 'ok' | 'error';
    text: string;
}

export default function RequestServerFill({ servers, serverId, mappings, nameIsUntouched, confirm, onFill }: RequestServerFillProps) {
    const [picked, setPicked] = useState('');
    const [busy, setBusy] = useState(false);
    const [status, setStatus] = useState<Status | null>(null);
    const chosen = servers.some(server => server.id === picked) ? picked : servers[0]?.id ?? '';

    const fill = async () => {
        if (!chosen) return;
        setBusy(true);
        setStatus(null);
        try {
            const found = await webhookAdminService.getRequestServerMappings(chosen, serverId);
            if (found.error) {
                setStatus({ tone: 'error', text: found.error });
                return;
            }
            const incoming = (found.mappings ?? '').trim();
            if (incoming && mappings.trim() && incoming !== mappings.trim()) {
                const replace = await confirm({
                    title: 'Replace the path mappings?',
                    message: `The path mappings already have text. Replace it with what was found in ${found.name}?`,
                    confirmText: 'Replace',
                    cancelText: 'Keep what I have'
                });
                if (!replace) return;
            }
            onFill({
                name: nameIsUntouched && found.name ? found.name : undefined,
                mappings: incoming || undefined
            });
            const unmatched = found.unmatchedRoots?.length ? ` No Vora folder was found for ${found.unmatchedRoots.join(', ')}.` : '';
            setStatus({ tone: 'ok', text: `${incoming ? `Filled in from ${found.name}.` : `Nothing to map from ${found.name}.`}${unmatched}` });
        } catch (err) {
            setStatus({ tone: 'error', text: webhookErrorMessage(err, 'Could not read that Request Server.') });
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-3">
            <FieldLabel htmlFor="webhook-fill-server">Fill in from a Request Server</FieldLabel>
            <div className="flex items-center gap-2 max-md:flex-wrap">
                <select
                    id="webhook-fill-server"
                    value={chosen}
                    onChange={e => setPicked(e.target.value)}
                    className={`vora-input min-w-0 flex-1 cursor-pointer max-md:basis-full ${SELECT_FOCUS}`}
                >
                    {servers.map(server => <option key={server.id} value={server.id}>{server.name}</option>)}
                </select>
                <button type="button" onClick={fill} disabled={busy} className={`vora-button-secondary shrink-0 text-xs disabled:opacity-50 ${FOCUS_FILL}`}>
                    {busy ? 'Filling…' : 'Fill in'}
                </button>
            </div>
            {status && (
                <p
                    role={status.tone === 'error' ? 'alert' : 'status'}
                    className={`mt-2 text-xs ${status.tone === 'error' ? 'text-[var(--vora-danger-text)]' : 'text-[var(--vora-text-secondary)]'}`}
                >
                    {status.text}
                </p>
            )}
        </div>
    );
}
