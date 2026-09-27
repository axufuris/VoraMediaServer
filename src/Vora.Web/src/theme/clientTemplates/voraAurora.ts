import type { ThemeManifest } from '../types';

const auroraCanvasSvg =
    "data:image/svg+xml,%3Csvg%20xmlns%3D'http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg'%20viewBox%3D'0%200%201600%201000'%3E%3Cdefs%3E%3CradialGradient%20id%3D'a'%20cx%3D'0.2'%20cy%3D'0.1'%20r%3D'0.9'%3E%3Cstop%20offset%3D'0%25'%20stop-color%3D'%2314b8a6'%20stop-opacity%3D'0.4'%2F%3E%3Cstop%20offset%3D'70%25'%20stop-color%3D'%23020617'%20stop-opacity%3D'0'%2F%3E%3C%2FradialGradient%3E%3CradialGradient%20id%3D'b'%20cx%3D'0.85'%20cy%3D'0.3'%20r%3D'0.7'%3E%3Cstop%20offset%3D'0%25'%20stop-color%3D'%237c3aed'%20stop-opacity%3D'0.32'%2F%3E%3Cstop%20offset%3D'70%25'%20stop-color%3D'%23020617'%20stop-opacity%3D'0'%2F%3E%3C%2FradialGradient%3E%3C%2Fdefs%3E%3Crect%20width%3D'1600'%20height%3D'1000'%20fill%3D'%23020617'%2F%3E%3Crect%20width%3D'1600'%20height%3D'1000'%20fill%3D'url(%23a)'%2F%3E%3Crect%20width%3D'1600'%20height%3D'1000'%20fill%3D'url(%23b)'%2F%3E%3C%2Fsvg%3E";

export const voraAurora: ThemeManifest = {
    id: 'vora-aurora',
    name: 'Vora Aurora',
    version: '1.0.0',
    author: 'Vora',
    description: 'Deep navy with teal accent and an aurora gradient canvas. Cool, ambient, easy on late-night eyes.',

    tokens: {
        colors: {
            bgCanvas: '#020617',
            bgSurface: '#0c1726',
            bgRaised: '#172033',
            bgSunken: '#01040c',
            bgOverlay: 'rgba(2, 6, 23, 0.78)',

            borderSubtle: 'rgba(125, 211, 252, 0.08)',
            borderStrong: 'rgba(125, 211, 252, 0.16)',
            borderFocus: '#14b8a6',

            textPrimary: '#e0f2fe',
            textSecondary: '#bae6fd',
            textMuted: '#475569',
            textDisabled: '#1e293b',
            textInverse: '#020617',

            accent500: '#14b8a6',
            accentHover: '#2dd4bf',
            accentActive: '#0d9488',
            accentSoft: 'rgba(20, 184, 166, 0.18)',
            accentSoftHover: 'rgba(20, 184, 166, 0.28)',
            accentText: '#5eead4',
            accentContrast: '#022c22',

            success500: '#22c55e',
            successSoft: 'rgba(34, 197, 94, 0.16)',
            successText: '#4ade80',

            warning500: '#eab308',
            warningSoft: 'rgba(234, 179, 8, 0.16)',
            warningText: '#facc15',

            danger500: '#ef4444',
            dangerSoft: 'rgba(239, 68, 68, 0.16)',
            dangerText: '#fca5a5',

            info500: '#3b82f6',
            infoSoft: 'rgba(59, 130, 246, 0.16)',
            infoText: '#93c5fd',
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
            sm: '0 1px 2px rgba(0, 0, 0, 0.55)',
            md: '0 8px 24px rgba(0, 0, 0, 0.6)',
            lg: '0 20px 50px rgba(0, 0, 0, 0.75)',
            overlay: '0 30px 80px rgba(0, 0, 0, 0.88)',
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
            skeletonShimmer: '#172033',
            accentFocusRing: 'rgba(20, 184, 166, 0.28)',
        },
    },

    backgrounds: {
        canvas: {
            image: auroraCanvasSvg,
            opacity: 1,
            position: 'center top',
            size: 'cover',
            tint: 'rgba(2, 6, 23, 0.4)',
        },
        pageHeader: null,
        playerScrim: null,
        loginCanvas: {
            image: auroraCanvasSvg,
            opacity: 1,
            position: 'center',
            size: 'cover',
            tint: 'rgba(2, 6, 23, 0.3)',
        },
    },

    layout: {
        sidebar: 'rail',
        header: 'minimal',
        density: 'comfortable',
        card: 'elevated',
    },
};
