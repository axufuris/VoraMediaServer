import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import type { ThemeManifest } from './types';
import { applyTheme } from './applyTheme';
import { voraCinema } from './clientTemplates/voraCinema';
import { voraNoir } from './clientTemplates/voraNoir';
import { voraVelvet } from './clientTemplates/voraVelvet';
import { voraAurora } from './clientTemplates/voraAurora';
import { clientTemplateService, type ActiveTemplateVM, type TemplateScheduleVM } from '../api/System/clientTemplateService';
import { useSignalREvent } from '../hooks/useSignalREvent';

const BUILT_IN_CLIENT_TEMPLATES: ThemeManifest[] = [voraCinema, voraNoir, voraVelvet, voraAurora];

const STORAGE_KEY = 'vora_client_template_id';
const URL_PARAM = 'template';
const ADMIN_PATH_PREFIX_RE = /^(\/server\/[^/]+)?\/admin(\/|$)/;

interface ClientTemplateContextValue {
    builtInTemplates: ThemeManifest[];
    active: ThemeManifest;
    activeInfo: ActiveTemplateVM | null;
    activeSchedule: TemplateScheduleVM | null;
    isLoading: boolean;
    isSwitching: boolean;
    setActive: (id: string) => Promise<boolean>;
    clearActive: () => Promise<boolean>;
    refresh: () => Promise<void>;
}

const ClientTemplateContext = createContext<ClientTemplateContextValue | null>(null);

function readUrlOverrideTemplateId(): string | null {
    if (typeof window === 'undefined') return null;
    try {
        const url = new URL(window.location.href);
        const fromUrl = url.searchParams.get(URL_PARAM);
        if (fromUrl) return fromUrl;
    } catch {
        return null;
    }
    return null;
}

function resolveInitialTemplateId(): string {
    if (typeof window === 'undefined') return voraCinema.id;
    const fromUrl = readUrlOverrideTemplateId();
    if (fromUrl) return fromUrl;
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) return stored;
    return voraCinema.id;
}

function findBuiltIn(id: string): ThemeManifest | undefined {
    return BUILT_IN_CLIENT_TEMPLATES.find(t => t.id === id);
}

function extractServerIdFromPath(pathname: string): string | undefined {
    const match = pathname.match(/^\/server\/([^/]+)/);
    return match ? match[1] : undefined;
}

function isAdminSurface(pathname: string): boolean {
    return ADMIN_PATH_PREFIX_RE.test(pathname);
}

function hydratePluginManifest(manifest: ThemeManifest): ThemeManifest {
    return {
        ...manifest,
        assetsBaseUrl: `/api/templates/${encodeURIComponent(manifest.id)}/assets/`,
    };
}

interface ClientTemplateProviderProps {
    children: ReactNode;
}

