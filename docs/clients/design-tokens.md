# Design tokens (cross-platform)

Single source of truth for colors, typography, radii, shadows, motion, and layout dimensions across every Vora client. The web client consumes them as CSS custom properties at runtime via `ThemeProvider`. Native clients consume them as **generated Swift and Kotlin files** emitted from the same source by `Vora.Web/scripts/emit-tokens.ts`.

This file documents the source of truth, the emitter, and the consumption model. The ADR is [`docs/adr/0002-client-platform-strategy.md`](../adr/0002-client-platform-strategy.md).

## Source of truth

The schema is `ThemeManifest` in `Vora.Web/src/theme/types.ts`. Concrete themes live in `Vora.Web/src/theme/themes/`:

- `voraDefault.ts` — light, warm-neutral, amber accent (the cinematic default).
- `voraDark.ts` — dark canvas variant.
- `voraOcean.ts` — ocean-blue accent variant.

A theme manifest is a plain TypeScript object — no React, no DOM. The `tokens` sub-tree carries colors, typography, radii, shadows, motion, layout, and misc values; `backgrounds` and `layout` carry web-specific extras (image slots, sidebar flags) that native renderers ignore.

When a token slot is added or renamed:

1. Add the property to the matching interface in `theme/types.ts` (`TokenColors`, `TokenRadii`, …).
2. Fill the value into every concrete theme under `theme/themes/`.
3. Update `Vora.Web/scripts/emit-tokens.ts` so the Swift and Kotlin emitters include the new field. Both `data class` / `struct` definitions and the per-theme value blocks need the new line.
4. Run `npm run emit-tokens` and commit the regenerated `dist/tokens/*` files (see "Workflow" below).

## Emitter

The emitter is `Vora.Web/scripts/emit-tokens.ts`. Run it from `src/Vora.Web/`:

```bash
npm run emit-tokens
```

For each theme manifest, the emitter writes two files under `<repo>/dist/tokens/<themeId>/`:

```
dist/tokens/
  vora-default/
    VoraTokens.swift
    VoraTokens.kt
  vora-dark/
    VoraTokens.swift
    VoraTokens.kt
  vora-ocean/
    VoraTokens.swift
    VoraTokens.kt
```

What the emitter does:

- Parses color literals (`#rrggbb`, `#rrggbbaa`, `rgba(r,g,b,a)`) into platform-native color values: SwiftUI `Color(.sRGB, red:green:blue:opacity:)` and Compose `Color(0xAARRGGBB)`.
- Parses `px` dimensions into SwiftUI `CGFloat` and Compose `Dp`.
- Parses `ms` durations into SwiftUI `Duration.milliseconds(…)` and Compose `Int.milliseconds`.
- Passes through CSS-only strings (font stacks, cubic-bezier easing) as `String` properties. The native consumer translates them at the use site (e.g. `Animation.timingCurve(...)` in SwiftUI, `CubicBezierEasing` in Compose).
- Adds a header comment to each output file naming the source script and the regeneration command, so a developer touching the file knows it's generated.

The emitter is self-contained: no external dependencies beyond `tsx` (a Node TypeScript runner, added as a devDep). It does not depend on Style Dictionary or any other tokens framework.

## Workflow

Two reasonable patterns for getting the emitted files into the native repos. Pick one per native repo and stick with it.

**Pattern A: commit `dist/tokens/` and have native repos pull this repo as a git source.** Lower friction for the native repos — they grab the latest emitted file and paste it into their tree (or symlink, or copy in their build). Means this repo carries generated files in git history.

**Pattern B: gitignore `dist/tokens/` and have native repos run the emitter themselves.** Cleaner git history here. Native repos need Node + tsx + the `Vora.Web` source tree available at build time. Heavier for the native build pipelines.

Default recommendation is **Pattern A** while there are only one or two consumers — the friction tradeoff favors committing the artifact. Switch to B once a third client appears or CI starts running the emitter as a verification step.

`dist/tokens/` is currently not in the root `.gitignore`. Add it if you switch to Pattern B.

## Consumption — web

The web client does **not** consume the emitted files. It keeps the existing runtime model: `ThemeProvider` applies a `ThemeManifest` directly to `:root` as CSS custom properties via `applyTheme()`. The fallback values in `styles/tokens.css` cover first paint. The emitted Swift and Kotlin files exist purely for the native clients.

## Consumption — Apple (SwiftUI)

