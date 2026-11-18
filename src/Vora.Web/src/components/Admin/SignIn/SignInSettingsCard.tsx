import { useState } from 'react';
import { FieldLabel, Hint, SwitchRow } from '../../../pages/Admin/SetupGuide/SetupParts';
import { signInProvidersAdminService, type AuthSettings } from '../../../api/Auth/signInProvidersAdminService';
import { useDialog } from '../../../dialogs';
import { resolveReason } from '../../../utils/apiError';

interface SignInSettingsCardProps {
    settings: AuthSettings;
    enabledProviderCount: number;
    serverId?: string;
    onChanged: (settings: AuthSettings) => void;
}

export default function SignInSettingsCard({ settings, enabledProviderCount, serverId, onChanged }: SignInSettingsCardProps) {
    const dialog = useDialog();
    const [address, setAddress] = useState(settings.publicBaseUrl ?? '');
    const [savingAddress, setSavingAddress] = useState(false);
    const [savingSwitch, setSavingSwitch] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const addressChanged = address.trim() !== (settings.publicBaseUrl ?? '');

    const saveAddress = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setSavingAddress(true);
        setError(null);
        try {
            onChanged(await signInProvidersAdminService.updateSettings({
                passwordSignInEnabled: settings.passwordSignInEnabled,
                publicBaseUrl: address.trim() || null,
            }, serverId));
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not save the public address.');
        } finally {
            setSavingAddress(false);
        }
    };

    const changePasswordSignIn = async (allow: boolean) => {
        setError(null);
        if (!allow && enabledProviderCount > 0) {
            const confirmed = await dialog.confirm({
                title: 'Require SSO for everyone except admins?',
                message: 'People who are not administrators will no longer be able to sign in or register with an email and password. They will have to use a sign-in provider. Administrator accounts keep their passwords, so you cannot lock yourself out. Sessions that began with a password stop refreshing for non-admins.',
                confirmText: 'Turn password sign-in off',
                tone: 'danger',
            });
            if (!confirmed) return;
        }
        setSavingSwitch(true);
        try {
            onChanged(await signInProvidersAdminService.updateSettings({
                passwordSignInEnabled: allow,
                publicBaseUrl: settings.publicBaseUrl,
            }, serverId));
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not change password sign-in.');
        } finally {
            setSavingSwitch(false);
        }
    };

    return (
        <section className="vora-card space-y-6 p-6 max-md:p-4" aria-labelledby="sign-in-settings-heading">
            <div>
                <h2 id="sign-in-settings-heading" className="text-base font-semibold text-[var(--vora-text-primary)]">How people sign in</h2>
                <p className="mt-0.5 text-sm text-[var(--vora-text-muted)]">Two settings that apply to every provider.</p>
            </div>

            <form onSubmit={saveAddress} className="space-y-1.5">
                <FieldLabel htmlFor="sign-in-public-address">Public address of this server</FieldLabel>
                <div className="flex flex-wrap gap-2">
                    <input
                        id="sign-in-public-address"
                        value={address}
                        onChange={e => setAddress(e.target.value)}
                        placeholder="https://vora.example.com"
                        className="vora-input min-w-0 flex-1 font-mono"
                    />
                    <button type="submit" disabled={!addressChanged || savingAddress} className="vora-button-secondary disabled:opacity-50">
                        {savingAddress ? 'Saving…' : 'Save'}
                    </button>
                </div>
                <Hint>
                    The address your sign-in provider sends people back to. Providers are currently told <span className="font-mono">{settings.effectiveBaseUrl}</span>.
                    Set it when Vora is behind a reverse proxy or reached from outside your network, and register the matching redirect address at each provider.
                </Hint>
            </form>

            <div className={savingSwitch ? 'pointer-events-none opacity-70' : ''}>
                <SwitchRow
                    id="allow-password-sign-in"
                    label="Allow password sign-in"
                    description={settings.passwordSignInEnabled
                        ? 'Everyone can sign in with an email and password as well as through a provider.'
                        : 'Require SSO for everyone except admins. Only administrator accounts can still sign in or register with a password.'}
                    checked={settings.passwordSignInEnabled}
                    onChange={allow => void changePasswordSignIn(allow)}
                />
            </div>

            {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
        </section>
    );
}
