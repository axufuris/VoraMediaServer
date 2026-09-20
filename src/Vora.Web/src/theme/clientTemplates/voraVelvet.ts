import type { ThemeManifest } from '../types';

const velvetCanvasSvg =
    "data:image/svg+xml,%3Csvg%20xmlns%3D'http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg'%20viewBox%3D'0%200%201600%201000'%3E%3Cdefs%3E%3CradialGradient%20id%3D'g'%20cx%3D'0.5'%20cy%3D'0.2'%20r%3D'0.85'%3E%3Cstop%20offset%3D'0%25'%20stop-color%3D'%23b91c1c'%20stop-opacity%3D'0.35'%2F%3E%3Cstop%20offset%3D'70%25'%20stop-color%3D'%231a0a0e'%20stop-opacity%3D'0'%2F%3E%3C%2FradialGradient%3E%3C%2Fdefs%3E%3Crect%20width%3D'1600'%20height%3D'1000'%20fill%3D'%231a0a0e'%2F%3E%3Crect%20width%3D'1600'%20height%3D'1000'%20fill%3D'url(%23g)'%2F%3E%3C%2Fsvg%3E";

export const voraVelvet: ThemeManifest = {
    id: 'vora-velvet',
    name: 'Vora Velvet',
    version: '1.0.0',
    author: 'Vora',
    description: 'Burgundy canvas with sepia-tinted artwork and gold accents. Holiday-warm without losing cinematic gravitas.',

    tokens: {
        colors: {
            bgCanvas: '#1a0a0e',
            bgSurface: '#2a1218',
            bgRaised: '#3a1c24',
            bgSunken: '#0e0608',
            bgOverlay: 'rgba(26, 10, 14, 0.78)',

            borderSubtle: 'rgba(254, 240, 138, 0.08)',
            borderStrong: 'rgba(254, 240, 138, 0.16)',
            borderFocus: '#fef08a',

            textPrimary: '#fef9c3',
            textSecondary: '#fde68a',
            textMuted: '#a8a29e',
            textDisabled: '#57534e',
            textInverse: '#1a0a0e',

            accent500: '#c2410c',
            accentHover: '#ea580c',
            accentActive: '#9a3412',
            accentSoft: 'rgba(194, 65, 12, 0.18)',
            accentSoftHover: 'rgba(194, 65, 12, 0.28)',
            accentText: '#fed7aa',
            accentContrast: '#1a0a0e',

            success500: '#65a30d',
            successSoft: 'rgba(101, 163, 13, 0.16)',
            successText: '#a3e635',

            warning500: '#eab308',
            warningSoft: 'rgba(234, 179, 8, 0.16)',
            warningText: '#fde047',

            danger500: '#dc2626',
            dangerSoft: 'rgba(220, 38, 38, 0.16)',
            dangerText: '#fca5a5',

            info500: '#0891b2',
            infoSoft: 'rgba(8, 145, 178, 0.16)',
            infoText: '#67e8f9',
        },

        typography: {
            fontSans: '"Inter Tight", "Inter", ui-sans-serif, system-ui, sans-serif',
            fontMono: '"JetBrains Mono", ui-monospace, monospace',
        },

        radii: {
            sm: '6px',
            md: '10px',
            lg: '14px',
            xl: '22px',
            pill: '9999px',
        },

        shadows: {
            sm: '0 1px 2px rgba(0, 0, 0, 0.55)',
            md: '0 8px 24px rgba(0, 0, 0, 0.6)',
            lg: '0 20px 50px rgba(0, 0, 0, 0.75)',
            overlay: '0 30px 80px rgba(0, 0, 0, 0.88)',
        },

        motion: {
            durationFast: '140ms',
            durationMed: '280ms',
            easeOut: 'cubic-bezier(0.16, 1, 0.3, 1)',
        },

        layout: {
            topbarHeight: '56px',
            sidebarWidth: '260px',
            sidebarRailWidth: '72px',
        },

        misc: {
            skeletonShimmer: '#3a1c24',
            accentFocusRing: 'rgba(254, 240, 138, 0.28)',
        },
    },

    backgrounds: {
        canvas: {
            image: velvetCanvasSvg,
            opacity: 1,
            position: 'center top',
            size: 'cover',
            tint: 'rgba(26, 10, 14, 0.55)',
        },
        pageHeader: null,
        playerScrim: null,
        loginCanvas: {
            image: velvetCanvasSvg,
            opacity: 1,
            position: 'center',
            size: 'cover',
            tint: 'rgba(26, 10, 14, 0.4)',
        },
    },

    layout: {
        sidebar: 'rail',
        header: 'minimal',
        density: 'comfortable',
        card: 'elevated',
    },
};