The emitted `VoraTokens.swift` declares one `VoraTokens` struct per theme, hung off the `VoraTheme` enum. Drop the file into the `VoraCore` Swift package, then:

```swift
import SwiftUI

struct PosterRail: View {
    @Environment(\.voraTokens) private var tokens

    var body: some View {
        ScrollView(.horizontal) {
            // ...
        }
        .background(tokens.colors.bgSurface)
    }
}
```

The recommended pattern is a SwiftUI `EnvironmentKey` so the active theme propagates down the view tree. Sketch:

```swift
private struct VoraTokensKey: EnvironmentKey {
    static let defaultValue: VoraTokens = VoraTheme.voraDefault
}

extension EnvironmentValues {
    var voraTokens: VoraTokens {
        get { self[VoraTokensKey.self] }
        set { self[VoraTokensKey.self] = newValue }
    }
}
```

When the user picks a different theme, set the environment value at the root: `.environment(\.voraTokens, VoraTheme.voraDark)`.

## Consumption — Android (Compose)

The emitted `VoraTokens.kt` declares one `VoraTokens` data class per theme, hung off the `VoraTheme` object. Drop the file into `:core/src/main/kotlin/com/vora/tokens/`, then:

```kotlin
@Composable
fun PosterRail() {
    val tokens = LocalVoraTokens.current
    LazyRow(
        modifier = Modifier.background(tokens.colors.bgSurface),
    ) {
        // ...
    }
}
```

The recommended pattern is a Compose `CompositionLocal`:

```kotlin
val LocalVoraTokens = staticCompositionLocalOf<VoraTokens> {
    error("VoraTokens not provided")
}

@Composable
fun VoraThemeProvider(tokens: VoraTokens, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalVoraTokens provides tokens) {
        content()
    }
}
```

Wrap the app root with `VoraThemeProvider(tokens = VoraTheme.VoraDefault) { ... }`. Theme switching becomes recomposition.

## Schema reference

See `Vora.Web/src/theme/types.ts` for the authoritative shape — these doc tables drift fast. As of this writing, the token tree contains:

- **colors** — surfaces (`bgCanvas`, `bgSurface`, `bgRaised`, `bgSunken`, `bgOverlay`), borders (`borderSubtle`, `borderStrong`, `borderFocus`), text (`textPrimary`, `textSecondary`, `textMuted`, `textDisabled`, `textInverse`), accent (`accent500`, `accentHover`, `accentActive`, `accentSoft`, `accentSoftHover`, `accentText`, `accentContrast`), semantic (`success500`/`successSoft`/`successText`, same for `warning`/`danger`/`info`).
- **typography** — `fontSans`, `fontMono` (CSS font-family stacks).
- **radii** — `sm`, `md`, `lg`, `xl`, `pill`.
- **shadows** — `sm`, `md`, `lg`, `overlay` (CSS box-shadow strings; native consumers translate at the use site).
- **motion** — `durationFast`, `durationMed`, `easeOut` (the cubic-bezier).
- **layout** — `topbarHeight`, `sidebarWidth`, `sidebarRailWidth` (web-shell dimensions; ignore on TV).
- **misc** — `skeletonShimmer`, `accentFocusRing`.

The `backgrounds` and `layout` (flags) blocks on `ThemeManifest` are web-only and not emitted.

## What's deliberately not in the token tree

- **Spacing scale** — there isn't one. The web uses Tailwind utilities directly; native clients should bring their own (4/8/12/16/24/32 progression is what the web reads as visually).
- **Z-index** — modal stacking is web-specific (`docs/redesign/design-language.md` covers it). Native renderers have their own modal/overlay stacks (Sheet, Popover, fullScreenCover on SwiftUI; ModalBottomSheet, Dialog on Compose).
- **Iconography** — stroke-style icons referenced in `design-language.md` are shipped as SVG in the web. Native clients use SF Symbols on Apple and Material Symbols on Android by default, matched as closely as possible. Per-icon asset choices are not tokenized.
- **Web shell chrome** — anything under `ThemeManifest.layout` (sidebar mode, header mode, density, card flavor) is web-specific styling and intentionally not emitted to native.

## Versioning

Each emitted file records the theme `version` from the manifest in its header comment. When a token slot is added or renamed in a breaking way, bump the manifest version. Native clients that consume an old emitted file will continue to compile until a fresh emit replaces it; this prevents accidental in-flight rebuilds from breaking other clients.
