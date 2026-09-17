import type { ThemeManifest } from '../types';

/**
 * Vora Dark — second built-in theme.
 *
 * Cool-neutral palette (Tailwind's zinc family) with the same amber accent
 * as Vora Default, slightly brightened so it stays legible on dark surfaces.
 * Ships a faint amber radial-glow on the canvas (embedded as a data:image
 * SVG so no external request) to validate the backgrounds feature.
 */

const canvasGlowSvg =
    "data:image/svg+xml,%3Csvg%20xmlns%3D'http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg'%20viewBox%3D'0%200%201600%201000'%3E%3Cdefs%3E%3CradialGradient%20id%3D'g'%20cx%3D'0.5'%20cy%3D'0.35'%20r%3D'0.7'%3E%3Cstop%20offset%3D'0%25'%20stop-color%3D'%23f59e0b'%20stop-opacity%3D'0.35'%2F%3E%3Cstop%20offset%3D'100%25'%20stop-color%3D'%2309090b'%20stop-opacity%3D'0'%2F%3E%3C%2FradialGradient%3E%3C%2Fdefs%3E%3Crect%20width%3D'1600'%20height%3D'1000'%20fill%3D'url(%23g)'%2F%3E%3C%2Fsvg%3E";

export const voraDark: ThemeManifest = {
    id: 'vora-dark',
    name: 'Vora Dark',
    version: '1.0.0',
    author: 'Vora',
    description: 'Cool zinc neutrals with a brighter amber accent and a subtle canvas glow.',

    tokens: {
        colors: {
            bgCanvas: '#09090b',
            bgSurface: '#18181b',
            bgRaised: '#27272a',
            bgSunken: '#0a0a0a',
            bgOverlay: 'rgba(0, 0, 0, 0.7)',

            borderSubtle: '#27272a',
            borderStrong: '#3f3f46',
            borderFocus: '#f59e0b',

            textPrimary: '#fafafa',
            textSecondary: '#d4d4d8',
            textMuted: '#71717a',
            textDisabled: '#52525b',
            textInverse: '#09090b',

            accent500: '#f59e0b',
            accentHover: '#fbbf24',
            accentActive: '#fcd34d',
            accentSoft: 'rgba(245, 158, 11, 0.15)',
            accentSoftHover: 'rgba(245, 158, 11, 0.25)',
            accentText: '#fcd34d',
            accentContrast: '#09090b',

            success500: '#22c55e',
            successSoft: 'rgba(34, 197, 94, 0.15)',
            successText: '#86efac',

            warning500: '#eab308',
            warningSoft: 'rgba(234, 179, 8, 0.15)',
            warningText: '#fde047',

            danger500: '#ef4444',
            dangerSoft: 'rgba(239, 68, 68, 0.15)',
            dangerText: '#fca5a5',

            info500: '#0ea5e9',
            infoSoft: 'rgba(14, 165, 233, 0.15)',
            infoText: '#7dd3fc',
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
            skeletonShimmer: '#27272a',
            accentFocusRing: 'rgba(245, 158, 11, 0.25)',
        },
    },

    backgrounds: {
        canvas: {
            image: canvasGlowSvg,
            opacity: 1,
            position: 'center top',
            size: 'cover',
            tint: 'rgba(9, 9, 11, 0.6)',
        },
        pageHeader: null,
    },

    layout: {
        sidebar: 'full',
        header: 'rich',
        density: 'comfortable',
        card: 'elevated',
    },
};
