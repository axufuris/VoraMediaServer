import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
import { authService } from '../../../api/Auth/authService';
import { invitationsAdminService } from '../../../api/Auth/invitationsAdminService';
import { resolveReason } from '../../../utils/apiError';

// How new people get an account, and the one thing to do about it, on the page
// where accounts are managed. The mode used to live only in System Settings and
// this page only ever offered a PIN, whatever the mode, so an admin on
// "Invitation only" or "Disabled" had nothing here that worked.
const RegistrationModes = {
    Disabled: 0,
    Open: 1,
    InvitePin: 2,
    InvitationOnly: 3,
} as const;

const MODE_OPTIONS = [
    { value: RegistrationModes.Disabled, label: 'Disabled — only admins add people' },
    { value: RegistrationModes.Open, label: 'Open — anyone can sign up' },
    { value: RegistrationModes.InvitePin, label: 'Invite PIN — sign up with a code from you' },
    { value: RegistrationModes.InvitationOnly, label: 'Invitation only — sign up from an emailed link' },
];

interface SignUpCardProps {
    serverId?: string;
    invitationsPath: string;
}

export default function SignUpCard({ serverId, invitationsPath }: SignUpCardProps) {
    const [settings, setSettings] = useState<ServerSettings | null>(null);
    const [savingMode, setSavingMode] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [pin, setPin] = useState<string | null>(null);
    const [inviteEmail, setInviteEmail] = useState('');
    const [inviteStatus, setInviteStatus] = useState<string | null>(null);
    const [copied, setCopied] = useState(false);

    useEffect(() => {
        systemSettingsAdminService.getServerSettings(serverId)
            .then(setSettings)
            .catch(() => setError('Could not load the sign-up setting.'));
    }, [serverId]);

    // Saved on change, like a toggle: the setting has no other field on this
    // page to wait for.
    const changeMode = async (mode: number) => {
        if (!settings) return;
        const previous = settings;
        const next = { ...settings, registrationMode: mode };
        setSettings(next);
        setSavingMode(true);
        setError(null);
        setPin(null);
        setInviteStatus(null);
        try {
            await systemSettingsAdminService.updateServerSettings(next, serverId);
        } catch (err) {
            setSettings(previous);
            setError(resolveReason(err) ?? 'Could not change how people sign up.');
        } finally {
            setSavingMode(false);
        }
    };

    const generatePin = async () => {
        try {
            setPin(await authService.generateInviteCode(serverId));
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not generate a PIN.');
        }
    };

    const sendInvitation = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        const email = inviteEmail.trim();
        if (!email) return;
        setInviteStatus(null);
        setError(null);
        try {
            const result = await invitationsAdminService.create(email, null, serverId);
            setInviteStatus(result.emailSent ? `Invitation sent to ${email}.` : (result.message ?? `Invitation created for ${email}, but the email wasn't sent.`));
            setInviteEmail('');
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not send the invitation.');
        }
    };

    const signUpLink = `${window.location.origin}/register`;
    const copyLink = async () => {
        try {
            await navigator.clipboard.writeText(signUpLink);
            setCopied(true);
        } catch {
            setCopied(false);
        }
    };

    const mode = settings?.registrationMode ?? null;

    return (
        <section className="vora-card p-6 space-y-4" aria-labelledby="sign-up-heading">
            <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                    <h2 id="sign-up-heading" className="text-base font-semibold text-[var(--vora-text-primary)]">New user sign-up</h2>
                    <p className="text-sm text-[var(--vora-text-muted)] mt-0.5">How people other than you get an account. You can always add someone yourself with Add user.</p>
                </div>
                <select
                    aria-label="How people sign up"
                    value={mode ?? ''}
                    disabled={mode === null || savingMode}
                    onChange={e => changeMode(Number(e.target.value))}
                    className="vora-input max-w-sm cursor-pointer"
                >
                    {MODE_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
            </div>

            {mode === RegistrationModes.Disabled && (
                <p className="text-sm text-[var(--vora-text-secondary)]">Nobody can create an account themselves. Add people with <strong>Add user</strong>.</p>
            )}

            {mode === RegistrationModes.Open && (
                <div className="flex flex-wrap items-center gap-3">
                    <p className="text-sm text-[var(--vora-text-secondary)]">Anyone who can reach this server can sign up at</p>
                    <code className="rounded bg-[var(--vora-bg-sunken)] px-2 py-1 text-sm text-[var(--vora-text-primary)]">{signUpLink}</code>
                    <button type="button" onClick={copyLink} className="vora-button-secondary text-xs">{copied ? 'Copied' : 'Copy link'}</button>
                </div>
            )}

            {mode === RegistrationModes.InvitePin && (
                pin ? (
                    <div className="p-5 border-2 border-dashed border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)] rounded-[var(--vora-radius-lg)] text-center max-w-md">
                        <p className="text-xs uppercase tracking-widest font-semibold text-[var(--vora-accent-text)] mb-2">Expires in 30 minutes</p>
                        <p className="text-5xl font-bold tracking-[0.4em] text-[var(--vora-accent-active)] mb-3 font-mono">{pin}</p>
                        <button type="button" onClick={() => setPin(null)} className="text-xs font-semibold text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer">Clear</button>
                    </div>
                ) : (
                    <div className="flex flex-wrap items-center gap-3">
                        <p className="text-sm text-[var(--vora-text-secondary)]">People sign up with a 4-digit PIN from you.</p>
                        <button type="button" onClick={generatePin} className="vora-button-secondary">Generate PIN</button>
                    </div>
                )
            )}

            {mode === RegistrationModes.InvitationOnly && (
                <form onSubmit={sendInvitation} className="flex flex-wrap items-center gap-3">
                    <input
                        type="email"
                        value={inviteEmail}
                        onChange={e => setInviteEmail(e.target.value)}
                        placeholder="Their email address"
                        aria-label="Email address to invite"
                        className="vora-input min-w-0 flex-1 max-w-sm"
                    />
                    <button type="submit" disabled={!inviteEmail.trim()} className="vora-button-secondary disabled:opacity-50">Send invitation</button>
                    <Link to={invitationsPath} className="text-xs font-semibold text-[var(--vora-accent-text)] hover:underline">Manage invitations</Link>
                </form>
            )}

            {inviteStatus && <p role="status" className="text-sm text-[var(--vora-text-secondary)]">{inviteStatus}</p>}
            {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
        </section>
    );
}
