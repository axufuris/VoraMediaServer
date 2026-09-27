import type { ThemeManifest } from '../types';

/**
 * Vora Ocean — third built-in theme.
 *
 * Deep navy/slate surfaces with a teal accent. Distinct hue family from
 * stone+amber (Vora Default) and zinc+amber (Vora Dark), which is the point
 * — it stress-tests the token system across a different palette and proves
 * the abstraction isn't accidentally amber-only.
 *
 * Tonally: tech-forward, slightly corporate, "data center under glass."
 */
export const voraOcean: ThemeManifest = {
    id: 'vora-ocean',
    name: 'Vora Ocean',
    version: '1.0.0',
    author: 'Vora',
    description: 'Deep navy surfaces with a teal accent. Cooler, more technical.',

    tokens: {
        colors: {
            bgCanvas: '#0a1623',
            bgSurface: '#0f1f30',
            bgRaised: '#16293e',
            bgSunken: '#07111c',
            bgOverlay: 'rgba(7, 17, 28, 0.75)',

            borderSubtle: '#16293e',
            borderStrong: '#22384f',
            borderFocus: '#2dd4bf',

            textPrimary: '#e2e8f0',
            textSecondary: '#cbd5e1',
            textMuted: '#64748b',
            textDisabled: '#475569',
            textInverse: '#0a1623',

            accent500: '#14b8a6',
            accentHover: '#2dd4bf',
            accentActive: '#5eead4',
            accentSoft: 'rgba(20, 184, 166, 0.15)',
            accentSoftHover: 'rgba(20, 184, 166, 0.25)',
            accentText: '#5eead4',
            accentContrast: '#0a1623',

            success500: '#10b981',
            successSoft: 'rgba(16, 185, 129, 0.15)',
            successText: '#6ee7b7',

            warning500: '#f59e0b',
            warningSoft: 'rgba(245, 158, 11, 0.15)',
            warningText: '#fcd34d',

            danger500: '#ef4444',
            dangerSoft: 'rgba(239, 68, 68, 0.15)',
            dangerText: '#fca5a5',

            info500: '#3b82f6',
            infoSoft: 'rgba(59, 130, 246, 0.15)',
            infoText: '#93c5fd',
        },

        typography: {
            fontSans: '"Inter", ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
            fontMono: '"JetBrains Mono", ui-monospace, "SF Mono", Menlo, monospace',
        },

        radii: {
            sm: '4px',
            md: '6px',
            lg: '10px',
            xl: '14px',
            pill: '9999px',
        },

        shadows: {
            sm: '0 1px 2px rgba(0, 0, 0, 0.4), 0 1px 2px rgba(0, 0, 0, 0.3)',
            md: '0 4px 8px rgba(0, 0, 0, 0.5), 0 2px 4px rgba(0, 0, 0, 0.4)',
            lg: '0 12px 24px rgba(0, 0, 0, 0.6), 0 4px 8px rgba(0, 0, 0, 0.5)',
            overlay: '0 24px 48px rgba(0, 0, 0, 0.8)',
        },

        motion: {
            durationFast: '150ms',
            durationMed: '200ms',
            easeOut: 'cubic-bezier(0.2, 0.0, 0.0, 1)',
        },

        layout: {
            topbarHeight: '56px',
            sidebarWidth: '240px',
            sidebarRailWidth: '64px',
        },

        misc: {
            skeletonShimmer: '#16293e',
            accentFocusRing: 'rgba(20, 184, 166, 0.25)',
        },
    },

    backgrounds: {
        canvas: null,
        pageHeader: null,
    },

    layout: {
        sidebar: 'full',
        header: 'rich',
        density: 'comfortable',
        card: 'elevated',
    },
};
