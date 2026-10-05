import { useCallback, useEffect, useState } from 'react';
import { remoteAccessService, type RemoteAccessStatus, type UpdateRemoteAccessRequest } from '../../../../api/System/remoteAccessService';
import { systemSettingsAdminService } from '../../../../api/System/systemSettingsAdminService';
import { emailAdminService, type EmailSettings } from '../../../../api/System/emailAdminService';
import EmailProviderGuide from '../../../../components/Admin/Settings/Email/EmailProviderGuide';
import SmtpConnectionFields from '../../../../components/Admin/Settings/Email/SmtpConnectionFields';
import { guessEmailProvider } from '../../../../components/Admin/Settings/Email/emailProviders';
import { EMPTY_PASSWORD_DRAFT, tlsModeFromSettings, toEmailUpdate, type SmtpPasswordDraft, type TlsMode } from '../../../../components/Admin/Settings/Email/smtpSettings';
import type { SetupStepProps } from '../setupContext';
import { StepValidationError, useStepSaver } from '../setupSaver';
import { EMAIL_INVITATION_MODE } from '../setupSteps';
import { normalizePublicUrl } from '../publicUrl';
import { CardHeading, CheckRow, ChoiceCard, FieldLabel, Hint, RecommendedBadge, SetupCard, StatusMessage, StepHeading, SwitchRow, WhyNote } from '../SetupParts';

type RemoteChoice = 'home' | 'proxy' | 'port';

const choiceOf = (status: RemoteAccessStatus): RemoteChoice => !status.isEnabled ? 'home' : status.externalUrl ? 'proxy' : 'port';

const BAD_URL = 'Enter the full web address people use to reach Vora, such as https://vora.example.com.';

const REMOTE_CHOICES: { value: RemoteChoice; title: string; description: string; recommended?: boolean }[] = [
    { value: 'home', title: 'Only at home', description: 'Vora works on your home network only. You can open it up later in System Settings → Remote Access.' },
    { value: 'proxy', title: 'Reverse proxy or tunnel', description: 'You already reach this server at a web address, through Nginx Proxy Manager, Caddy, Traefik, a Cloudflare Tunnel, Tailscale Funnel or similar.', recommended: true },
    { value: 'port', title: 'Open a port on my router', description: "Vora asks your router to forward a port (UPnP), or you forward it yourself. People connect to your home's internet address." },
];

