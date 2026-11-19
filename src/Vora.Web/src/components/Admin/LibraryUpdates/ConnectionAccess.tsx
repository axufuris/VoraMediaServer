import { useState } from 'react';
import { FieldLabel, Hint } from '../../../pages/Admin/SetupGuide/SetupParts';
import { copyText } from '../../../utils/copyText';
import { FOCUS_FILL } from './libraryUpdates';

const BUTTON = `vora-button-secondary shrink-0 text-xs ${FOCUS_FILL}`;

interface AddressFieldProps {
    url: string;
    providerName: string;
}

export function AddressField({ url, providerName }: AddressFieldProps) {
    const [copied, setCopied] = useState(false);

    const copy = async () => {
        setCopied(await copyText(url));
    };

    return (
        <div>
            <FieldLabel htmlFor="webhook-address">Address to paste into {providerName}</FieldLabel>
            <div className="flex items-center gap-2 max-md:flex-wrap">
                <input
                    id="webhook-address"
                    type="text"
                    readOnly
                    value={url}
                    onFocus={e => e.currentTarget.select()}
                    className="vora-input min-w-0 flex-1 font-mono text-xs max-md:basis-full"
                />
                <button type="button" onClick={copy} aria-label={copied ? 'Address copied' : 'Copy address'} className={BUTTON}>
                    {copied ? 'Copied' : 'Copy'}
                </button>
            </div>
            <Hint>The app must be able to reach this address. Inside Docker that is usually the container name and port, not localhost.</Hint>
        </div>
    );
}

interface SecretFieldProps {
    secret: string | null;
    loadError?: string | null;
    initiallyRevealed?: boolean;
    regenerating?: boolean;
    onRegenerate?: () => Promise<boolean>;
}

export function SecretField({ secret, loadError = null, initiallyRevealed = false, regenerating = false, onRegenerate }: SecretFieldProps) {
    const [revealed, setRevealed] = useState(initiallyRevealed);
    const [copiedSecret, setCopiedSecret] = useState<string | null>(null);
    const copied = secret !== null && copiedSecret === secret;

    const copy = async () => {
        if (secret !== null && await copyText(secret)) setCopiedSecret(secret);
    };

    const regenerate = async () => {
        if (!onRegenerate) return;
        if (await onRegenerate()) setRevealed(true);
    };

    return (
        <div>
            <FieldLabel htmlFor="webhook-secret">Secret</FieldLabel>
            <div className="flex items-center gap-2 max-md:flex-wrap">
                <input
                    id="webhook-secret"
                    type={revealed ? 'text' : 'password'}
                    readOnly
                    autoComplete="off"
                    value={secret ?? ''}
                    placeholder={secret === null && !loadError ? 'Loading…' : ''}
                    onFocus={e => e.currentTarget.select()}
                    className="vora-input min-w-0 flex-1 font-mono text-xs max-md:basis-full"
                />
                <button type="button" onClick={() => setRevealed(value => !value)} disabled={secret === null} aria-label={revealed ? 'Hide secret' : 'Reveal secret'} className={`${BUTTON} disabled:opacity-50`}>
                    {revealed ? 'Hide' : 'Reveal'}
                </button>
                <button type="button" onClick={copy} disabled={secret === null} aria-label={copied ? 'Secret copied' : 'Copy secret'} className={`${BUTTON} disabled:opacity-50`}>
                    {copied ? 'Copied' : 'Copy'}
                </button>
                {onRegenerate && (
                    <button type="button" onClick={regenerate} disabled={regenerating} aria-label="Regenerate secret" className={`${BUTTON} disabled:opacity-50`}>
                        {regenerating ? 'Regenerating…' : 'Regenerate'}
                    </button>
                )}
            </div>
            {loadError && <p role="alert" className="mt-1.5 text-xs text-[var(--vora-danger-text)]">{loadError}</p>}
        </div>
    );
}
