import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../components/Admin/Primitives/EmptyState';
import ProviderCard from '../../components/Admin/SignIn/ProviderCard';
import ProviderEditorModal from '../../components/Admin/SignIn/ProviderEditorModal';
import SignInSettingsCard from '../../components/Admin/SignIn/SignInSettingsCard';
import { PROVIDER_PRESETS, type ProviderPreset } from '../../components/Admin/SignIn/providerPresets';
import { resolveAdminPath } from '../../components/Admin/Shell/adminNavData';
import {
    signInProvidersAdminService,
    type AuthProviderAdmin,
    type AuthSettings,
    type OidcTestResult,
    type SaveAuthProvider,
} from '../../api/Auth/signInProvidersAdminService';
import { useDialog } from '../../dialogs';
import { resolveReason } from '../../utils/apiError';

type Editing = { provider: AuthProviderAdmin | null; preset: ProviderPreset | null };

export default function SignInProvidersPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [providers, setProviders] = useState<AuthProviderAdmin[]>([]);
    const [settings, setSettings] = useState<AuthSettings | null>(null);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [editing, setEditing] = useState<Editing | null>(null);
    const [tests, setTests] = useState<Record<string, OidcTestResult | 'testing'>>({});

    const load = useCallback(async () => {
        try {
            const [list, current] = await Promise.all([
                signInProvidersAdminService.list(serverId),
                signInProvidersAdminService.getSettings(serverId),
            ]);
            setProviders(list);
            setSettings(current);
            setLoadError(null);
        } catch (err) {
            setLoadError(resolveReason(err) ?? 'Could not load the sign-in providers.');
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        void load();
    }, [load]);

    const runTest = useCallback(async (provider: AuthProviderAdmin) => {
        setTests(current => ({ ...current, [provider.id]: 'testing' }));
        try {
            const result = await signInProvidersAdminService.test(provider.id, serverId);
            setTests(current => ({ ...current, [provider.id]: result }));
        } catch (err) {
            setTests(current => ({
                ...current,
                [provider.id]: {
                    ok: false, message: resolveReason(err) ?? 'The test could not run.', issuer: null, authorizationEndpoint: null, tokenEndpoint: null,
                    jwksUri: null, userInfoEndpoint: null, signingKeyCount: 0, advertisesPkceS256: false, warnings: [],
                },
            }));
        }
    }, [serverId]);

    const save = async (request: SaveAuthProvider) => {
        if (!editing) return;
        const saved = editing.provider
            ? await signInProvidersAdminService.update(editing.provider.id, request, serverId)
            : await signInProvidersAdminService.create(request, serverId);
        setEditing(null);
        await load();
        if (saved.issuer && saved.clientId) void runTest(saved);
    };

    const remove = async (provider: AuthProviderAdmin) => {
        const confirmed = await dialog.confirm({
            title: `Remove ${provider.displayName}?`,
            message: 'People who linked this provider can no longer sign in with it. Their accounts and watch history stay.',
            confirmText: 'Remove',
            tone: 'danger',
        });
        if (!confirmed) return;
        try {
            await signInProvidersAdminService.remove(provider.id, false, serverId);
        } catch (err) {
            if (!(axios.isAxiosError(err) && err.response?.status === 409)) {
                await dialog.alert(resolveReason(err) ?? 'Could not remove the provider.');
                return;
            }
            const force = await dialog.confirm({
                title: 'Some accounts would be locked out',
                message: `${resolveReason(err) ?? 'Some accounts can only sign in through this provider.'} Remove it anyway?`,
                confirmText: 'Remove anyway',
                tone: 'danger',
            });
            if (!force) return;
            try {
                await signInProvidersAdminService.remove(provider.id, true, serverId);
            } catch (forced) {
                await dialog.alert(resolveReason(forced) ?? 'Could not remove the provider.');
                return;
            }
        }
        setTests(current => {
            const next = { ...current };
            delete next[provider.id];
            return next;
        });
        await load();
    };

    const usersPath = resolveAdminPath('/admin/users', serverId);

    if (loading) {
        return (
            <div data-vora-page="">
                <PageHeader title="Sign-in providers" description="Let people sign in with your own identity provider." />
                <div className="mx-auto max-w-6xl p-8 max-md:p-4"><div className="vora-skeleton h-32" /></div>
            </div>
        );
    }

    return (
        <div data-vora-page="">
            <PageHeader
                title="Sign-in providers"
                description="Let people sign in with an identity provider you run or trust, such as Authentik, Keycloak, Google or Microsoft. Accounts, profiles and permissions stay in Vora."
                actions={<Link to={usersPath} className="vora-button-secondary">Users & Access</Link>}
            />

            <div className="mx-auto max-w-6xl space-y-8 p-8 max-md:p-4">
                {loadError && (
                    <div role="alert" className="rounded-[var(--vora-radius-md)] border border-[var(--vora-danger-500)] bg-[var(--vora-danger-soft)] p-3 text-sm text-[var(--vora-danger-text)]">
                        {loadError}
                        <button type="button" onClick={() => void load()} className="vora-button-secondary ml-3 text-xs">Try again</button>
                    </div>
                )}

                {settings && (
                    <SignInSettingsCard
                        settings={settings}
                        enabledProviderCount={providers.filter(p => p.enabled).length}
                        serverId={serverId}
                        onChanged={next => { setSettings(next); }}
                    />
                )}

                <section className="space-y-4" aria-labelledby="add-provider-heading">
                    <div>
                        <h2 id="add-provider-heading" className="text-base font-semibold text-[var(--vora-text-primary)]">Add a provider</h2>
                        <p className="text-sm text-[var(--vora-text-muted)]">Vora ships no client id of its own: you register this server at the provider, with the redirect address Vora shows you, and paste what the provider gives you.</p>
                    </div>
                    <div className="grid gap-3 md:grid-cols-3">
                        {PROVIDER_PRESETS.map(preset => (
                            <button
                                key={preset.id}
                                type="button"
                                onClick={() => setEditing({ provider: null, preset })}
                                className="vora-card cursor-pointer p-4 text-left transition-colors hover:border-[var(--vora-accent-500)]"
                            >
                                <span className="block font-semibold text-[var(--vora-text-primary)]">{preset.label}</span>
                                <span className="mt-1 block text-sm text-[var(--vora-text-muted)]">{preset.description}</span>
                            </button>
                        ))}
                    </div>
                </section>

                <section className="space-y-4" aria-labelledby="providers-heading">
                    <h2 id="providers-heading" className="text-base font-semibold text-[var(--vora-text-primary)]">Your providers</h2>
                    {providers.length === 0 ? (
                        <div className="vora-card">
                            <EmptyState title="No providers yet" description="Add one above. Until you do, everyone signs in with an email and password, exactly as before." />
                        </div>
                    ) : (
                        <div className="space-y-4">
                            {providers.map(provider => (
                                <ProviderCard
                                    key={provider.id}
                                    provider={provider}
                                    test={tests[provider.id] ?? null}
                                    onEdit={() => setEditing({ provider, preset: null })}
                                    onTest={() => void runTest(provider)}
                                    onDelete={() => void remove(provider)}
                                />
                            ))}
                        </div>
                    )}
                </section>
            </div>

            {editing && settings && (
                <ProviderEditorModal
                    provider={editing.provider}
                    preset={editing.preset}
                    effectiveBaseUrl={settings.effectiveBaseUrl}
                    sortOrder={providers.length}
                    onSave={save}
                    onClose={() => setEditing(null)}
                />
            )}
        </div>
    );
}