export function RemoteAccessStep({ serverId }: SetupStepProps) {
    const [status, setStatus] = useState<RemoteAccessStatus | null>(null);
    const [loadFailed, setLoadFailed] = useState(false);
    const [choice, setChoice] = useState<RemoteChoice>('home');
    const [url, setUrl] = useState('');
    const [manualPort, setManualPort] = useState(false);
    const [port, setPort] = useState(32080);
    const [dirty, setDirty] = useState(false);
    const [checking, setChecking] = useState(false);
    const [checked, setChecked] = useState<RemoteAccessStatus | null>(null);
    const [checkError, setCheckError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        remoteAccessService.getRemoteAccessStatus(serverId)
            .then(loaded => {
                if (cancelled) return;
                setStatus(loaded);
                setChoice(choiceOf(loaded));
                setUrl(loaded.externalUrl ?? '');
                setManualPort(loaded.manuallySpecifyPort);
                setPort(loaded.publicPort);
            })
            .catch(() => { if (!cancelled) setLoadFailed(true); });
        return () => { cancelled = true; };
    }, [serverId]);

    const request = useCallback((): UpdateRemoteAccessRequest => {
        if (choice === 'proxy') {
            const externalUrl = normalizePublicUrl(url);
            if (!externalUrl) throw new StepValidationError(BAD_URL);
            return { isEnabled: true, manuallySpecifyPort: manualPort, publicPort: port, externalUrl };
        }
        return {
            isEnabled: choice === 'port',
            manuallySpecifyPort: manualPort,
            publicPort: port,
            externalUrl: choice === 'port' ? null : status?.externalUrl ?? null,
        };
    }, [choice, url, manualPort, port, status]);

    useStepSaver(useCallback(async () => {
        if (!dirty || !status) return;
        const saved = await remoteAccessService.updateRemoteAccess(request(), serverId);
        setStatus(saved);
        setDirty(false);
    }, [dirty, status, request, serverId]));

    const change = (apply: () => void) => {
        apply();
        setDirty(true);
        setChecked(null);
        setCheckError(null);
    };

    const checkNow = async () => {
        setChecking(true);
        setChecked(null);
        setCheckError(null);
        try {
            const saved = await remoteAccessService.updateRemoteAccess(request(), serverId);
            setStatus(saved);
            setChecked(saved);
            setDirty(false);
        } catch (err) {
            setCheckError(err instanceof StepValidationError ? err.message : "Couldn't save the address. Try again in a moment.");
        } finally {
            setChecking(false);
        }
    };

    return (
        <>
            <StepHeading eyebrow="Access" title="Remote access" lead="Decide whether Vora can be used away from home, and how people reach it." />
            {loadFailed && <StatusMessage tone="error">Couldn't load the remote access settings. You can set them later in System Settings → Remote Access.</StatusMessage>}
            {!status && !loadFailed && <div className="vora-skeleton h-40" />}
            {status && (
                <>
                    <div role="radiogroup" aria-label="How do people reach Vora from outside your home?" className="grid gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
                        {REMOTE_CHOICES.map(c => (
                            <ChoiceCard
                                key={c.value}
                                selected={choice === c.value}
                                onSelect={() => change(() => setChoice(c.value))}
                                title={c.title}
                                badge={c.recommended ? <RecommendedBadge /> : undefined}
                                description={c.description}
                            />
                        ))}
                    </div>
                    {choice === 'proxy' && (
                        <SetupCard>
                            <div>
                                <FieldLabel htmlFor="setup-remote-url">Public address</FieldLabel>
                                <input id="setup-remote-url" type="url" value={url} onChange={e => change(() => setUrl(e.target.value))} placeholder="https://vora.example.com" className="vora-input max-w-md" autoComplete="off" />
                                <Hint>The address people type to reach Vora from outside, including https://. Point your proxy or tunnel at the address you open Vora on at home. Vora skips router port forwarding when this is set.</Hint>
                            </div>
                            <div className="flex flex-wrap items-center gap-3">
                                <button type="button" onClick={checkNow} disabled={checking} className="vora-button-secondary !px-3 !py-1.5 text-xs">{checking ? 'Checking…' : 'Save and check'}</button>
                                {checkError && <StatusMessage tone="error">{checkError}</StatusMessage>}
                                {checked && (checked.reachable
                                    ? <StatusMessage tone="ok">Vora answered at {checked.externalUrl ?? checked.accessUrl}.</StatusMessage>
                                    : <StatusMessage tone="info">Vora couldn't reach that address from the server. That can still be fine, because many routers don't loop traffic back inside the network. Try it on your phone with Wi-Fi off.</StatusMessage>)}
                            </div>
                        </SetupCard>
                    )}
                    {choice === 'port' && (
                        <SetupCard>
                            <CheckRow
                                id="setup-remote-manual-port"
                                checked={manualPort}
                                onChange={on => change(() => setManualPort(on))}
                                label="My router needs the port forwarded by hand"
                                hint="Leave this off if your router supports UPnP. Vora then opens the port itself."
                            />
                            {manualPort && (
                                <div>
                                    <FieldLabel htmlFor="setup-remote-port">Public port</FieldLabel>
                                    <input id="setup-remote-port" type="number" min={1} max={65535} value={port} onChange={e => change(() => setPort(parseInt(e.target.value, 10) || 32080))} className="vora-input w-32" />
                                    <Hint>Forward this port on your router to the Vora server.</Hint>
                                </div>
                            )}
                        </SetupCard>
                    )}
                    {choice !== 'home' && (
                        <WhyNote>
                            Email links, such as invitations and password resets, use this address too. Keep Vora's sign-up setting on the next step in mind: anything reachable from the internet should not let just anyone sign up.
                        </WhyNote>
                    )}
                </>
            )}
        </>
    );
}

