export const AI_PLUGIN_ID = 'openai_recommendations';

export const AI_KEYS = {
    baseUrl: 'base_url',
    apiKey: 'api_key',
    chatModel: 'chat_model',
    collectionsModel: 'collections_model',
    playlistsModel: 'music_playlists_model',
    embeddingModel: 'embedding_model',
    monthlyTokenLimit: 'monthly_token_limit',
    scheduleTime: 'schedule_time',
    guardrails: 'guardrails',
} as const;

export const MODEL_KEYS = [AI_KEYS.chatModel, AI_KEYS.collectionsModel, AI_KEYS.playlistsModel, AI_KEYS.embeddingModel] as const;

export const OPENAI_BASE_URL = 'https://api.openai.com/v1';

export const OPENAI_MODELS: Record<(typeof MODEL_KEYS)[number], string> = {
    [AI_KEYS.chatModel]: 'gpt-4o-mini',
    [AI_KEYS.collectionsModel]: 'gpt-5',
    [AI_KEYS.playlistsModel]: '',
    [AI_KEYS.embeddingModel]: 'text-embedding-3-small',
};

export interface AiProviderLink {
    label: string;
    url: string;
}

export interface AiProvider {
    id: string;
    name: string;
    kind: 'hosted' | 'local';
    // Filled into the address field. Empty for a server only the admin knows.
    baseUrl: string;
    needsKey: boolean;
    key: string;
    notes: string[];
    links: AiProviderLink[];
}

const DOCKER_NOTE = 'Vora runs in Docker, so "localhost" is the Vora container, not your computer. For a server on the same machine use host.docker.internal; on Linux, add extra_hosts: ["host.docker.internal:host-gateway"] to the Vora service. For another machine, use its network address.';

export const AI_PROVIDERS: AiProvider[] = [
    {
        id: 'openai',
        name: 'OpenAI',
        kind: 'hosted',
        baseUrl: OPENAI_BASE_URL,
        needsKey: true,
        key: 'A secret key that starts with sk-. The account needs billing set up; use is charged per request.',
        notes: [
            'The best results for collections and story order, which lean on knowing films and shows.',
            'A monthly token limit below caps what Vora can spend.',
        ],
        links: [
            { label: 'Create an API key', url: 'https://platform.openai.com/api-keys' },
            { label: 'Set up billing', url: 'https://platform.openai.com/account/billing' },
        ],
    },
    {
        id: 'openrouter',
        name: 'OpenRouter',
        kind: 'hosted',
        baseUrl: 'https://openrouter.ai/api/v1',
        needsKey: true,
        key: 'An OpenRouter API key.',
        notes: [
            'One key for models from many companies. Model names include the company, for example openai/gpt-4o-mini.',
            'Recommendations and AI playlists also need an embedding model from its list.',
        ],
        links: [{ label: 'Create an API key', url: 'https://openrouter.ai/keys' }],
    },
    {
        id: 'ollama',
        name: 'Ollama',
        kind: 'local',
        baseUrl: 'http://host.docker.internal:11434/v1',
        needsKey: false,
        key: 'None. Leave it empty.',
        notes: [
            DOCKER_NOTE,
            'If Vora cannot connect, Ollama is only listening to its own machine. Set OLLAMA_HOST=0.0.0.0 and restart it.',
            'Pull a chat model and an embedding model first, for example "ollama pull llama3.1" and "ollama pull nomic-embed-text".',
        ],
        links: [{ label: 'Ollama model library', url: 'https://ollama.com/library' }],
    },
    {
        id: 'lmstudio',
        name: 'LM Studio',
        kind: 'local',
        baseUrl: 'http://host.docker.internal:1234/v1',
        needsKey: false,
        key: 'None. Leave it empty.',
        notes: [
            DOCKER_NOTE,
            'Start the server on LM Studio\'s Developer tab and turn on "Serve on Local Network".',
            'Download a chat model and an embedding model there first.',
        ],
        links: [{ label: 'LM Studio server guide', url: 'https://lmstudio.ai/docs/developer/core/server' }],
    },
    {
        id: 'custom',
        name: 'Another server',
        kind: 'local',
        baseUrl: '',
        needsKey: false,
        key: 'Only if the server asks for one.',
        notes: [
            'Any server that speaks OpenAI\'s API works: llama.cpp, vLLM, LocalAI, LiteLLM and others. The address usually ends in /v1.',
            DOCKER_NOTE,
            'Collections only need chat. Recommendations and AI playlists also need an embedding model that returns 1536 numbers per item or fewer.',
        ],
        links: [],
    },
];

export const findAiProvider = (id: string): AiProvider | undefined => AI_PROVIDERS.find(p => p.id === id);

function parse(baseUrl: string): URL | null {
    const text = baseUrl.trim();
    if (!text) return null;
    try {
        return new URL(text.includes('://') ? text : `http://${text}`);
    } catch {
        return null;
    }
}

// Which preset an address belongs to. An empty address is OpenAI: that is
// where a server that never saved one sends its requests.
export function detectAiProvider(baseUrl: string): string {
    if (!baseUrl.trim()) return 'openai';
    const url = parse(baseUrl);
    if (!url) return 'custom';
    if (url.hostname === 'api.openai.com') return 'openai';
    if (url.hostname === 'openrouter.ai') return 'openrouter';
    if (url.port === '11434') return 'ollama';
    if (url.port === '1234') return 'lmstudio';
    return 'custom';
}

export const isOpenAiAddress = (baseUrl: string): boolean => detectAiProvider(baseUrl) === 'openai';

// What the address in the form would be reached as, for the summary line.
export function aiHostLabel(baseUrl: string): string {
    const url = parse(baseUrl);
    return url ? url.host : baseUrl.trim();
}
