import { AI_PROVIDERS, findAiProvider, type AiProvider } from './aiProviders';

interface AiProviderGuideProps {
    selectedId: string;
    onSelect: (provider: AiProvider) => void;
}

function ProviderButton({ provider, selected, onSelect }: { provider: AiProvider; selected: boolean; onSelect: () => void }) {
    return (
        <button
            type="button"
            aria-pressed={selected}
            onClick={onSelect}
            className={`cursor-pointer rounded-full border px-3 py-1 text-[13px] font-medium transition-colors ${selected
                ? 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]'
                : 'border-[var(--vora-border-strong)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)]'}`}
        >
            {provider.name}
        </button>
    );
}

// Picking one fills in its address and says what to enter, the way the email
// provider guide does for SMTP.
export default function AiProviderGuide({ selectedId, onSelect }: AiProviderGuideProps) {
    const provider = findAiProvider(selectedId);
    const hosted = AI_PROVIDERS.filter(p => p.kind === 'hosted');
    const local = AI_PROVIDERS.filter(p => p.kind === 'local');

    return (
        <div className="flex flex-col gap-3">
            <div>
                <p className="text-sm font-semibold text-[var(--vora-text-primary)]">Where does the AI run?</p>
                <p className="mt-0.5 text-xs text-[var(--vora-text-muted)]">
                    A service is quickest and gives the best results. A model on your own computer costs nothing per use and keeps what your household watches at home.
                </p>
            </div>
            <div role="group" aria-label="AI services" className="flex flex-wrap items-center gap-1.5">
                <span className="mr-1 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--vora-text-muted)]">Services</span>
                {hosted.map(p => <ProviderButton key={p.id} provider={p} selected={p.id === selectedId} onSelect={() => onSelect(p)} />)}
            </div>
            <div role="group" aria-label="Your own server" className="flex flex-wrap items-center gap-1.5">
                <span className="mr-1 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--vora-text-muted)]">Your own</span>
                {local.map(p => <ProviderButton key={p.id} provider={p} selected={p.id === selectedId} onSelect={() => onSelect(p)} />)}
            </div>
            {provider && (
                <div className="flex flex-col gap-3 rounded-[var(--vora-radius-lg)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-4">
                    <dl className="grid gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[max-content_minmax(0,1fr)]">
                        {provider.baseUrl && (
                            <div className="contents">
                                <dt className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] sm:pt-0.5">Address</dt>
                                <dd className="min-w-0 break-all font-mono text-[13px] text-[var(--vora-text-primary)]">{provider.baseUrl}</dd>
                            </div>
                        )}
                        <div className="contents">
                            <dt className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] sm:pt-0.5">API key</dt>
                            <dd className="min-w-0 text-[var(--vora-text-primary)]">{provider.key}</dd>
                        </div>
                    </dl>
                    <ul className="list-disc space-y-1 pl-5 text-[13px] text-[var(--vora-text-secondary)]">
                        {provider.notes.map(note => <li key={note}>{note}</li>)}
                    </ul>
                    {provider.links.length > 0 && (
                        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                            {provider.links.map(link => (
                                <a key={link.url} href={link.url} target="_blank" rel="noopener noreferrer" className="text-[13px] font-semibold text-[var(--vora-accent-text)] hover:underline">
                                    {link.label} ↗
                                </a>
                            ))}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}