const SIGN_UP_MODES: { value: number; title: string; description: string }[] = [
    { value: 0, title: 'Only I add people', description: 'Nobody can create an account themselves. You add each person under Users & Access.' },
    { value: 2, title: 'Sign up with a PIN', description: 'People sign up with a 4-digit PIN you make for them. Each PIN expires after 30 minutes.' },
    { value: EMAIL_INVITATION_MODE, title: 'Email invitation', description: "You enter someone's email address and Vora sends them a sign-up link that works once. Needs email." },
    { value: 1, title: 'Anyone can sign up', description: 'Anyone who can reach this server can make an account. Only sensible on a private network.' },
];

export function SignUpStep({ serverId, settings, onSettings, emailWanted, onEmailWanted }: SetupStepProps) {
    const [savedMode] = useState(settings.registrationMode);
    const mode = settings.registrationMode;

    useStepSaver(useCallback(async () => {
        if (mode === savedMode) return;
        await systemSettingsAdminService.updateRegistrationMode(mode, serverId);
    }, [mode, savedMode, serverId]));

    return (
        <>
            <StepHeading eyebrow="Access" title="Sign-ups" lead="How people other than you get an account. You can always add someone yourself." />
            <div role="radiogroup" aria-label="How do people sign up?" className="grid gap-2.5 sm:grid-cols-2">
                {SIGN_UP_MODES.map(m => (
                    <ChoiceCard key={m.value} selected={mode === m.value} onSelect={() => onSettings({ registrationMode: m.value })} title={m.title} description={m.description} />
                ))}
            </div>
            {mode === EMAIL_INVITATION_MODE
                ? <WhyNote>Invitations are sent by email, so the next step sets email up.</WhyNote>
                : (
                    <SetupCard>
                        <SwitchRow
                            id="setup-email-wanted"
                            label="Set up email too"
                            description="Email lets people reset a forgotten password and hear when something they asked for is ready."
                            checked={emailWanted}
                            onChange={onEmailWanted}
                        />
                    </SetupCard>
                )}
            <Hint>You can change this any time under Users & Access.</Hint>
        </>
    );
}

const missing = (value: string | null) => !value || !value.trim();

