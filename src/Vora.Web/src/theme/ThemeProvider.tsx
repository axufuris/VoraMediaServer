import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import type { ThemeManifest } from './types';
import { applyTheme } from './applyTheme';
import { voraDefault } from './themes/voraDefault';
import { voraDark } from './themes/voraDark';
import { voraOcean } from './themes/voraOcean';
import { themeService } from '../api/System/themeService';
import { useSignalREvent } from '../hooks/useSignalREvent';

/**
 * Built-in themes bundled with the frontend. Plugin-shipped themes don't
 * appear here — they're fetched on demand from /api/admin/themes/{id}/manifest.
 */
const BUILT_IN_THEMES: ThemeManifest[] = [voraDefault, voraDark, voraOcean];

const STORAGE_KEY = 'vora_admin_theme_id';
const URL_PARAM = 'theme';

interface ThemeContextValue {
    /** Built-in themes available without a network round-trip. The picker
     *  may show additional plugin themes via themeService.getAll() — those
     *  aren't included here until they're activated and fetched. */
    builtInThemes: ThemeManifest[];
    /** Currently active manifest. Always set; defaults to vora-default. */
    active: ThemeManifest;
    /** True while the initial backend reconcile is in flight. */
    isLoading: boolean;
    /** True while an asynchronous theme switch is being applied (relevant for
     *  plugin themes where the manifest must be fetched first). */
    isSwitching: boolean;
    /**
     * Switch themes by id. Built-in themes apply immediately. Plugin themes
     * trigger a manifest fetch first, then apply. Persists to backend on
     * success; reverts on failure. Returns true on success.
     */
    setActive: (id: string) => Promise<boolean>;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function readUrlOverrideThemeId(): string | null {
    if (typeof window === 'undefined') return null;
    try {
        const url = new URL(window.location.href);
        const fromUrl = url.searchParams.get(URL_PARAM);
        if (fromUrl) return fromUrl;
    } catch {
        /* ignore malformed URL */
    }
    return null;
}

function resolveInitialThemeId(): string {
    if (typeof window === 'undefined') return voraDefault.id;

    // URL takes precedence; useful for preview or recovery. Note that a URL
    // override pointing at a plugin theme will fall back to the default for
    // the first paint (we can't synchronously fetch its manifest); the real
    // theme applies after the fetch completes.
    const fromUrl = readUrlOverrideThemeId();
    if (fromUrl) return fromUrl;

    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) return stored;

    return voraDefault.id;
}

function findBuiltIn(id: string): ThemeManifest | undefined {
    return BUILT_IN_THEMES.find(t => t.id === id);
}

function extractServerIdFromPath(pathname: string): string | undefined {
    const match = pathname.match(/^\/server\/([^/]+)/);
    return match ? match[1] : undefined;
}

const ADMIN_PATH_PREFIX_RE = /^(\/server\/[^/]+)?\/admin(\/|$)/;
function isAdminSurface(pathname: string): boolean {
    return ADMIN_PATH_PREFIX_RE.test(pathname);
}

/**
 * The backend returns a manifest with no assetsBaseUrl. The frontend has to
 * fill it in so relative image references inside the manifest resolve to the
 * theme's asset endpoint.
 */
function hydratePluginManifest(manifest: ThemeManifest): ThemeManifest {
    return {
        ...manifest,
        assetsBaseUrl: `/api/admin/themes/${encodeURIComponent(manifest.id)}/assets/`,
    };
}

interface ThemeProviderProps {
    children: ReactNode;
}

