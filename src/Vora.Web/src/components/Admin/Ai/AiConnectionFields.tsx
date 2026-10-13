import type { ReactNode } from 'react';
import AiProviderGuide from './AiProviderGuide';
import ModelPicker from './ModelPicker';
import { AI_KEYS, findAiProvider } from './aiProviders';
import type { AiSettingsForm } from './useAiSettings';

interface AiConnectionFieldsProps {
    form: AiSettingsForm;
    idPrefix: string;
}

function Label({ htmlFor, children }: { htmlFor: string; children: ReactNode }) {
    return <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">{children}</label>;
}

function Help({ children }: { children: ReactNode }) {
    return <p className="mt-1.5 max-w-[78ch] text-xs text-[var(--vora-text-muted)]">{children}</p>;
}

// Where the AI runs and which model does each job. Shared by AI Settings, the
// plugin's panel and the setup guide, so the three cannot drift.
export default function AiConnectionFields({ form, idPrefix }: AiConnectionFieldsProps) {
    const { values, set, models, isOpenAi } = form;
    const provider = findAiProvider(form.providerId);
    const id = (key: string) => `${idPrefix}-${key}`;

    // Until the server has been asked, OpenAI still has its usual names to offer.
    const known = (key: string) => isOpenAi ? form.field(key)?.options ?? [] : [];
    const chatOptions = models && !models.error ? models.chatModels : known(AI_KEYS.chatModel);
    const embeddingOptions = models && !models.error ? models.embeddingModels : known(AI_KEYS.embeddingModel);
    const found = models && !models.error ? models.chatModels.length + models.embeddingModels.length : null;

    return (
        <div className="flex flex-col gap-5">
            <AiProviderGuide selectedId={form.providerId} onSelect={form.applyProvider} />

            <div className="grid gap-4 sm:grid-cols-2">
                <div className="min-w-0">
                    <Label htmlFor={id(AI_KEYS.baseUrl)}>Server address</Label>
                    <input
                        id={id(AI_KEYS.baseUrl)}
                        type="text"
                        inputMode="url"
                        value={values[AI_KEYS.baseUrl] ?? ''}
                        onChange={e => set(AI_KEYS.baseUrl, e.target.value)}
                        placeholder="http://host.docker.internal:11434/v1"
                        className="vora-input font-mono text-sm"
                        autoComplete="off"
                        spellCheck={false}
                    />
                    <Help>{isOpenAi ? "OpenAI's own address. Pick another choice above to change it." : 'Usually ends in /v1. "localhost" here means the Vora container, not your computer.'}</Help>
                </div>
                <div className="min-w-0">
                    <Label htmlFor={id(AI_KEYS.apiKey)}>API key</Label>
                    <input
                        id={id(AI_KEYS.apiKey)}
                        type="password"
                        value={values[AI_KEYS.apiKey] ?? ''}
                        onChange={e => set(AI_KEYS.apiKey, e.target.value)}
                        placeholder={provider?.needsKey ? 'Paste the key' : 'Not needed for most servers'}
                        className="vora-input font-mono text-sm"
                        autoComplete="off"
                    />
                    <Help>{provider?.key}</Help>
                </div>
            </div>

            <div className="flex flex-col gap-4 border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <div className="min-w-0">
                        <p className="text-sm font-semibold text-[var(--vora-text-primary)]">Models</p>
                        <p className="mt-0.5 text-xs text-[var(--vora-text-muted)]">Each job can use its own. A stronger model for collections, a cheaper one for the rest, is a common split.</p>
                    </div>
                    <button type="button" onClick={() => { void form.loadModels(); }} disabled={form.loadingModels} className="vora-button-secondary !px-3 !py-1.5 text-xs">
                        {form.loadingModels ? 'Asking the server…' : found === null ? 'Load models from the server' : 'Reload models'}
                    </button>
                </div>
                {models?.error && <p role="alert" className="text-[13px] text-[var(--vora-danger-text)]">{models.error}</p>}
                {found !== null && <p role="status" className="text-[13px] text-[var(--vora-text-secondary)]">{found === 0 ? 'The server answered but listed no models. Type the names below.' : `${found.toLocaleString()} ${found === 1 ? 'model' : 'models'} found on the server.`}</p>}

                <div className="grid gap-4 sm:grid-cols-2">
                    <div className="min-w-0">
                        <Label htmlFor={id(AI_KEYS.chatModel)}>Recommendations</Label>
                        <ModelPicker id={id(AI_KEYS.chatModel)} value={values[AI_KEYS.chatModel] ?? ''} onChange={v => set(AI_KEYS.chatModel, v)} options={chatOptions} placeholder="Model name, e.g. llama3.1" />
                        <Help>Invents the themed "For You" rows from what each person watches.</Help>
                    </div>
                    <div className="min-w-0">
                        <Label htmlFor={id(AI_KEYS.collectionsModel)}>Collections and story order</Label>
                        <ModelPicker id={id(AI_KEYS.collectionsModel)} value={values[AI_KEYS.collectionsModel] ?? ''} onChange={v => set(AI_KEYS.collectionsModel, v)} options={chatOptions} emptyLabel="Same as recommendations" />
                        <Help>{isOpenAi
                            ? 'Lists a franchise and puts it in story order. gpt-5 is the most accurate; gpt-5-mini costs less.'
                            : 'Lists a franchise and puts it in story order. This leans on knowing films and shows: a small model invents titles and gets the order wrong.'}</Help>
                    </div>
                    <div className="min-w-0">
                        <Label htmlFor={id(AI_KEYS.playlistsModel)}>AI playlists</Label>
                        <ModelPicker id={id(AI_KEYS.playlistsModel)} value={values[AI_KEYS.playlistsModel] ?? ''} onChange={v => set(AI_KEYS.playlistsModel, v)} options={chatOptions} emptyLabel="Same as recommendations" />
                        <Help>Describes how each song feels, once, and reads "make me a playlist for…" requests.</Help>
                    </div>
                    <div className="min-w-0">
                        <Label htmlFor={id(AI_KEYS.embeddingModel)}>Embeddings</Label>
                        <ModelPicker
                            id={id(AI_KEYS.embeddingModel)}
                            value={values[AI_KEYS.embeddingModel] ?? ''}
                            onChange={v => set(AI_KEYS.embeddingModel, v)}
                            options={embeddingOptions}
                            emptyLabel={isOpenAi ? undefined : 'None: recommendations and AI playlists off'}
                            placeholder="Embedding model, e.g. nomic-embed-text"
                        />
                        <Help>Turns titles and songs into the numbers recommendations and AI playlists search by. It can return at most 1536 numbers per item. Changing it means the whole library is read again.</Help>
                    </div>
                </div>
            </div>
        </div>
    );
}
