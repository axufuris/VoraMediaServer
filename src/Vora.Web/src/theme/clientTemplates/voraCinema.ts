import type { ThemeManifest } from '../types';

export const voraCinema: ThemeManifest = {
    id: 'vora-cinema',
    name: 'Vora Cinema',
    version: '1.0.0',
    author: 'Vora',
    description: 'The default. Deep canvas, amber accent, subtle vignette. Designed to disappear behind your library.',

    tokens: {
        colors: {
            bgCanvas: '#08080b',
            bgSurface: '#101015',
            bgRaised: '#181820',
            bgSunken: '#050507',
            bgOverlay: 'rgba(0, 0, 0, 0.72)',

            borderSubtle: 'rgba(255, 255, 255, 0.08)',
            borderStrong: 'rgba(255, 255, 255, 0.14)',
            borderFocus: '#f59e0b',

            textPrimary: '#fafafa',
            textSecondary: '#c4c4cc',
            textMuted: '#9090a0',
            textDisabled: '#5b5b6a',
            textInverse: '#0a0a0e',

            accent500: '#f59e0b',
            accentHover: '#fbbf24',
            accentActive: '#d97706',
            accentSoft: 'rgba(245, 158, 11, 0.16)',
            accentSoftHover: 'rgba(245, 158, 11, 0.24)',
            accentText: '#fbbf24',
            accentContrast: '#1a1207',

            success500: '#22c55e',
            successSoft: 'rgba(34, 197, 94, 0.16)',
            successText: '#4ade80',

            warning500: '#eab308',
            warningSoft: 'rgba(234, 179, 8, 0.16)',
            warningText: '#facc15',

            danger500: '#ef4444',
            dangerSoft: 'rgba(239, 68, 68, 0.16)',
            dangerText: '#fca5a5',

            info500: '#0ea5e9',
            infoSoft: 'rgba(14, 165, 233, 0.16)',
            infoText: '#7dd3fc',
        },

        typography: {
            fontSans: '"Inter Tight", "Inter", ui-sans-serif, system-ui, sans-serif',
            fontMono: '"JetBrains Mono", ui-monospace, monospace',
        },

        radii: {
            sm: '6px',
            md: '10px',
            lg: '14px',
            xl: '20px',
            pill: '9999px',
        },

        shadows: {
            sm: '0 1px 2px rgba(0, 0, 0, 0.5)',
            md: '0 8px 24px rgba(0, 0, 0, 0.55)',
            lg: '0 20px 50px rgba(0, 0, 0, 0.7)',
            overlay: '0 30px 80px rgba(0, 0, 0, 0.85)',
        },

        motion: {
            durationFast: '120ms',
            durationMed: '240ms',
            easeOut: 'cubic-bezier(0.16, 1, 0.3, 1)',
        },

        layout: {
            topbarHeight: '56px',
            sidebarWidth: '260px',
            sidebarRailWidth: '72px',
        },

        misc: {
            skeletonShimmer: '#181820',
            accentFocusRing: 'rgba(245, 158, 11, 0.28)',
        },
    },

    backgrounds: {
        canvas: null,
        pageHeader: null,
        playerScrim: null,
        loginCanvas: null,
    },

    layout: {
        sidebar: 'rail',
        header: 'minimal',
        density: 'comfortable',
        card: 'elevated',
    },
};
