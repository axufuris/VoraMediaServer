import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { externalSignInService, type LinkedSignIn, type SignInProvider } from '../../api/Auth/externalSignInService';
import { useDialog } from '../../dialogs';
import { resolveReason } from '../../utils/apiError';
import { linkErrorMessage } from '../../utils/externalSignInMessages';

interface LinkedSignInsProps {
    serverId?: string;
    hasPassword: boolean;
}

export const LAST_WAY_IN = "This is the only way you can sign in. Set a password or link another sign-in first.";

export default function LinkedSignIns({ serverId, hasPassword }: LinkedSignInsProps) {
    const dialog = useDialog();
    const [searchParams, setSearchParams] = useSearchParams();
    const [linked, setLinked] = useState<LinkedSignIn[]>([]);
    const [available, setAvailable] = useState<SignInProvider[]>([]);
    const [passwordSignInEnabled, setPasswordSignInEnabled] = useState(true);
    const [loading, setLoading] = useState(true);
    const [message, setMessage] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null);
    const [busy, setBusy] = useState<string | null>(null);

    const linkedSlug = searchParams.get('linked');
    const linkError = searchParams.get('linkError');

    const load = useCallback(async () => {
        try {
            const [identities, providers] = await Promise.all([
                externalSignInService.listLinked(serverId),
                externalSignInService.getProvidersFor(serverId),
            ]);
            setLinked(identities);
            setAvailable(providers.providers);
            setPasswordSignInEnabled(providers.passwordSignInEnabled);
        } catch {
            setLinked([]);
            setAvailable([]);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        void load();
    }, [load]);

    useEffect(() => {
        if (!linkedSlug && !linkError) return;
        if (linkError) {
            setMessage({ tone: 'error', text: linkErrorMessage(linkError) });
        } else if (linkedSlug) {
            setMessage({ tone: 'ok', text: 'Linked. You can now sign in with it.' });
        }
        const next = new URLSearchParams(searchParams);
        next.delete('linked');
        next.delete('linkError');
        setSearchParams(next, { replace: true });
    }, [linkedSlug, linkError, searchParams, setSearchParams]);

    if (loading || (linked.length === 0 && available.length === 0 && !message)) return null;

    const linkedProviderIds = new Set(linked.map(l => l.providerId));
    const toLink = available.filter(p => !linkedProviderIds.has(p.id));
    const passwordWorks = hasPassword && passwordSignInEnabled;
    const isLastWayIn = (linkedCount: number) => !passwordWorks && linkedCount <= 1;

    const startLink = async (provider: SignInProvider) => {
        setBusy(provider.id);
        setMessage(null);
        try {
            window.location.assign(await externalSignInService.startLink(provider.slug, serverId));
        } catch (err) {
            setMessage({ tone: 'error', text: resolveReason(err) ?? `Could not start linking ${provider.displayName}.` });
            setBusy(null);
        }
    };

    const unlink = async (identity: LinkedSignIn) => {
        const confirmed = await dialog.confirm({
            title: `Unlink ${identity.providerName}?`,
            message: `You will no longer be able to sign in with ${identity.providerName}. Your account and everything in it stay.`,
            confirmText: 'Unlink',
            tone: 'danger',
        });
        if (!confirmed) return;
        setBusy(identity.id);
        setMessage(null);
        try {
            await externalSignInService.unlink(identity.id, serverId);
            setMessage({ tone: 'ok', text: `${identity.providerName} is no longer linked.` });
            await load();
        } catch (err) {
            setMessage({ tone: 'error', text: resolveReason(err) ?? 'Could not unlink that sign-in.' });
        } finally {
            setBusy(null);
        }
    };

    return (
        <div className="bg-[var(--vora-bg-surface)] rounded-lg p-6 mb-8 border border-[var(--vora-border-subtle)] shadow-[var(--vora-shadow-lg)]" aria-labelledby="linked-sign-ins-heading" role="region">
            <h2 id="linked-sign-ins-heading" className="text-xl font-bold mb-1 border-b border-[var(--vora-border-subtle)] pb-2">Linked sign-ins</h2>
            <p className="text-sm text-[var(--vora-text-muted)] mb-4">Other ways to sign in to this account. Your history, profiles and settings stay the same whichever you use.</p>

            {message && (
                <p role={message.tone === 'ok' ? 'status' : 'alert'} className={`mb-4 text-sm ${message.tone === 'ok' ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-danger-text)]'}`}>
                    {message.text}
                </p>
            )}

            {linked.length > 0 && (
                <ul className="divide-y divide-[var(--vora-border-subtle)] mb-4">
                    {linked.map(identity => {
                        const locked = isLastWayIn(linked.length);
                        return (
                            <li key={identity.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                                <div className="min-w-0">
                                    <p className="font-semibold text-[var(--vora-text-primary)]">{identity.providerName}</p>
                                    <p className="truncate text-xs text-[var(--vora-text-muted)]">
                                        {identity.email ?? 'No email'}
                                        {identity.lastSignInAt ? ` · last used ${new Date(identity.lastSignInAt).toLocaleDateString()}` : ''}
                                    </p>
                                </div>
                                <div className="flex flex-col items-end gap-1">
                                    <button
                                        type="button"
                                        disabled={locked || busy === identity.id}
                                        onClick={() => void unlink(identity)}
                                        className="vora-button-secondary text-xs disabled:opacity-50"
                                    >
                                        Unlink
                                    </button>
                                    {locked && <span className="max-w-xs text-right text-xs text-[var(--vora-text-muted)]">{LAST_WAY_IN}</span>}
                                </div>
                            </li>
                        );
                    })}
                </ul>
            )}

            {toLink.length > 0 && (
                <div className="flex flex-wrap gap-2">
                    {toLink.map(provider => (
                        <button key={provider.id} type="button" disabled={busy === provider.id} onClick={() => void startLink(provider)} className="vora-button-secondary disabled:opacity-50">
                            Link {provider.displayName}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}
