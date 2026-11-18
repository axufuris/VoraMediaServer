import { useState } from 'react';
import HealthBadge from '../Primitives/HealthBadge';
import PluginSettingsForm from '../Settings/PluginSettingsForm';
import { webhookAdminService, type WebhookProviderVM } from '../../../api/System/webhookAdminService';
import { useDialog } from '../../../dialogs';
import { copyText } from '../../../utils/copyText';
import { FOCUS_FILL, providerStatus, providerTitle, receiveUrl } from './libraryUpdates';

interface WebhookProviderCardProps {
    provider: WebhookProviderVM;
    serverId?: string;
    showModal: (title: string, message: string, isError?: boolean) => void;
    onChanged: () => void;
}

const BUTTON = `vora-button-secondary shrink-0 text-xs ${FOCUS_FILL}`;

export default function WebhookProviderCard({ provider, serverId, showModal, onChanged }: WebhookProviderCardProps) {
    const dialog = useDialog();
    const title = providerTitle(provider.name);
    const status = providerStatus(provider);
    const url = receiveUrl(provider.receivePath);
    const [urlCopied, setUrlCopied] = useState(false);
    const [secretCopied, setSecretCopied] = useState(false);
    const [newSecret, setNewSecret] = useState<string | null>(null);
    const [formKey, setFormKey] = useState(0);
    const [generating, setGenerating] = useState(false);

    const copyUrl = async () => {
        setUrlCopied(await copyText(url));
    };

    const copySecret = async () => {
        if (newSecret) setSecretCopied(await copyText(newSecret));
    };

    const generate = async () => {
        if (provider.isConfigured) {
            const confirmed = await dialog.confirm({
                title: 'Generate a new secret?',
                message: `The current secret stops working. ${title}'s connection must be updated with the new secret, or it will be refused.`,
                confirmText: 'Generate secret',
                cancelText: 'Cancel',
                tone: 'danger'
            });
            if (!confirmed) return;
        }
        setGenerating(true);
        try {
            const generated = await webhookAdminService.generateSecret(provider.id, serverId);
            setNewSecret(generated.secret);
            setSecretCopied(false);
            setFormKey(key => key + 1);
            onChanged();
        } catch {
            showModal('Error', `Failed to generate a secret for ${title}.`, true);
        } finally {
            setGenerating(false);
        }
    };

    return (
        <article className="vora-card overflow-hidden" aria-label={title}>
            <header className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--vora-border-subtle)] px-5 py-4 max-md:px-4">
                <div className="min-w-0">
                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)]">{title}</h3>
                    <p className="mt-0.5 text-xs text-[var(--vora-text-muted)]">{provider.description}</p>
                </div>
                <HealthBadge tone={status.tone} showDot={false}>{status.label}</HealthBadge>
            </header>

            <div className="space-y-5 px-5 py-4 max-md:px-4">
                {!provider.isEnabled && (
                    <p className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-strong)] bg-[var(--vora-bg-sunken)] p-3 text-sm text-[var(--vora-text-secondary)]">
                        This connection is off. Turn it on under Plugins.
                    </p>
                )}

                <div>
                    <label htmlFor={`${provider.id}-url`} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">
                        Address to paste into {title}
                    </label>
                    <div className="flex items-center gap-2">
                        <input
                            id={`${provider.id}-url`}
                            type="text"
                            readOnly
                            value={url}
                            onFocus={e => e.currentTarget.select()}
                            className="vora-input min-w-0 flex-1 font-mono text-xs"
                        />
                        <button
                            type="button"
                            onClick={copyUrl}
                            aria-label={urlCopied ? `${title} address copied` : `Copy ${title} address`}
                            className={BUTTON}
                        >
                            {urlCopied ? 'Copied' : 'Copy'}
                        </button>
                    </div>
                    <p className="mt-1.5 text-xs text-[var(--vora-text-muted)]">
                        {title} must be able to reach this address. Inside Docker that is usually the container name and port, not localhost.
                    </p>
                </div>

                <ol className="list-decimal space-y-1 pl-5 text-sm text-[var(--vora-text-secondary)]">
                    <li>In {title}, go to Settings, Connect, and add a Webhook.</li>
                    <li>Paste the address above into the URL field.</li>
                    <li>Set Method to POST.</li>
                    <li>Username can be anything, for example vora.</li>
                    <li>Set Password to the secret.</li>
                    <li>Tick the triggers for import and upgrade, rename, and delete.</li>
                    <li>Press Test.</li>
                </ol>

                <div className="space-y-3">
                    <button type="button" onClick={generate} disabled={generating} className={`vora-button-secondary text-xs disabled:opacity-50 ${FOCUS_FILL}`}>
                        {generating ? 'Generating…' : 'Generate secret'}
                    </button>

                    {newSecret && (
                        <div
                            role="status"
                            className="rounded-[var(--vora-radius-md)] border border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)] p-3 text-[var(--vora-accent-text)]"
                        >
                            <p className="text-xs font-bold uppercase tracking-widest">New secret</p>
                            <div className="mt-1.5 flex items-center gap-2 max-md:flex-wrap">
                                <code className="min-w-0 flex-1 break-all font-mono text-sm text-[var(--vora-text-primary)]">{newSecret}</code>
                                <button
                                    type="button"
                                    onClick={copySecret}
                                    aria-label={secretCopied ? `${title} secret copied` : `Copy ${title} secret`}
                                    className={BUTTON}
                                >
                                    {secretCopied ? 'Copied' : 'Copy'}
                                </button>
                            </div>
                            <p className="mt-2 text-xs">
                                Copy it now, it is only shown here once. It is already saved, and it is also editable below.
                            </p>
                        </div>
                    )}
                </div>

                <div className="border-t border-[var(--vora-border-subtle)] pt-4">
                    <PluginSettingsForm
                        key={formKey}
                        serverId={serverId}
                        pluginId={provider.id}
                        pluginName={provider.name}
                        showModal={showModal}
                        onAfterChange={onChanged}
                    />
                </div>
            </div>
        </article>
    );
}
