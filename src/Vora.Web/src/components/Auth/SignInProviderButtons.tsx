import type { SignInProvider } from '../../api/Auth/externalSignInService';

interface SignInProviderButtonsProps {
    providers: SignInProvider[];
    serverUrl: string;
    onChoose: (provider: SignInProvider) => void;
    disabled?: boolean;
}

function iconSource(iconUrl: string, serverUrl: string): string {
    if (!iconUrl.startsWith('/')) return iconUrl;
    return `${serverUrl.replace(/\/+$/, '').replace(/\/api$/, '')}${iconUrl}`;
}

export function ProviderGlyph({ iconUrl, serverUrl }: { iconUrl: string | null; serverUrl: string }) {
    if (iconUrl) {
        return <img src={iconSource(iconUrl, serverUrl)} alt="" aria-hidden="true" className="h-5 w-5 shrink-0 object-contain" />;
    }
    return (
        <svg className="h-5 w-5 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} aria-hidden="true">
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 7a4 4 0 11-3.9 4.9L4 19v2h3v-2h2v-2h2l1.9-1.9A4 4 0 0115 7zm1.5 2.5h.01" />
        </svg>
    );
}

export default function SignInProviderButtons({ providers, serverUrl, onChoose, disabled }: SignInProviderButtonsProps) {
    return (
        <div className="space-y-2.5">
            {providers.map(provider => (
                <button
                    key={provider.id}
                    type="button"
                    disabled={disabled}
                    onClick={() => onChoose(provider)}
                    className="vora-button-secondary flex w-full cursor-pointer items-center justify-center gap-2.5 disabled:opacity-50"
                >
                    <ProviderGlyph iconUrl={provider.iconUrl} serverUrl={serverUrl} />
                    <span className="truncate">Continue with {provider.displayName}</span>
                </button>
            ))}
        </div>
    );
}

export function OrDivider() {
    return (
        <div className="my-5 flex items-center gap-3 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }} role="separator" aria-label="or">
            <span className="h-px flex-1" style={{ background: 'var(--vora-border-strong)' }} />
            or
            <span className="h-px flex-1" style={{ background: 'var(--vora-border-strong)' }} />
        </div>
    );
}