export function ThemeProvider({ children }: ThemeProviderProps) {
    const location = useLocation();
    const serverId = extractServerIdFromPath(location.pathname);
    const ownsSurface = isAdminSurface(location.pathname);

    const [activeId, setActiveId] = useState<string>(resolveInitialThemeId);
    const [activeManifest, setActiveManifest] = useState<ThemeManifest>(() => {
        return findBuiltIn(resolveInitialThemeId()) ?? voraDefault;
    });
    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [isSwitching, setIsSwitching] = useState<boolean>(false);

    // Plugin manifest cache. Keyed by theme id. Survives renders via ref so
    // we don't refetch when the component tree above us re-renders.
    const pluginManifestCache = useRef<Map<string, ThemeManifest>>(new Map());

    // Apply whenever the manifest changes or we move onto the admin surface.
    useEffect(() => {
        if (!ownsSurface) return;
        applyTheme(activeManifest);
    }, [activeManifest, ownsSurface]);

    /**
     * Resolve a theme id to a manifest. Built-in: instant. Plugin: fetch
     * (cache-aware). Returns null if the theme doesn't exist on the server.
     */
    const resolveManifest = useCallback(async (id: string): Promise<ThemeManifest | null> => {
        const builtIn = findBuiltIn(id);
        if (builtIn) return builtIn;

        const cached = pluginManifestCache.current.get(id);
        if (cached) return cached;

        try {
            const fetched = await themeService.getManifest(id, serverId);
            const hydrated = hydratePluginManifest(fetched);
            pluginManifestCache.current.set(id, hydrated);
            return hydrated;
        } catch (err) {
            console.warn(`Failed to fetch plugin theme manifest "${id}"`, err);
            return null;
        }
    }, [serverId]);

    // Reconcile with the backend on mount + when the active server changes.
    useEffect(() => {
        const urlOverride = readUrlOverrideThemeId();
        if (urlOverride) {
            // URL override: still resolve the manifest (might be a plugin theme)
            // but skip backend reconciliation.
            resolveManifest(urlOverride).then(manifest => {
                if (manifest) {
                    setActiveId(urlOverride);
                    setActiveManifest(manifest);
                }
            }).finally(() => setIsLoading(false));
            return;
        }

        let cancelled = false;
        if (!localStorage.getItem('profile_token') && !localStorage.getItem('account_token')) {
            return () => { cancelled = true; };
        }
        themeService.getActiveId(serverId)
            .then(async serverThemeId => {
                if (cancelled) return;
                const manifest = await resolveManifest(serverThemeId);
                if (cancelled) return;
                if (manifest) {
                    setActiveId(serverThemeId);
                    setActiveManifest(manifest);
                    try { localStorage.setItem(STORAGE_KEY, serverThemeId); } catch { /* ignore */ }
                }
                // If the manifest didn't resolve (plugin uninstalled, etc.),
                // we keep whatever we initially rendered.
            })
            .catch(() => { /* unauthenticated or offline — keep local choice */ })
            .finally(() => { if (!cancelled) setIsLoading(false); });
        return () => { cancelled = true; };
    }, [serverId, resolveManifest]);

    // Live theme propagation. When another admin changes the theme via the
    // backend, we re-resolve and apply without a page reload. URL override
    // wins (preview mode shouldn't get yanked away by someone else's change).
    useSignalREvent<string>('AdminThemeChanged', useCallback((newThemeId: string) => {
        if (readUrlOverrideThemeId()) return;
        if (!newThemeId || newThemeId === activeId) return;
        resolveManifest(newThemeId).then(manifest => {
            if (manifest) {
                setActiveId(newThemeId);
                setActiveManifest(manifest);
                try { localStorage.setItem(STORAGE_KEY, newThemeId); } catch { /* ignore */ }
            }
        });
    }, [activeId, resolveManifest]));

    const setActive = useCallback(async (id: string): Promise<boolean> => {
        setIsSwitching(true);
        try {
            const manifest = await resolveManifest(id);
            if (!manifest) {
                console.warn(`ThemeProvider.setActive: theme "${id}" not found`);
                return false;
            }

            // Optimistic local apply so the picker feels instant.
            const previousId = activeId;
            const previousManifest = activeManifest;
            setActiveId(id);
            setActiveManifest(manifest);
            try { localStorage.setItem(STORAGE_KEY, id); } catch { /* ignore */ }

            try {
                await themeService.setActiveId(id, serverId);
                return true;
            } catch (err) {
                console.error('Failed to persist theme to backend', err);
                setActiveId(previousId);
                setActiveManifest(previousManifest);
                try { localStorage.setItem(STORAGE_KEY, previousId); } catch { /* ignore */ }
                return false;
            }
        } finally {
            setIsSwitching(false);
        }
    }, [activeId, activeManifest, resolveManifest, serverId]);

    const value: ThemeContextValue = useMemo(() => ({
        builtInThemes: BUILT_IN_THEMES,
        active: activeManifest,
        isLoading,
        isSwitching,
        setActive,
    }), [activeManifest, isLoading, isSwitching, setActive]);

    return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
    const ctx = useContext(ThemeContext);
    if (!ctx) {
        throw new Error('useTheme must be used inside <ThemeProvider>');
    }
    return ctx;
}