export function EmailStep({ serverId, settings }: SetupStepProps) {
    const [email, setEmail] = useState<EmailSettings | null>(null);
    const [loadFailed, setLoadFailed] = useState(false);
    const [tlsMode, setTlsMode] = useState<TlsMode>('startTls');
    const [password, setPassword] = useState<SmtpPasswordDraft>(EMPTY_PASSWORD_DRAFT);
    const [dirty, setDirty] = useState(false);
    const [filledUrl, setFilledUrl] = useState(false);
    const [testTo, setTestTo] = useState('');
    const [testing, setTesting] = useState(false);
    const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
    const invitations = settings.registrationMode === EMAIL_INVITATION_MODE;

    useEffect(() => {
        let cancelled = false;
        Promise.all([
            emailAdminService.getSettings(serverId),
            remoteAccessService.getRemoteAccessStatus(serverId).then(s => s.isEnabled ? s.externalUrl ?? null : null).catch(() => null),
        ]).then(([loaded, publicUrl]) => {
            if (cancelled) return;
            const fillUrl = missing(loaded.emailPublicBaseUrl) && !!publicUrl;
            setEmail({
                ...loaded,
                emailEnabled: loaded.emailEnabled || missing(loaded.smtpHost),
                emailPublicBaseUrl: fillUrl ? publicUrl : loaded.emailPublicBaseUrl,
            });
            setTlsMode(tlsModeFromSettings(loaded));
            setFilledUrl(fillUrl);
        }).catch(() => { if (!cancelled) setLoadFailed(true); });
        return () => { cancelled = true; };
    }, [serverId]);

    const patch = (next: Partial<EmailSettings>) => {
        setEmail(current => current ? { ...current, ...next } : current);
        setDirty(true);
        setTestResult(null);
    };

    const save = useCallback(async () => {
        if (!email) return;
        if (email.emailEnabled && missing(email.smtpHost)) throw new StepValidationError("Enter your email provider's SMTP server, or turn email off for now.");
        if (email.emailEnabled && missing(email.smtpFromAddress)) throw new StepValidationError('Enter the address emails are sent from.');
        await emailAdminService.updateSettings(toEmailUpdate(email, tlsMode, password), serverId);
        setDirty(false);
    }, [email, tlsMode, password, serverId]);

    useStepSaver(useCallback(async () => {
        if (dirty) await save();
    }, [dirty, save]));

    const sendTest = async () => {
        setTesting(true);
        setTestResult(null);
        try {
            await save();
            const reloaded = await emailAdminService.getSettings(serverId);
            setEmail(current => current ? { ...current, smtpPasswordIsSet: reloaded.smtpPasswordIsSet } : current);
            setPassword(EMPTY_PASSWORD_DRAFT);
            const result = await emailAdminService.sendTest(testTo.trim(), serverId);
            setTestResult({ success: result.success, message: result.message ?? (result.success ? `Sent. Check ${testTo.trim()}.` : "The test didn't go through.") });
        } catch (err) {
            setTestResult({ success: false, message: err instanceof StepValidationError ? err.message : "Couldn't send the test. Check the settings and try again." });
        } finally {
            setTesting(false);
        }
    };

    const lead = invitations
        ? 'Invitations go out by email, so Vora needs an account to send from. The same settings let people reset a forgotten password and hear when a request is ready.'
        : 'Email lets people reset a forgotten password, receive invitations and hear when something they asked for is ready.';

    return (
        <>
            <StepHeading eyebrow="Access" title="Email" lead={lead} />
            {loadFailed && <StatusMessage tone="error">Couldn't load the email settings. You can set them later in System Settings → Email.</StatusMessage>}
            {!email && !loadFailed && <div className="vora-skeleton h-40" />}
            {email && (
                <>
                    <SetupCard>
                        <SwitchRow id="setup-email-on" label="Send email from this server" checked={email.emailEnabled} onChange={on => patch({ emailEnabled: on })} />
                        {!email.emailEnabled && invitations && (
                            <StatusMessage tone="info">Without email, invitations can't be sent. Go back and pick another way to sign up, or turn email on.</StatusMessage>
                        )}
                    </SetupCard>
                    {email.emailEnabled && (
                        <>
                            <SetupCard>
                                <EmailProviderGuide
                                    initialProviderId={guessEmailProvider(email.smtpHost)}
                                    onApply={smtp => {
                                        patch({ smtpHost: smtp.host, smtpPort: smtp.port });
                                        setTlsMode(smtp.security);
                                    }}
                                />
                            </SetupCard>
                            <SetupCard>
                                <SmtpConnectionFields
                                    idPrefix="setup-email"
                                    settings={email}
                                    onChange={patch}
                                    tlsMode={tlsMode}
                                    onTlsMode={mode => { setTlsMode(mode); setDirty(true); }}
                                    password={password}
                                    onPassword={next => { setPassword(current => ({ ...current, ...next })); setDirty(true); }}
                                    publicUrlHint={filledUrl ? 'Filled in from Remote access. Used to build the links in emails, such as invitations and password resets.' : undefined}
                                />
                            </SetupCard>
                            <SetupCard>
                                <CardHeading title="Send a test" subtitle="Saves these settings and sends a short test message." />
                                <div className="flex flex-col gap-3 sm:flex-row">
                                    <label htmlFor="setup-email-test" className="sr-only">Send the test to</label>
                                    <input id="setup-email-test" type="email" value={testTo} onChange={e => setTestTo(e.target.value)} placeholder="you@example.com" className="vora-input flex-1" />
                                    <button type="button" onClick={sendTest} disabled={testing || !testTo.trim()} className="vora-button-secondary disabled:opacity-50">
                                        {testing ? 'Sending…' : 'Save and send test'}
                                    </button>
                                </div>
                                {testResult && <StatusMessage tone={testResult.success ? 'ok' : 'error'}>{testResult.message}</StatusMessage>}
                            </SetupCard>
                        </>
                    )}
                </>
            )}
        </>
    );
}