export function ClientTemplateProvider({ children }: ClientTemplateProviderProps) {
    const location = useLocation();
    const serverId = extractServerIdFromPath(location.pathname);
    const onAdminSurface = isAdminSurface(location.pathname);

    const [activeId, setActiveId] = useState<string>(resolveInitialTemplateId);
    const [activeManifest, setActiveManifest] = useState<ThemeManifest>(() => {
        return findBuiltIn(resolveInitialTemplateId()) ?? voraCinema;
    });
    const [activeInfo, setActiveInfo] = useState<ActiveTemplateVM | null>(null);
    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [isSwitching, setIsSwitching] = useState<boolean>(false);

    const pluginManifestCache = useRef<Map<string, ThemeManifest>>(new Map());

    useEffect(() => {
        if (onAdminSurface) return;
        applyTheme(activeManifest);
    }, [activeManifest, onAdminSurface]);

    const resolveManifest = useCallback(async (id: string): Promise<ThemeManifest | null> => {
        const builtIn = findBuiltIn(id);
        if (builtIn) return builtIn;
        const cached = pluginManifestCache.current.get(id);
        if (cached) return cached;
        try {
            const fetched = await clientTemplateService.getManifest(id, serverId);
            const hydrated = hydratePluginManifest(fetched);
            pluginManifestCache.current.set(id, hydrated);
            return hydrated;
        } catch (err) {
            console.warn(`Failed to fetch plugin template manifest "${id}"`, err);
            return null;
        }
    }, [serverId]);

    const reconcileWithBackend = useCallback(async () => {
        if (!localStorage.getItem('profile_token') && !localStorage.getItem('account_token')) {
            return;
        }
        try {
            const result = await clientTemplateService.getActive(serverId);
            const manifest = await resolveManifest(result.templateId);
            if (manifest) {
                setActiveId(result.templateId);
                setActiveManifest(manifest);
                setActiveInfo(result);
                try { localStorage.setItem(STORAGE_KEY, result.templateId); } catch { /* ignore */ }
            }
        } catch {
            // unauthenticated or offline — keep local choice
        }
    }, [serverId, resolveManifest]);

    useEffect(() => {
        const urlOverride = readUrlOverrideTemplateId();
        if (urlOverride) {
            resolveManifest(urlOverride).then(manifest => {
                if (manifest) {
                    setActiveId(urlOverride);
                    setActiveManifest(manifest);
                }
            }).finally(() => setIsLoading(false));
            return;
        }

        let cancelled = false;
        reconcileWithBackend().finally(() => {
            if (!cancelled) setIsLoading(false);
        });
        return () => { cancelled = true; };
    }, [reconcileWithBackend, resolveManifest]);

    useSignalREvent<void>('ClientTemplateConfigurationChanged', useCallback(() => {
        if (readUrlOverrideTemplateId()) return;
        reconcileWithBackend();
    }, [reconcileWithBackend]));

    useEffect(() => {
        if (typeof document === 'undefined') return;
        const onFocus = () => {
            if (readUrlOverrideTemplateId()) return;
            reconcileWithBackend();
        };
        window.addEventListener('focus', onFocus);
        return () => window.removeEventListener('focus', onFocus);
    }, [reconcileWithBackend]);

    const setActive = useCallback(async (id: string): Promise<boolean> => {
        setIsSwitching(true);
        try {
            const manifest = await resolveManifest(id);
            if (!manifest) {
                console.warn(`ClientTemplateProvider.setActive: template "${id}" not found`);
                return false;
            }

            const previousId = activeId;
            const previousManifest = activeManifest;
            const previousInfo = activeInfo;
            setActiveId(id);
            setActiveManifest(manifest);
            try { localStorage.setItem(STORAGE_KEY, id); } catch { /* ignore */ }

            try {
                const result = await clientTemplateService.setActive(id, serverId);
                setActiveInfo(prev => prev ? { ...prev, templateId: result.templateId, source: result.source } : { templateId: result.templateId, source: result.source });
                return true;
            } catch (err) {
                console.error('Failed to persist client template to backend', err);
                setActiveId(previousId);
                setActiveManifest(previousManifest);
                setActiveInfo(previousInfo);
                try { localStorage.setItem(STORAGE_KEY, previousId); } catch { /* ignore */ }
                return false;
            }
        } finally {
            setIsSwitching(false);
        }
    }, [activeId, activeManifest, activeInfo, resolveManifest, serverId]);

    const clearActive = useCallback(async (): Promise<boolean> => {
        setIsSwitching(true);
        try {
            await clientTemplateService.clearActive(serverId);
            await reconcileWithBackend();
            return true;
        } catch (err) {
            console.error('Failed to clear client template', err);
            return false;
        } finally {
            setIsSwitching(false);
        }
    }, [reconcileWithBackend, serverId]);

    const refresh = useCallback(async () => {
        await reconcileWithBackend();
    }, [reconcileWithBackend]);

    const value: ClientTemplateContextValue = useMemo(() => ({
        builtInTemplates: BUILT_IN_CLIENT_TEMPLATES,
        active: activeManifest,
        activeInfo,
        activeSchedule: activeInfo?.schedule ?? null,
        isLoading,
        isSwitching,
        setActive,
        clearActive,
        refresh,
    }), [activeManifest, activeInfo, isLoading, isSwitching, setActive, clearActive, refresh]);

    return <ClientTemplateContext.Provider value={value}>{children}</ClientTemplateContext.Provider>;
}

export function useClientTemplate(): ClientTemplateContextValue {
    const ctx = useContext(ClientTemplateContext);
    if (!ctx) {
        throw new Error('useClientTemplate must be used inside <ClientTemplateProvider>');
    }
    return ctx;
}
