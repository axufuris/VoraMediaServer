# Admin Theme Bundles

How to author a plugin-shipped admin theme for Vora.

## What a bundle is

A theme bundle is a folder on the server at:

```
<Vora install>/Themes/<theme-id>/
```

It contains a `manifest.json` describing the theme's tokens, backgrounds, and layout flags, plus an optional `assets/` folder of images the manifest references.

The folder name **must** match the manifest's `id` field. Mismatches are rejected at load time.

The reserved ids `vora-default` and `vora-dark` cannot be used — they're the built-in themes that ship with Vora.

## Minimum layout

```
Themes/
  midnight-cinema/
    manifest.json
    assets/
      preview.png
      canvas-grain.svg
```

Theme bundles live in a top-level `Themes/` folder, separate from `Plugins/`. Code plugins (`.dll` files) and theme bundles (folders with `manifest.json`) are different artifacts and stay in different directories so the plugin loader never accidentally walks into a theme bundle.

## manifest.json

This is the same shape the frontend uses for its built-in themes. See
`src/Vora.Web/src/theme/types.ts` for the full TypeScript definition and
`src/Vora.Web/src/theme/themes/voraDark.ts` for a complete worked example.

Minimum required fields are `id`, `name`, `version`, and a `tokens` block. Everything else is optional.

```jsonc
{
  "id": "midnight-cinema",
  "name": "Midnight Cinema",
  "version": "1.0.0",
  "author": "You",
  "description": "Deep navy surfaces with a teal accent.",
  "preview": "preview.png",

  "tokens": {
    "colors": {
      "bgCanvas": "#0b0f17",
      "bgSurface": "#131a25",
      "bgRaised": "#1c2433",
      "bgSunken": "#070a10",
      "bgOverlay": "rgba(0,0,0,0.7)",

      "borderSubtle": "#1f2839",
      "borderStrong": "#2f3a52",
      "borderFocus": "#14b8a6",

      "textPrimary": "#f1f5f9",
      "textSecondary": "#cbd5e1",
      "textMuted": "#64748b",
      "textDisabled": "#475569",
      "textInverse": "#0b0f17",

      "accent500": "#14b8a6",
      "accentHover": "#2dd4bf",
      "accentActive": "#5eead4",
      "accentSoft": "rgba(20,184,166,0.15)",
      "accentSoftHover": "rgba(20,184,166,0.25)",
      "accentText": "#5eead4",
      "accentContrast": "#0b0f17",

      "success500": "#22c55e",
      "successSoft": "rgba(34,197,94,0.15)",
      "successText": "#86efac",

      "warning500": "#eab308",
      "warningSoft": "rgba(234,179,8,0.15)",
      "warningText": "#fde047",

      "danger500": "#ef4444",
      "dangerSoft": "rgba(239,68,68,0.15)",
      "dangerText": "#fca5a5",

      "info500": "#0ea5e9",
      "infoSoft": "rgba(14,165,233,0.15)",
      "infoText": "#7dd3fc"
    },

    "typography": {
      "fontSans": "\"Inter\", system-ui, sans-serif",
      "fontMono": "\"JetBrains Mono\", monospace"
    },

    "radii":   { "sm": "4px", "md": "6px", "lg": "10px", "xl": "14px", "pill": "9999px" },
    "shadows": {
      "sm":      "0 1px 2px rgba(0,0,0,0.4)",
      "md":      "0 4px 8px rgba(0,0,0,0.5)",
      "lg":      "0 12px 24px rgba(0,0,0,0.6)",
      "overlay": "0 24px 48px rgba(0,0,0,0.8)"
    },
    "motion": {
      "durationFast": "150ms",
      "durationMed":  "200ms",
      "easeOut":      "cubic-bezier(0.2, 0.0, 0.0, 1)"
    },
    "layout": {
      "topbarHeight":     "56px",
      "sidebarWidth":     "240px",
      "sidebarRailWidth": "64px"
    },
    "misc": {
      "skeletonShimmer": "#1c2433",
      "accentFocusRing": "rgba(20,184,166,0.25)"
    }
  },

  "backgrounds": {
    "canvas": {
      "image": "canvas-grain.svg",
      "opacity": 1,
      "position": "center",
      "size": "cover",
      "tint": "rgba(11,15,23,0.5)"
    },
    "pageHeader": null
  },

  "layout": {
    "sidebar":  "full",
    "header":   "rich",
    "density":  "comfortable",
    "card":     "elevated"
  }
}
```

## Images

Image references in `manifest.json` (the `preview` field and any
`backgrounds.*.image`) are **relative to the `assets/` folder**, not the
bundle root. So `"image": "canvas-grain.svg"` resolves to
`assets/canvas-grain.svg`.

Allowed image sources:

- `assets/...` paths inside your bundle (preferred — keeps the theme self-contained)
- `data:image/png;base64,...`, `data:image/svg+xml,...`, etc.
- `https://` URLs (external host — caveat: leaks the user's IP to that host)

`http://` URLs, `javascript:`, and anything that contains characters that
could escape a CSS `url("…")` context (`"`, `(`, `)`, `\`, newlines) are
rejected by the runtime sanitizer.

Vora serves your assets from `/api/admin/themes/<theme-id>/assets/<file>`.
That endpoint enforces path-traversal protection — any request that resolves
outside your bundle's `assets/` folder returns 404.

## The `tint` overlay (important)

The runtime applies image-slot backgrounds as a **layered** background:

```css
background-image:
    linear-gradient(<tint>, <tint>),
    url(<your-image>);
```

That is, the tint sits *over* the image as a constant-color overlay. This
is what keeps text on top of the surface legible regardless of what image
you choose. Set the `tint` to a semi-transparent version of the surface's
solid color (`tokens.colors.bgCanvas` for the canvas slot, etc.) and adjust
the alpha until the image reads at the strength you want.

## Activating

Drop the folder, restart the Vora API (the bundle loader runs once at
startup), then go to **Admin → Server → Appearance** and your theme appears
in the picker alongside the built-ins, tagged with a "Plugin" badge. Click
"Set active" to apply it server-wide.

## Troubleshooting

The bundle loader logs to the API console at startup. Look for lines
prefixed `[Theme bundles]`:

- `Loaded '<id>' (<name>)` — bundle parsed successfully.
- `Skipping <folder>: missing manifest.json` — your folder doesn't have a manifest at the top level.
- `Skipping <folder>: duplicate theme id '<id>'` — two bundles declare the same id; one was already loaded.
- `Rejected <folder>: <reason>` — the manifest is malformed or violates a constraint (folder name vs id mismatch, reserved id, missing required fields).
