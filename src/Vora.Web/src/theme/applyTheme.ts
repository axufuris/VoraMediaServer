import type { BackgroundSlot, ThemeManifest } from './types';

/**
 * Translates a ThemeManifest into:
 *   - CSS custom properties set on document.documentElement
 *   - data-vora-* attributes set on document.documentElement (layout flags)
 *
 * Pure as possible: no React, no DOM queries beyond the document root, no
 * network. Safe to call from anywhere.
 */

const VAR_PREFIX = '--vora-';

/**
 * Allow http(s), data:image/*, and relative paths inside the theme bundle.
 * Reject anything that could close out of the url() call or smuggle JS in a
 * `url()` context (`javascript:`, `expression(`, unbalanced quotes/parens).
 *
 * Phase 2 themes are author-controlled in-repo, but this is defense-in-depth
 * for Phase 3 when themes ship via plugin install.
 */
export function sanitizeImageUrl(raw: string, assetsBaseUrl?: string): string | null {
    if (typeof raw !== 'string') return null;
    const trimmed = raw.trim();
    if (trimmed.length === 0) return null;

    // Reject anything that could escape the CSS url("…") context we emit.
    // We always wrap the URL in DOUBLE quotes, so single quotes inside the URL
    // are safe (SVG data URLs commonly embed them). The actual dangers are:
    //   - unescaped double quote → closes the string
    //   - parenthesis           → closes the url() call
    //   - backslash             → CSS string escape sequences
    //   - whitespace control    → CSS syntax breakage
    if (/["()\\\n\r\t]/.test(trimmed)) {
        return null;
    }

    // Allowed schemes.
    if (/^https:\/\//i.test(trimmed)) return trimmed;
    if (/^data:image\/(png|jpe?g|gif|webp|svg\+xml|avif)[,;]/i.test(trimmed)) return trimmed;

    // Relative path — resolve against assetsBaseUrl when provided.
    if (/^[a-zA-Z0-9._/-]+$/.test(trimmed) && !trimmed.startsWith('/')) {
        if (assetsBaseUrl) {
            const base = assetsBaseUrl.endsWith('/') ? assetsBaseUrl : assetsBaseUrl + '/';
            return base + trimmed;
        }
        return null;
    }

    // Absolute paths on this origin (e.g. /api/admin/themes/...) — allow.
    if (trimmed.startsWith('/api/admin/themes/') || trimmed.startsWith('/theme/')) {
        return trimmed;
    }

    return null;
}

function setVar(root: HTMLElement, name: string, value: string | null) {
    if (value === null || value === '') {
        root.style.removeProperty(VAR_PREFIX + name);
    } else {
        root.style.setProperty(VAR_PREFIX + name, value);
    }
}

function applyBackgroundSlot(
    root: HTMLElement,
    slotName: string,
    slot: BackgroundSlot | null | undefined,
    assetsBaseUrl: string | undefined,
) {
    const prefix = `bg-${slotName}`;
    if (!slot) {
        setVar(root, `${prefix}-image`, 'none');
        setVar(root, `${prefix}-image-tint`, 'transparent');
        setVar(root, `${prefix}-image-position`, 'center');
        setVar(root, `${prefix}-image-size`, 'cover');
        setVar(root, `${prefix}-image-blend`, 'normal');
        return;
    }

    const safeUrl = sanitizeImageUrl(slot.image, assetsBaseUrl);
    if (!safeUrl) {
        console.warn(`Theme background slot "${slotName}" image was rejected by URL sanitization.`);
        setVar(root, `${prefix}-image`, 'none');
        setVar(root, `${prefix}-image-tint`, 'transparent');
        return;
    }

    setVar(root, `${prefix}-image`, `url("${safeUrl}")`);

    // The tint sits as a constant linear-gradient *over* the image so text on
    // top of the surface stays legible. If no tint is provided, default to a
    // transparent overlay (image shows through unchanged).
    const tint = slot.tint ?? 'transparent';
    setVar(root, `${prefix}-image-tint`, tint);
    setVar(root, `${prefix}-image-position`, slot.position ?? 'center');
    setVar(root, `${prefix}-image-size`, slot.size ?? 'cover');
    setVar(root, `${prefix}-image-blend`, slot.blendMode ?? 'normal');

    // opacity field is informational on the slot itself; we render opacity by
    // controlling tint alpha, which is the correct way to dim an image without
    // also dimming any text that sits above it.
}

/**
 * Subset of CSS variables we cache to localStorage so the next first paint
 * uses the active theme rather than the light defaults baked into tokens.css.
 * The full theme still applies after React mounts; this just stops the FOUC
 * from being visible on subsequent refreshes.
 */
export const THEME_PREPAINT_CACHE_KEY = 'vora_admin_theme_prepaint';

function persistPrePaintCache(manifest: ThemeManifest): void {
    try {
        const c = manifest.tokens.colors;
        const cache = {
            'bg-canvas': c.bgCanvas,
            'bg-surface': c.bgSurface,
            'bg-raised': c.bgRaised,
            'bg-sunken': c.bgSunken,
            'bg-overlay': c.bgOverlay,
            'border-subtle': c.borderSubtle,
            'border-strong': c.borderStrong,
            'text-primary': c.textPrimary,
            'text-secondary': c.textSecondary,
            'text-muted': c.textMuted,
            'text-disabled': c.textDisabled,
            'accent-500': c.accent500,
            'accent-text': c.accentText,
            'accent-soft': c.accentSoft
        };
        localStorage.setItem(THEME_PREPAINT_CACHE_KEY, JSON.stringify(cache));
    } catch {
        /* ignore quota / disabled storage */
    }
}

export function applyTheme(manifest: ThemeManifest): void {
    const root = document.documentElement;

    // ===== Colors =====
    const c = manifest.tokens.colors;
    setVar(root, 'bg-canvas', c.bgCanvas);
    setVar(root, 'bg-surface', c.bgSurface);
    setVar(root, 'bg-raised', c.bgRaised);
    setVar(root, 'bg-sunken', c.bgSunken);
    setVar(root, 'bg-overlay', c.bgOverlay);

    setVar(root, 'border-subtle', c.borderSubtle);
    setVar(root, 'border-strong', c.borderStrong);
    setVar(root, 'border-focus', c.borderFocus);

    setVar(root, 'text-primary', c.textPrimary);
    setVar(root, 'text-secondary', c.textSecondary);
    setVar(root, 'text-muted', c.textMuted);
    setVar(root, 'text-disabled', c.textDisabled);
    setVar(root, 'text-inverse', c.textInverse);

    persistPrePaintCache(manifest);

    setVar(root, 'accent-500', c.accent500);
    setVar(root, 'accent-hover', c.accentHover);
    setVar(root, 'accent-active', c.accentActive);
    setVar(root, 'accent-soft', c.accentSoft);
    setVar(root, 'accent-soft-hover', c.accentSoftHover);
    setVar(root, 'accent-text', c.accentText);
    setVar(root, 'accent-contrast', c.accentContrast);

    setVar(root, 'success-500', c.success500);
    setVar(root, 'success-soft', c.successSoft);
    setVar(root, 'success-text', c.successText);

    setVar(root, 'warning-500', c.warning500);
    setVar(root, 'warning-soft', c.warningSoft);
    setVar(root, 'warning-text', c.warningText);

    setVar(root, 'danger-500', c.danger500);
    setVar(root, 'danger-soft', c.dangerSoft);
    setVar(root, 'danger-text', c.dangerText);

    setVar(root, 'info-500', c.info500);
    setVar(root, 'info-soft', c.infoSoft);
    setVar(root, 'info-text', c.infoText);

    // ===== Typography =====
    setVar(root, 'font-sans', manifest.tokens.typography.fontSans);
    setVar(root, 'font-mono', manifest.tokens.typography.fontMono);

    // ===== Radii =====
    const r = manifest.tokens.radii;
    setVar(root, 'radius-sm', r.sm);
    setVar(root, 'radius-md', r.md);
    setVar(root, 'radius-lg', r.lg);
    setVar(root, 'radius-xl', r.xl);
    setVar(root, 'radius-pill', r.pill);

    // ===== Shadows =====
    const s = manifest.tokens.shadows;
    setVar(root, 'shadow-sm', s.sm);
    setVar(root, 'shadow-md', s.md);
    setVar(root, 'shadow-lg', s.lg);
    setVar(root, 'shadow-overlay', s.overlay);

    // ===== Motion =====
    const m = manifest.tokens.motion;
    setVar(root, 'duration-fast', m.durationFast);
    setVar(root, 'duration-med', m.durationMed);
    setVar(root, 'ease-out', m.easeOut);

    // ===== Layout =====
    const l = manifest.tokens.layout;
    setVar(root, 'shell-topbar-h', l.topbarHeight);
    setVar(root, 'shell-sidebar-w', l.sidebarWidth);
    setVar(root, 'shell-sidebar-rail-w', l.sidebarRailWidth);

    // ===== Misc =====
    setVar(root, 'skeleton-shimmer', manifest.tokens.misc.skeletonShimmer);
    setVar(root, 'accent-focus-ring', manifest.tokens.misc.accentFocusRing);

    // ===== Backgrounds =====
    applyBackgroundSlot(root, 'canvas', manifest.backgrounds?.canvas, manifest.assetsBaseUrl);
    applyBackgroundSlot(root, 'page-header', manifest.backgrounds?.pageHeader, manifest.assetsBaseUrl);
    applyBackgroundSlot(root, 'player-scrim', manifest.backgrounds?.playerScrim, manifest.assetsBaseUrl);
    applyBackgroundSlot(root, 'login-canvas', manifest.backgrounds?.loginCanvas, manifest.assetsBaseUrl);

    // ===== Layout flags (data attributes) =====
    const lf = manifest.layout ?? {};
    root.setAttribute('data-vora-sidebar', lf.sidebar ?? 'full');
    root.setAttribute('data-vora-header', lf.header ?? 'rich');
    root.setAttribute('data-vora-density', lf.density ?? 'comfortable');
    root.setAttribute('data-vora-card', lf.card ?? 'elevated');

    // ===== Bookkeeping =====
    root.setAttribute('data-vora-theme-id', manifest.id);
}
