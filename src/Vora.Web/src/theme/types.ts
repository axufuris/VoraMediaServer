export interface TokenColors {
    bgCanvas: string;
    bgSurface: string;
    bgRaised: string;
    bgSunken: string;
    bgOverlay: string;

    borderSubtle: string;
    borderStrong: string;
    borderFocus: string;

    textPrimary: string;
    textSecondary: string;
    textMuted: string;
    textDisabled: string;
    textInverse: string;

    accent500: string;
    accentHover: string;
    accentActive: string;
    accentSoft: string;
    accentSoftHover: string;
    accentText: string;
    accentContrast: string;

    success500: string;
    successSoft: string;
    successText: string;

    warning500: string;
    warningSoft: string;
    warningText: string;

    danger500: string;
    dangerSoft: string;
    dangerText: string;

    info500: string;
    infoSoft: string;
    infoText: string;
}

export interface TokenTypography {
    fontSans: string;
    fontMono: string;
}

export interface TokenRadii {
    sm: string;
    md: string;
    lg: string;
    xl: string;
    pill: string;
}

export interface TokenShadows {
    sm: string;
    md: string;
    lg: string;
    overlay: string;
}

export interface TokenMotion {
    durationFast: string;
    durationMed: string;
    easeOut: string;
}

export interface TokenLayout {
    topbarHeight: string;
    sidebarWidth: string;
    sidebarRailWidth: string;
}

/**
 * Skeleton shimmer hi-tone color. Sits between bgSunken and the next-step-up so
 * the wave reads cleanly on both light and dark themes.
 */
export interface TokenMisc {
    skeletonShimmer: string;
    /** Used to render a soft accent halo behind focused inputs (`box-shadow`). */
    accentFocusRing: string;
}

export interface TokenSet {
    colors: TokenColors;
    typography: TokenTypography;
    radii: TokenRadii;
    shadows: TokenShadows;
    motion: TokenMotion;
    layout: TokenLayout;
    misc: TokenMisc;
}

/**
 * A single background slot.
 *   image     — Asset URL. Either an https:// URL, a data:image/... URL, or a
 *               relative path inside the theme bundle (resolved by the loader
 *               against `<themeBaseUrl>/assets/...`).
 *   opacity   — 0–1. Applied to the image layer via the tint overlay.
 *   position  — Background-position keyword/syntax.
 *   size      — Background-size keyword. Defaults to 'cover'.
 *   tint      — Overlay color applied on top of the image as a constant linear
 *               gradient so text on top stays legible regardless of image
 *               contents. Use rgba() with the desired alpha.
 *   blendMode — CSS background-blend-mode. Optional.
 */
export interface BackgroundSlot {
    image: string;
    opacity?: number;
    position?: string;
    size?: 'cover' | 'contain' | 'auto' | string;
    tint?: string;
    blendMode?: string;
}

export interface ThemeBackgrounds {
    /** Whole-app canvas behind every admin page. */
    canvas?: BackgroundSlot | null;
    /** PageHeader strip — per-page banner. */
    pageHeader?: BackgroundSlot | null;
    /** Player chrome scrim. Optional, only used by client templates. */
    playerScrim?: BackgroundSlot | null;
    /** Login / register / profile-pick canvas. Optional, only used by client templates. */
    loginCanvas?: BackgroundSlot | null;
}

/**
 * Non-color layout choices a theme can flip. Applied as `data-vora-*`
 * attributes on `<html>` so CSS can key off them.
 */
export interface ThemeLayoutFlags {
    /** 'full' (default 240px) | 'rail' (icon-only 64px) | 'floating' */
    sidebar?: 'full' | 'rail' | 'floating';
    /** 'rich' (default) | 'minimal' (no breadcrumb, no activity pill) */
    header?: 'rich' | 'minimal';
    /** 'comfortable' (default) | 'compact' */
    density?: 'comfortable' | 'compact';
    /** 'elevated' (shadow) | 'flat' | 'outlined' */
    card?: 'elevated' | 'flat' | 'outlined';
}

export interface ThemeManifest {
    /** Stable identifier; used as the key when persisting the active theme. */
    id: string;
    /** Human-readable name shown in the picker. */
    name: string;
    /** SemVer string. */
    version: string;
    /** Optional author / publisher attribution. */
    author?: string;
    /** Optional one-line description for the picker. */
    description?: string;
    /** Optional preview image URL for the picker. Same sanitization rules as backgrounds. */
    preview?: string;
    /**
     * Optional parent theme id. Token/background values omitted in this manifest
     * fall back to the parent's. Phase 2 only supports built-in parents.
     */
    extends?: string;

    tokens: TokenSet;
    backgrounds?: ThemeBackgrounds;
    layout?: ThemeLayoutFlags;

    /**
     * Optional base URL used to resolve relative `image` paths inside backgrounds.
     * Phase 3 themes loaded from plugins set this to the plugin's asset endpoint.
     * Phase 2 built-in themes don't set this (they use absolute https:// or data: URLs).
     */
    assetsBaseUrl?: string;
}
