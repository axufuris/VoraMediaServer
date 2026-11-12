import { useState, type ReactNode } from 'react';
import type { IptvChannelKind, IptvConnectionTestVM, IptvSourceType } from '../../../api/Iptv/iptvAdminService';
import { describeTestResult, hasAdvancedValues, missingSourceField, type IptvSourceDraft, type PasswordDraft } from './iptvSourceDraft';

interface IptvSourceFieldsProps {
    idPrefix: string;
    kind: IptvChannelKind;
    draft: IptvSourceDraft;
    onChange: (patch: Partial<IptvSourceDraft>) => void;
    passwordIsSet?: boolean;
    onTest: () => Promise<IptvConnectionTestVM>;
}

const SOURCE_OPTIONS: { value: IptvSourceType; label: string }[] = [
    { value: 'M3u', label: 'M3U playlist' },
    { value: 'Xtream', label: 'Xtream Codes account' },
];

const FILL_BUTTON = 'cursor-pointer rounded-[var(--vora-radius-md)] transition-colors hover:bg-[var(--vora-accent-soft)] hover:text-[var(--vora-accent-text)] focus-visible:bg-[var(--vora-accent-soft)] focus-visible:text-[var(--vora-accent-text)] focus-visible:outline-none';

function FieldLabel({ htmlFor, children }: { htmlFor?: string; children: ReactNode }) {
    return <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">{children}</label>;
}

function FieldHint({ children }: { children: ReactNode }) {
    return <p className="mt-1.5 text-xs text-[var(--vora-text-muted)]">{children}</p>;
}

function SourceSelector({ idPrefix, value, onChange }: { idPrefix: string; value: IptvSourceType; onChange: (value: IptvSourceType) => void }) {
    return (
        <div role="radiogroup" aria-labelledby={`${idPrefix}-source-label`} className="inline-flex flex-wrap gap-1 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-strong)] bg-[var(--vora-bg-sunken)] p-1">
            {SOURCE_OPTIONS.map(option => {
                const selected = option.value === value;
                return (
                    <button
                        key={option.value}
                        type="button"
                        role="radio"
                        aria-checked={selected}
                        onClick={() => onChange(option.value)}
                        className={`px-3 py-1.5 text-sm font-semibold ${FILL_BUTTON} ${selected ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]' : 'text-[var(--vora-text-secondary)]'}`}
                    >
                        {option.label}
                    </button>
                );
            })}
        </div>
    );
}

function PasswordField({ id, label, isSet, draft, onChange, placeholder }: { id: string; label: string; isSet: boolean; draft: PasswordDraft; onChange: (patch: Partial<PasswordDraft>) => void; placeholder: string }) {
    return (
        <div>
            <FieldLabel htmlFor={id}>{label}</FieldLabel>
            {draft.clear ? (
                <div className="flex h-[38px] items-center gap-3">
                    <span className="text-sm text-[var(--vora-danger-text)]">Will be cleared on save.</span>
                    <button type="button" onClick={() => onChange({ clear: false })} className={`px-1.5 py-0.5 text-xs text-[var(--vora-accent-text)] ${FILL_BUTTON}`}>Undo</button>
                </div>
            ) : isSet && !draft.editing ? (
                <div className="flex h-[38px] items-center gap-3">
                    <span className="text-sm text-[var(--vora-text-secondary)]">A password is saved.</span>
                    <button type="button" onClick={() => onChange({ editing: true })} className={`px-1.5 py-0.5 text-xs text-[var(--vora-accent-text)] ${FILL_BUTTON}`}>Change</button>
                    <button type="button" onClick={() => onChange({ clear: true })} className={`px-1.5 py-0.5 text-xs text-[var(--vora-danger-text)] ${FILL_BUTTON}`}>Clear</button>
                </div>
            ) : (
                <div className="flex items-center gap-2">
                    <input
                        id={id}
                        type="password"
                        value={draft.value}
                        onChange={e => onChange({ value: e.target.value })}
                        placeholder={isSet ? 'New password' : placeholder}
                        className="vora-input flex-1"
                        autoComplete="new-password"
                    />
                    {isSet && (
                        <button type="button" onClick={() => onChange({ editing: false, value: '' })} className={`px-1.5 py-0.5 text-xs text-[var(--vora-text-muted)] ${FILL_BUTTON}`}>Cancel</button>
                    )}
                </div>
            )}
        </div>
    );
}

