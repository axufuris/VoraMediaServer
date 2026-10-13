import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { systemSettingsAdminService, type PluginConnectionTestResult, type PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import { aiSettingsService, type AiModels } from '../../../api/System/aiSettingsService';
import { AI_KEYS, AI_PLUGIN_ID, MODEL_KEYS, OPENAI_BASE_URL, OPENAI_MODELS, detectAiProvider, isOpenAiAddress, type AiProvider } from './aiProviders';

export type AiValues = Record<string, string>;

export interface AiSettingsForm {
    loading: boolean;
    loadError: boolean;
    fields: PluginSettingField[];
    field: (key: string) => PluginSettingField | undefined;
    values: AiValues;
    saved: AiValues;
    dirty: boolean;
    providerId: string;
    isOpenAi: boolean;
    models: AiModels | null;
    loadingModels: boolean;
    busy: boolean;
    result: PluginConnectionTestResult | null;
    // What is missing before the form is worth saving, or null.
    problem: string | null;
    set: (key: string, value: string) => void;
    applyProvider: (provider: AiProvider) => void;
    loadModels: () => Promise<void>;
    test: () => Promise<PluginConnectionTestResult>;
    // Saves every AI setting. Resolves to why it could not, or null when saved.
    save: (extra?: AiValues) => Promise<string | null>;
    reset: () => void;
    clearResult: () => void;
}

// What the form needs before it is worth saving.
export function aiSettingsProblem(values: AiValues, providerId: string): string | null {
    if (providerId === 'openai') {
        return (values[AI_KEYS.apiKey] ?? '').trim() ? null : 'Paste the API key first.';
    }
    if (!(values[AI_KEYS.baseUrl] ?? '').trim()) return "Enter the server's address first.";
    return (values[AI_KEYS.chatModel] ?? '').trim() ? null : 'Choose a model for recommendations first. Load the models the server has, or type its name.';
}

// An empty address is OpenAI on a server that never saved one, but it is also
// what "Another server" leaves for the admin to fill in. The choice made in the
// form settles which, until an address is typed.
export function currentAiProvider(address: string, picked: string | null): string {
    return !address.trim() && picked ? picked : detectAiProvider(address);
}

// The embedding model the server would use for these values. OpenAI falls back
// to its default; any other server has none until one is named.
export function effectiveEmbeddingModel(values: AiValues, onOpenAi: boolean = isOpenAiAddress(values[AI_KEYS.baseUrl] ?? '')): string {
    const chosen = (values[AI_KEYS.embeddingModel] ?? '').trim();
    if (chosen) return chosen;
    return onOpenAi ? OPENAI_MODELS[AI_KEYS.embeddingModel] : '';
}

const canAskForModels = (values: AiValues): boolean =>
    !isOpenAiAddress(values[AI_KEYS.baseUrl] ?? '') || !!(values[AI_KEYS.apiKey] ?? '').trim();

const settingsFor = (values: AiValues, providerId: string): AiValues =>
    providerId === 'openai' && !(values[AI_KEYS.baseUrl] ?? '').trim() ? { ...values, [AI_KEYS.baseUrl]: OPENAI_BASE_URL } : values;

export function useAiSettings(serverId?: string): AiSettingsForm {
    const [fields, setFields] = useState<PluginSettingField[] | null>(null);
    const [values, setValues] = useState<AiValues>({});
    const [saved, setSaved] = useState<AiValues>({});
    const [loadError, setLoadError] = useState(false);
    const [models, setModels] = useState<AiModels | null>(null);
    const [loadingModels, setLoadingModels] = useState(false);
    const [busy, setBusy] = useState(false);
    const [result, setResult] = useState<PluginConnectionTestResult | null>(null);
    const [picked, setPicked] = useState<string | null>(null);
    const latestModelRequest = useRef(0);
    const providerId = currentAiProvider(values[AI_KEYS.baseUrl] ?? '', picked);
    const problem = aiSettingsProblem(values, providerId);

    const fetchModels = useCallback(async (from: AiValues, quiet: boolean) => {
        const request = ++latestModelRequest.current;
        if (!quiet) setLoadingModels(true);
        try {
            const loaded = await aiSettingsService.listModels(from, serverId);
            if (latestModelRequest.current !== request) return;
            setModels(quiet && loaded.error ? null : loaded);
        } catch {
            if (latestModelRequest.current === request && !quiet) {
                setModels({ chatModels: [], embeddingModels: [], error: 'Could not ask the server for its models.' });
            }
        } finally {
            if (latestModelRequest.current === request && !quiet) setLoadingModels(false);
        }
    }, [serverId]);

    useEffect(() => {
        let cancelled = false;
        systemSettingsAdminService.getPluginSettings(AI_PLUGIN_ID, serverId)
            .then(loaded => {
                if (cancelled) return;
                const initial: AiValues = {};
                loaded.filter(f => f.key !== 'is_enabled').forEach(f => { initial[f.key] = f.value ?? ''; });
                setFields(loaded);
                setValues(initial);
                setSaved(initial);
                if (canAskForModels(initial)) void fetchModels(initial, true);
            })
            .catch(() => { if (!cancelled) setLoadError(true); });
        return () => { cancelled = true; };
    }, [serverId, fetchModels]);

    const set = useCallback((key: string, value: string) => {
        setValues(current => ({ ...current, [key]: value }));
        setResult(null);
        if (key === AI_KEYS.baseUrl || key === AI_KEYS.apiKey) {
            latestModelRequest.current++;
            setModels(null);
        }
    }, []);

    const applyProvider = useCallback((provider: AiProvider) => {
        if (provider.id !== providerId) {
            setValues(current => {
                // The key belongs to the server it was made for. Left in place
                // it would be sent, as it is, to whichever server comes next.
                const next: AiValues = { ...current, [AI_KEYS.baseUrl]: provider.baseUrl, [AI_KEYS.apiKey]: '' };
                // Model names belong to one server too. Keeping OpenAI's on
                // Ollama, or the other way round, would save names it lacks.
                MODEL_KEYS.forEach(key => { next[key] = provider.id === 'openai' ? OPENAI_MODELS[key] : ''; });
                return next;
            });
        } else if (provider.baseUrl) {
            setValues(current => ({ ...current, [AI_KEYS.baseUrl]: provider.baseUrl }));
        }
        setPicked(provider.id);
        latestModelRequest.current++;
        setModels(null);
        setResult(null);
    }, [providerId]);

    const loadModels = useCallback(() => fetchModels(settingsFor(values, providerId), false), [fetchModels, values, providerId]);

    const test = useCallback(async (): Promise<PluginConnectionTestResult> => {
        setBusy(true);
        setResult(null);
        let outcome: PluginConnectionTestResult;
        try {
            outcome = await systemSettingsAdminService.testPluginConnection(AI_PLUGIN_ID, settingsFor(values, providerId), serverId);
        } catch {
            outcome = { success: false, message: 'The connection test could not be run.' };
        }
        setResult(outcome);
        setBusy(false);
        return outcome;
    }, [values, providerId, serverId]);

    const save = useCallback(async (extra?: AiValues): Promise<string | null> => {
        if (problem) {
            setResult({ success: false, message: problem });
            return problem;
        }
        setBusy(true);
        try {
            const body = settingsFor(values, providerId);
            await systemSettingsAdminService.updatePluginSettings(AI_PLUGIN_ID, { ...body, ...extra }, serverId);
            setValues(body);
            setSaved(body);
            return null;
        } catch {
            const message = "Couldn't save the AI settings. Try again in a moment.";
            setResult({ success: false, message });
            return message;
        } finally {
            setBusy(false);
        }
    }, [values, providerId, problem, serverId]);

    const reset = useCallback(() => {
        setValues(saved);
        setPicked(null);
        setResult(null);
    }, [saved]);

    const clearResult = useCallback(() => setResult(null), []);

    const dirty = useMemo(() => Object.keys(values).some(key => (values[key] ?? '') !== (saved[key] ?? '')), [values, saved]);
    const field = useCallback((key: string) => fields?.find(f => f.key === key), [fields]);

    return {
        loading: fields === null && !loadError,
        loadError,
        fields: fields ?? [],
        field,
        values,
        saved,
        dirty,
        providerId,
        isOpenAi: providerId === 'openai',
        models,
        loadingModels,
        busy,
        result,
        problem,
        set,
        applyProvider,
        loadModels,
        test,
        save,
        reset,
        clearResult,
    };
}
