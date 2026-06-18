import type { ThemeManifest } from '../types';

/**
 * Vora Default — light theme.
 *
 * Warm-neutral palette (Tailwind's stone family) with amber accent.
 * 1:1 with the values shipped in styles/tokens.css as the static fallback.
 */
export const voraDefault: ThemeManifest = {
    id: 'vora-default',
    name: 'Vora Default',
    version: '1.0.0',
    author: 'Vora',
    description: 'Warm neutrals with an amber accent. Editorial and quiet.',

    tokens: {
        colors: {
            bgCanvas: '#fafaf9',
            bgSurface: '#ffffff',
            bgRaised: '#ffffff',
            bgSunken: '#f5f5f4',
            bgOverlay: 'rgba(28, 25, 23, 0.6)',

            borderSubtle: '#e7e5e4',
            borderStrong: '#d6d3d1',
            borderFocus: '#d97706',

            textPrimary: '#1c1917',
            textSecondary: '#44403c',
            textMuted: '#78716c',
            textDisabled: '#a8a29e',
            textInverse: '#fafaf9',

            accent500: '#d97706',
            accentHover: '#b45309',
            accentActive: '#92400e',
            accentSoft: '#fef3c7',
            accentSoftHover: '#fde68a',
            accentText: '#92400e',
            accentContrast: '#1c1917',

            success500: '#16a34a',
            successSoft: '#dcfce7',
            successText: '#166534',

            warning500: '#eab308',
            warningSoft: '#fef9c3',
            warningText: '#854d0e',

            danger500: '#dc2626',
            dangerSoft: '#fee2e2',
            dangerText: '#991b1b',

            info500: '#0284c7',
            infoSoft: '#e0f2fe',
            infoText: '#075985',
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
            sm: '0 1px 2px rgba(28, 25, 23, 0.04), 0 1px 2px rgba(28, 25, 23, 0.06)',
            md: '0 4px 8px rgba(28, 25, 23, 0.05), 0 2px 4px rgba(28, 25, 23, 0.06)',
            lg: '0 12px 24px rgba(28, 25, 23, 0.08), 0 4px 8px rgba(28, 25, 23, 0.06)',
            overlay: '0 24px 48px rgba(28, 25, 23, 0.18)',
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
            skeletonShimmer: '#ebeae8',
            accentFocusRing: 'rgba(217, 119, 6, 0.15)',
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