export default function IptvSourceFields({ idPrefix, kind, draft, onChange, passwordIsSet = false, onTest }: IptvSourceFieldsProps) {
    const id = (name: string) => `${idPrefix}-${name}`;
    const isXtream = draft.sourceType === 'Xtream';
    const unit = kind === 'Radio' ? 'stations' : 'channels';
    const [showAdvanced, setShowAdvanced] = useState(() => hasAdvancedValues(draft, passwordIsSet));
    const [testing, setTesting] = useState(false);
    const [testResult, setTestResult] = useState<{ ok: boolean; message: string } | null>(null);

    const change = (patch: Partial<IptvSourceDraft>) => {
        setTestResult(null);
        onChange(patch);
    };
    const changePassword = (patch: Partial<PasswordDraft>) => change({ password: { ...draft.password, ...patch } });
    const missing = missingSourceField(draft, passwordIsSet);

    const test = async () => {
        setTesting(true);
        setTestResult(null);
        try {
            const result = await onTest();
            setTestResult({ ok: result.succeeded, message: describeTestResult(result, unit) });
        } catch {
            setTestResult({ ok: false, message: 'The test could not be run. Check the fields and try again.' });
        } finally {
            setTesting(false);
        }
    };

    return (
        <div className="flex flex-col gap-4">
            <div>
                <span id={id('source-label')} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Source</span>
                <SourceSelector idPrefix={idPrefix} value={draft.sourceType} onChange={value => change({ sourceType: value })} />
            </div>

            {isXtream ? (
                <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                    <div className="md:col-span-3">
                        <FieldLabel htmlFor={id('server')}>Server address</FieldLabel>
                        <input id={id('server')} type="text" value={draft.serverUrl} onChange={e => change({ serverUrl: e.target.value })} className="vora-input font-mono text-sm" placeholder="http://provider.example.com:8080" autoComplete="off" />
                        <FieldHint>The address your provider gave you, with its port. Paths like /player_api.php are removed.</FieldHint>
                    </div>
                    <div>
                        <FieldLabel htmlFor={id('username')}>Username</FieldLabel>
                        <input id={id('username')} type="text" value={draft.username} onChange={e => change({ username: e.target.value })} className="vora-input" autoComplete="off" />
                    </div>
                    <div className="md:col-span-2">
                        <PasswordField id={id('password')} label="Password" isSet={passwordIsSet} draft={draft.password} onChange={changePassword} placeholder="Account password" />
                    </div>
                    <p className="text-xs text-[var(--vora-text-muted)] md:col-span-3">The password is encrypted with this server's data-protection keys. Apps never see it, or the provider's address.</p>
                </div>
            ) : (
                <div>
                    <FieldLabel htmlFor={id('m3u')}>M3U URL</FieldLabel>
                    <input id={id('m3u')} type="text" value={draft.m3uUrl} onChange={e => change({ m3uUrl: e.target.value })} className="vora-input font-mono text-sm" placeholder="https://…" autoComplete="off" />
                </div>
            )}

            {isXtream && kind === 'Tv' && (
                <label className="flex cursor-pointer select-none items-center gap-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-3">
                    <input type="checkbox" checked={draft.useProviderGuide} onChange={e => change({ useProviderGuide: e.target.checked })} className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
                    <div className="flex flex-col">
                        <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Use the provider's guide</span>
                        <span className="text-xs text-[var(--vora-text-muted)]">Reads the account's own TV guide in each guide sync. Your EPG sources come first; the provider's guide fills in the channels they don't cover.</span>
                    </div>
                </label>
            )}

            <div>
                <button
                    type="button"
                    aria-expanded={showAdvanced}
                    aria-controls={id('advanced')}
                    onClick={() => setShowAdvanced(open => !open)}
                    className={`inline-flex items-center gap-1.5 px-2 py-1 text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] ${FILL_BUTTON}`}
                >
                    <svg className={`h-3 w-3 transition-transform ${showAdvanced ? 'rotate-90' : ''}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true"><path d="M9 6l6 6-6 6" /></svg>
                    Advanced
                </button>
                {showAdvanced && (
                    <div id={id('advanced')} className="mt-3 grid grid-cols-1 gap-4 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-4 md:grid-cols-2">
                        {!isXtream && (
                            <>
                                <div>
                                    <FieldLabel htmlFor={id('http-username')}>HTTP username</FieldLabel>
                                    <input id={id('http-username')} type="text" value={draft.username} onChange={e => change({ username: e.target.value })} className="vora-input" autoComplete="off" />
                                </div>
                                <PasswordField id={id('http-password')} label="HTTP password" isSet={passwordIsSet} draft={draft.password} onChange={changePassword} placeholder="Only if the playlist asks for one" />
                                <p className="text-xs text-[var(--vora-text-muted)] md:col-span-2">Sent as a login only to the playlist's own host, never to other servers.</p>
                            </>
                        )}
                        <div>
                            <FieldLabel htmlFor={id('user-agent')}>User-Agent</FieldLabel>
                            <input id={id('user-agent')} type="text" value={draft.userAgent} onChange={e => change({ userAgent: e.target.value })} className="vora-input font-mono text-sm" placeholder="Leave blank for the default" autoComplete="off" />
                        </div>
                        <div>
                            <FieldLabel htmlFor={id('referer')}>Referer</FieldLabel>
                            <input id={id('referer')} type="text" value={draft.referer} onChange={e => change({ referer: e.target.value })} className="vora-input font-mono text-sm" placeholder="Leave blank for none" autoComplete="off" />
                        </div>
                        <p className="text-xs text-[var(--vora-text-muted)] md:col-span-2">Only if the provider asks for them. A channel that names its own in the playlist keeps it.</p>
                    </div>
                )}
            </div>

            <div className="flex flex-wrap items-center gap-3">
                <button type="button" onClick={test} disabled={testing || missing !== null} className="vora-button-secondary !px-3 !py-1.5 text-xs disabled:cursor-not-allowed disabled:opacity-50">
                    {testing ? 'Testing…' : 'Test connection'}
                </button>
                {testResult && (
                    <span role="status" className={`text-sm ${testResult.ok ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-danger-text)]'}`}>{testResult.message}</span>
                )}
                {!testResult && missing && (draft.m3uUrl || draft.serverUrl || draft.username) && (
                    <span className="text-xs text-[var(--vora-text-muted)]">{missing}</span>
                )}
            </div>
        </div>
    );
}
