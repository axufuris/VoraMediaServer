import { describe, it, expect } from 'vitest';
import { AI_KEYS, AI_PROVIDERS, MODEL_KEYS, OPENAI_BASE_URL, OPENAI_MODELS, aiHostLabel, detectAiProvider, findAiProvider, isOpenAiAddress } from './aiProviders';

describe('detectAiProvider', () => {
    it.each([
        ['', 'openai'],
        ['   ', 'openai'],
        ['https://api.openai.com/v1', 'openai'],
        ['https://openrouter.ai/api/v1', 'openrouter'],
        ['http://host.docker.internal:11434/v1', 'ollama'],
        ['192.168.1.20:11434', 'ollama'],
        ['http://host.docker.internal:1234/v1', 'lmstudio'],
        ['http://llm.example.com:8000/v1', 'custom'],
        ['http://not a host', 'custom'],
    ])('reads %j as %s', (address, expected) => {
        expect(detectAiProvider(address)).toBe(expected);
    });

    it('treats an address nobody saved as OpenAI, where such a server sends its requests', () => {
        expect(isOpenAiAddress('')).toBe(true);
        expect(isOpenAiAddress('http://host.docker.internal:11434/v1')).toBe(false);
    });
});

describe('the AI presets', () => {
    it('each have their own id and are found by it', () => {
        const ids = AI_PROVIDERS.map(p => p.id);

        expect(new Set(ids).size).toBe(ids.length);
        ids.forEach(id => expect(findAiProvider(id)?.id).toBe(id));
    });

    it('recognise the address they fill in as their own', () => {
        AI_PROVIDERS.filter(p => p.baseUrl).forEach(p => expect(detectAiProvider(p.baseUrl)).toBe(p.id));
    });

    it('leave the address of a server only the admin knows empty', () => {
        expect(findAiProvider('custom')?.baseUrl).toBe('');
    });

    it('ask for a key only where the service needs one', () => {
        expect(AI_PROVIDERS.filter(p => p.needsKey).map(p => p.id)).toEqual(['openai', 'openrouter']);
    });

    it('keep OpenAI on the address and models a server has always used', () => {
        expect(findAiProvider('openai')?.baseUrl).toBe(OPENAI_BASE_URL);
        expect(MODEL_KEYS.map(key => OPENAI_MODELS[key])).toEqual(['gpt-4o-mini', 'gpt-5', '', 'text-embedding-3-small']);
        expect(OPENAI_MODELS[AI_KEYS.playlistsModel]).toBe('');
    });
});

describe('aiHostLabel', () => {
    it.each([
        ['http://host.docker.internal:11434/v1', 'host.docker.internal:11434'],
        ['192.168.1.20:1234', '192.168.1.20:1234'],
        ['https://openrouter.ai/api/v1', 'openrouter.ai'],
    ])('shows %j as %s', (address, expected) => {
        expect(aiHostLabel(address)).toBe(expected);
    });
});
