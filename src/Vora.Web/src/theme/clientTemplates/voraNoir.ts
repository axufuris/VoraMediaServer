import type { ThemeManifest } from '../types';

export const voraNoir: ThemeManifest = {
    id: 'vora-noir',
    name: 'Vora Noir',
    version: '1.0.0',
    author: 'Vora',
    description: 'Pure black canvas, cool steel accent, high contrast. For OLED screens and high-glare rooms.',

    tokens: {
        colors: {
            bgCanvas: '#000000',
            bgSurface: '#0a0a0a',
            bgRaised: '#141414',
            bgSunken: '#000000',
            bgOverlay: 'rgba(0, 0, 0, 0.8)',

            borderSubtle: 'rgba(255, 255, 255, 0.06)',
            borderStrong: 'rgba(255, 255, 255, 0.12)',
            borderFocus: '#94a3b8',

            textPrimary: '#f8fafc',
            textSecondary: '#cbd5e1',
            textMuted: '#64748b',
            textDisabled: '#334155',
            textInverse: '#020617',

            accent500: '#94a3b8',
            accentHover: '#cbd5e1',
            accentActive: '#64748b',
            accentSoft: 'rgba(148, 163, 184, 0.16)',
            accentSoftHover: 'rgba(148, 163, 184, 0.24)',
            accentText: '#cbd5e1',
            accentContrast: '#020617',

            success500: '#22c55e',
            successSoft: 'rgba(34, 197, 94, 0.14)',
            successText: '#4ade80',

            warning500: '#eab308',
            warningSoft: 'rgba(234, 179, 8, 0.14)',
            warningText: '#facc15',

            danger500: '#ef4444',
            dangerSoft: 'rgba(239, 68, 68, 0.14)',
            dangerText: '#fca5a5',

            info500: '#0ea5e9',
            infoSoft: 'rgba(14, 165, 233, 0.14)',
            infoText: '#7dd3fc',
        },

        typography: {
            fontSans: '"Inter Tight", "Inter", ui-sans-serif, system-ui, sans-serif',
            fontMono: '"JetBrains Mono", ui-monospace, monospace',
        },

        radii: {
            sm: '4px',
            md: '8px',
            lg: '12px',
            xl: '18px',
            pill: '9999px',
        },

        shadows: {
            sm: '0 1px 2px rgba(0, 0, 0, 0.6)',
            md: '0 8px 24px rgba(0, 0, 0, 0.65)',
            lg: '0 20px 50px rgba(0, 0, 0, 0.8)',
            overlay: '0 30px 80px rgba(0, 0, 0, 0.92)',
        },

        motion: {
            durationFast: '120ms',
            durationMed: '220ms',
            easeOut: 'cubic-bezier(0.16, 1, 0.3, 1)',
        },

        layout: {
            topbarHeight: '56px',
            sidebarWidth: '260px',
            sidebarRailWidth: '72px',
        },

        misc: {
            skeletonShimmer: '#141414',
            accentFocusRing: 'rgba(148, 163, 184, 0.28)',
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
        density: 'compact',
        card: 'flat',
    },
};
