import type { PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import { AI_KEYS } from './aiProviders';

const OPENAI_CHAT = ['gpt-4o-mini', 'gpt-4o', 'gpt-4.1-mini', 'gpt-4.1', 'gpt-5-nano', 'gpt-5-mini', 'gpt-5'];

const field = (key: string, label: string, type: string, value: string, options: string[] = []): PluginSettingField => ({
    key, label, type, description: '', value, placeholder: '', required: false, options,
});

// The AI plugin's settings as the server sends them to a server that never
// saved any: OpenAI's address and the models it has always used.
export function aiFields(saved: Record<string, string> = {}): PluginSettingField[] {
    const value = (key: string, fallback: string) => saved[key] ?? fallback;
    return [
        field('is_enabled', 'Enable Plugin', 'boolean', 'true'),
        field(AI_KEYS.baseUrl, 'Server Address', 'text', value(AI_KEYS.baseUrl, 'https://api.openai.com/v1')),
        field(AI_KEYS.apiKey, 'API Key', 'password', value(AI_KEYS.apiKey, '')),
        field(AI_KEYS.chatModel, 'Recommendations Model', 'model', value(AI_KEYS.chatModel, 'gpt-4o-mini'), OPENAI_CHAT),
        field(AI_KEYS.collectionsModel, 'AI Collections Model', 'model', value(AI_KEYS.collectionsModel, 'gpt-5'), OPENAI_CHAT),
        field(AI_KEYS.playlistsModel, 'AI Playlists Model', 'model', value(AI_KEYS.playlistsModel, ''), OPENAI_CHAT),
        field(AI_KEYS.embeddingModel, 'Embedding Model', 'model', value(AI_KEYS.embeddingModel, 'text-embedding-3-small'), ['text-embedding-3-small']),
        field(AI_KEYS.scheduleTime, 'Nightly Vector Generation Time', 'time', value(AI_KEYS.scheduleTime, '02:00')),
        field(AI_KEYS.monthlyTokenLimit, 'Monthly Token Limit', 'number', value(AI_KEYS.monthlyTokenLimit, '0')),
        field(AI_KEYS.guardrails, 'Guardrails', 'textarea', value(AI_KEYS.guardrails, '')),
    ];
}

export const OPENAI_DEFAULTS: Record<string, string> = {
    [AI_KEYS.baseUrl]: 'https://api.openai.com/v1',
    [AI_KEYS.apiKey]: '',
    [AI_KEYS.chatModel]: 'gpt-4o-mini',
    [AI_KEYS.collectionsModel]: 'gpt-5',
    [AI_KEYS.playlistsModel]: '',
    [AI_KEYS.embeddingModel]: 'text-embedding-3-small',
    [AI_KEYS.scheduleTime]: '02:00',
    [AI_KEYS.monthlyTokenLimit]: '0',
    [AI_KEYS.guardrails]: '',
};
