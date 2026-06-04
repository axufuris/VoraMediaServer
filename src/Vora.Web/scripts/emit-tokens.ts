import { mkdirSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import type { ThemeManifest } from '../src/theme/types';
import { voraDefault } from '../src/theme/themes/voraDefault';
import { voraDark } from '../src/theme/themes/voraDark';
import { voraOcean } from '../src/theme/themes/voraOcean';
import { voraCinema } from '../src/theme/clientTemplates/voraCinema';
import { voraNoir } from '../src/theme/clientTemplates/voraNoir';
import { voraVelvet } from '../src/theme/clientTemplates/voraVelvet';
import { voraAurora } from '../src/theme/clientTemplates/voraAurora';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../../../');
const outRoot = resolve(repoRoot, 'dist', 'tokens');

// Native clients bundle both registries — admin themes (used by the admin
// shell on web) and the four client templates that appear in the Settings
// picker. Bundling client templates means tapping vora-cinema, vora-noir,
// vora-velvet, or vora-aurora hot-swaps instantly on Android/iOS without a
// round-trip to /api/templates/{id}/manifest.
const themes: ThemeManifest[] = [
    voraDefault,
    voraDark,
    voraOcean,
    voraCinema,
    voraNoir,
    voraVelvet,
    voraAurora,
];

type Rgba = { r: number; g: number; b: number; a: number };

function parseColor(input: string): Rgba {
    const value = input.trim().toLowerCase();

    if (value.startsWith('#')) {
        return parseHex(value);
    }
    if (value.startsWith('rgba(') || value.startsWith('rgb(')) {
        return parseRgbFunction(value);
    }
    throw new Error(`Unsupported color literal: ${input}`);
}

function parseHex(value: string): Rgba {
    let hex = value.slice(1);
    if (hex.length === 3) {
        hex = hex.split('').map((c) => c + c).join('');
    }
    if (hex.length === 4) {
        hex = hex.split('').map((c) => c + c).join('');
    }
    if (hex.length !== 6 && hex.length !== 8) {
        throw new Error(`Bad hex color: ${value}`);
    }
    const r = parseInt(hex.slice(0, 2), 16);
    const g = parseInt(hex.slice(2, 4), 16);
    const b = parseInt(hex.slice(4, 6), 16);
    const a = hex.length === 8 ? parseInt(hex.slice(6, 8), 16) / 255 : 1;
    return { r, g, b, a };
}

function parseRgbFunction(value: string): Rgba {
    const inside = value.slice(value.indexOf('(') + 1, value.lastIndexOf(')'));
    const parts = inside.split(',').map((p) => p.trim());
    if (parts.length < 3 || parts.length > 4) {
        throw new Error(`Bad rgb()/rgba() color: ${value}`);
    }
    const r = parseInt(parts[0], 10);
    const g = parseInt(parts[1], 10);
    const b = parseInt(parts[2], 10);
    const a = parts.length === 4 ? parseFloat(parts[3]) : 1;
    return { r, g, b, a };
}

function parseDimensionPx(input: string): number {
    const trimmed = input.trim();
    if (!trimmed.endsWith('px')) {
        throw new Error(`Expected px dimension, got: ${input}`);
    }
    return parseFloat(trimmed.slice(0, -2));
}

function parseDurationMs(input: string): number {
    const trimmed = input.trim();
    if (!trimmed.endsWith('ms')) {
        throw new Error(`Expected ms duration, got: ${input}`);
    }
    return parseFloat(trimmed.slice(0, -2));
}

function toSwiftColor(input: string): string {
    const { r, g, b, a } = parseColor(input);
    const rN = (r / 255).toFixed(4);
    const gN = (g / 255).toFixed(4);
    const bN = (b / 255).toFixed(4);
    const aN = a.toFixed(4);
    return `Color(.sRGB, red: ${rN}, green: ${gN}, blue: ${bN}, opacity: ${aN})`;
}

function toKotlinColor(input: string): string {
    const { r, g, b, a } = parseColor(input);
    const alpha = Math.round(a * 255).toString(16).padStart(2, '0').toUpperCase();
    const rh = r.toString(16).padStart(2, '0').toUpperCase();
    const gh = g.toString(16).padStart(2, '0').toUpperCase();
    const bh = b.toString(16).padStart(2, '0').toUpperCase();
    return `Color(0x${alpha}${rh}${gh}${bh})`;
}

function pascal(input: string): string {
    return input.charAt(0).toUpperCase() + input.slice(1);
}

function swiftIdentifier(themeId: string): string {
    return themeId
        .split('-')
        .map((segment, index) => (index === 0 ? segment : pascal(segment)))
        .join('');
}

function kotlinIdentifier(themeId: string): string {
    return themeId.split('-').map(pascal).join('');
}

const swiftTypeDefinitions = `public struct VoraColors: Sendable {
    public let bgCanvas: Color
    public let bgSurface: Color
    public let bgRaised: Color
    public let bgSunken: Color
    public let bgOverlay: Color

    public let borderSubtle: Color
    public let borderStrong: Color
    public let borderFocus: Color

    public let textPrimary: Color
    public let textSecondary: Color
    public let textMuted: Color
    public let textDisabled: Color
    public let textInverse: Color

    public let accent500: Color
    public let accentHover: Color
    public let accentActive: Color
    public let accentSoft: Color
    public let accentSoftHover: Color
    public let accentText: Color
    public let accentContrast: Color

    public let success500: Color
    public let successSoft: Color
    public let successText: Color

    public let warning500: Color
    public let warningSoft: Color
    public let warningText: Color

    public let danger500: Color
    public let dangerSoft: Color
    public let dangerText: Color

    public let info500: Color
    public let infoSoft: Color
    public let infoText: Color
}

public struct VoraRadii: Sendable {
    public let sm: CGFloat
    public let md: CGFloat
    public let lg: CGFloat
    public let xl: CGFloat
    public let pill: CGFloat
}

public struct VoraMotion: Sendable {
    public let durationFast: Duration
    public let durationMed: Duration
    public let easeOut: String
}

public struct VoraLayout: Sendable {
    public let topbarHeight: CGFloat
    public let sidebarWidth: CGFloat
    public let sidebarRailWidth: CGFloat
}

public struct VoraTypography: Sendable {
    public let fontSans: String
    public let fontMono: String
}

public struct VoraMisc: Sendable {
    public let skeletonShimmer: Color
    public let accentFocusRing: Color
}

public struct VoraTokens: Sendable {
    public let id: String
    public let name: String
    public let version: String

    public let colors: VoraColors
    public let radii: VoraRadii
    public let motion: VoraMotion
    public let layout: VoraLayout
    public let typography: VoraTypography
    public let misc: VoraMisc
}`;

const kotlinTypeDefinitions = `data class VoraColors(
    val bgCanvas: Color,
    val bgSurface: Color,
    val bgRaised: Color,
    val bgSunken: Color,
    val bgOverlay: Color,

    val borderSubtle: Color,
    val borderStrong: Color,
    val borderFocus: Color,

    val textPrimary: Color,
    val textSecondary: Color,
    val textMuted: Color,
    val textDisabled: Color,
    val textInverse: Color,

    val accent500: Color,
    val accentHover: Color,
    val accentActive: Color,
    val accentSoft: Color,
    val accentSoftHover: Color,
    val accentText: Color,
    val accentContrast: Color,

    val success500: Color,
    val successSoft: Color,
    val successText: Color,

    val warning500: Color,
    val warningSoft: Color,
    val warningText: Color,

    val danger500: Color,
    val dangerSoft: Color,
    val dangerText: Color,

    val info500: Color,
    val infoSoft: Color,
    val infoText: Color,
)

data class VoraRadii(
    val sm: Dp,
    val md: Dp,
    val lg: Dp,
    val xl: Dp,
    val pill: Dp,
)

data class VoraMotion(
    val durationFast: Duration,
    val durationMed: Duration,
    val easeOut: String,
)

data class VoraLayout(
    val topbarHeight: Dp,
    val sidebarWidth: Dp,
    val sidebarRailWidth: Dp,
)

data class VoraTypography(
    val fontSans: String,
    val fontMono: String,
)

data class VoraMisc(
    val skeletonShimmer: Color,
    val accentFocusRing: Color,
)

data class VoraTokens(
    val id: String,
    val name: String,
    val version: String,

    val colors: VoraColors,
    val radii: VoraRadii,
    val motion: VoraMotion,
    val layout: VoraLayout,
    val typography: VoraTypography,
    val misc: VoraMisc,
)`;

function emitSwiftTheme(theme: ThemeManifest): string {
    const c = theme.tokens.colors;
    const r = theme.tokens.radii;
    const m = theme.tokens.motion;
    const layout = theme.tokens.layout;
    const misc = theme.tokens.misc;
    const typography = theme.tokens.typography;

    return `    public static let ${swiftIdentifier(theme.id)} = VoraTokens(
        id: "${theme.id}",
        name: "${theme.name}",
        version: "${theme.version}",
        colors: VoraColors(
            bgCanvas: ${toSwiftColor(c.bgCanvas)},
            bgSurface: ${toSwiftColor(c.bgSurface)},
            bgRaised: ${toSwiftColor(c.bgRaised)},
            bgSunken: ${toSwiftColor(c.bgSunken)},
            bgOverlay: ${toSwiftColor(c.bgOverlay)},
            borderSubtle: ${toSwiftColor(c.borderSubtle)},
            borderStrong: ${toSwiftColor(c.borderStrong)},
            borderFocus: ${toSwiftColor(c.borderFocus)},
            textPrimary: ${toSwiftColor(c.textPrimary)},
            textSecondary: ${toSwiftColor(c.textSecondary)},
            textMuted: ${toSwiftColor(c.textMuted)},
            textDisabled: ${toSwiftColor(c.textDisabled)},
            textInverse: ${toSwiftColor(c.textInverse)},
            accent500: ${toSwiftColor(c.accent500)},
            accentHover: ${toSwiftColor(c.accentHover)},
            accentActive: ${toSwiftColor(c.accentActive)},
            accentSoft: ${toSwiftColor(c.accentSoft)},
            accentSoftHover: ${toSwiftColor(c.accentSoftHover)},
            accentText: ${toSwiftColor(c.accentText)},
            accentContrast: ${toSwiftColor(c.accentContrast)},
            success500: ${toSwiftColor(c.success500)},
            successSoft: ${toSwiftColor(c.successSoft)},
            successText: ${toSwiftColor(c.successText)},
            warning500: ${toSwiftColor(c.warning500)},
            warningSoft: ${toSwiftColor(c.warningSoft)},
            warningText: ${toSwiftColor(c.warningText)},
            danger500: ${toSwiftColor(c.danger500)},
            dangerSoft: ${toSwiftColor(c.dangerSoft)},
            dangerText: ${toSwiftColor(c.dangerText)},
            info500: ${toSwiftColor(c.info500)},
            infoSoft: ${toSwiftColor(c.infoSoft)},
            infoText: ${toSwiftColor(c.infoText)}
        ),
        radii: VoraRadii(
            sm: ${parseDimensionPx(r.sm)},
            md: ${parseDimensionPx(r.md)},
            lg: ${parseDimensionPx(r.lg)},
            xl: ${parseDimensionPx(r.xl)},
            pill: ${parseDimensionPx(r.pill)}
        ),
        motion: VoraMotion(
            durationFast: .milliseconds(${parseDurationMs(m.durationFast)}),
            durationMed: .milliseconds(${parseDurationMs(m.durationMed)}),
            easeOut: ${JSON.stringify(m.easeOut)}
        ),
        layout: VoraLayout(
            topbarHeight: ${parseDimensionPx(layout.topbarHeight)},
            sidebarWidth: ${parseDimensionPx(layout.sidebarWidth)},
            sidebarRailWidth: ${parseDimensionPx(layout.sidebarRailWidth)}
        ),
        typography: VoraTypography(
            fontSans: ${JSON.stringify(typography.fontSans)},
            fontMono: ${JSON.stringify(typography.fontMono)}
        ),
        misc: VoraMisc(
            skeletonShimmer: ${toSwiftColor(misc.skeletonShimmer)},
            accentFocusRing: ${toSwiftColor(misc.accentFocusRing)}
        )
    )`;
}

function emitKotlinTheme(theme: ThemeManifest): string {
    const c = theme.tokens.colors;
    const r = theme.tokens.radii;
    const m = theme.tokens.motion;
    const layout = theme.tokens.layout;
    const misc = theme.tokens.misc;
    const typography = theme.tokens.typography;

    return `    val ${kotlinIdentifier(theme.id)} = VoraTokens(
        id = "${theme.id}",
        name = "${theme.name}",
        version = "${theme.version}",
        colors = VoraColors(
            bgCanvas = ${toKotlinColor(c.bgCanvas)},
            bgSurface = ${toKotlinColor(c.bgSurface)},
            bgRaised = ${toKotlinColor(c.bgRaised)},
            bgSunken = ${toKotlinColor(c.bgSunken)},
            bgOverlay = ${toKotlinColor(c.bgOverlay)},
            borderSubtle = ${toKotlinColor(c.borderSubtle)},
            borderStrong = ${toKotlinColor(c.borderStrong)},
            borderFocus = ${toKotlinColor(c.borderFocus)},
            textPrimary = ${toKotlinColor(c.textPrimary)},
            textSecondary = ${toKotlinColor(c.textSecondary)},
            textMuted = ${toKotlinColor(c.textMuted)},
            textDisabled = ${toKotlinColor(c.textDisabled)},
            textInverse = ${toKotlinColor(c.textInverse)},
            accent500 = ${toKotlinColor(c.accent500)},
            accentHover = ${toKotlinColor(c.accentHover)},
            accentActive = ${toKotlinColor(c.accentActive)},
            accentSoft = ${toKotlinColor(c.accentSoft)},
            accentSoftHover = ${toKotlinColor(c.accentSoftHover)},
            accentText = ${toKotlinColor(c.accentText)},
            accentContrast = ${toKotlinColor(c.accentContrast)},
            success500 = ${toKotlinColor(c.success500)},
            successSoft = ${toKotlinColor(c.successSoft)},
            successText = ${toKotlinColor(c.successText)},
            warning500 = ${toKotlinColor(c.warning500)},
            warningSoft = ${toKotlinColor(c.warningSoft)},
            warningText = ${toKotlinColor(c.warningText)},
            danger500 = ${toKotlinColor(c.danger500)},
            dangerSoft = ${toKotlinColor(c.dangerSoft)},
            dangerText = ${toKotlinColor(c.dangerText)},
            info500 = ${toKotlinColor(c.info500)},
            infoSoft = ${toKotlinColor(c.infoSoft)},
            infoText = ${toKotlinColor(c.infoText)},
        ),
        radii = VoraRadii(
            sm = ${parseDimensionPx(r.sm)}.dp,
            md = ${parseDimensionPx(r.md)}.dp,
            lg = ${parseDimensionPx(r.lg)}.dp,
            xl = ${parseDimensionPx(r.xl)}.dp,
            pill = ${parseDimensionPx(r.pill)}.dp,
        ),
        motion = VoraMotion(
            durationFast = ${parseDurationMs(m.durationFast)}.milliseconds,
            durationMed = ${parseDurationMs(m.durationMed)}.milliseconds,
            easeOut = ${JSON.stringify(m.easeOut)},
        ),
        layout = VoraLayout(
            topbarHeight = ${parseDimensionPx(layout.topbarHeight)}.dp,
            sidebarWidth = ${parseDimensionPx(layout.sidebarWidth)}.dp,
            sidebarRailWidth = ${parseDimensionPx(layout.sidebarRailWidth)}.dp,
        ),
        typography = VoraTypography(
            fontSans = ${JSON.stringify(typography.fontSans)},
            fontMono = ${JSON.stringify(typography.fontMono)},
        ),
        misc = VoraMisc(
            skeletonShimmer = ${toKotlinColor(misc.skeletonShimmer)},
            accentFocusRing = ${toKotlinColor(misc.accentFocusRing)},
        ),
    )`;
}

function emitSwiftFile(): string {
    const themeStatics = themes.map(emitSwiftTheme).join('\n\n');
    const themeNames = themes.map((t) => `VoraThemes.${swiftIdentifier(t.id)}`).join(', ');
    return `// VoraTokens.swift — generated by Vora.Web/scripts/emit-tokens.ts. Do not edit by hand.
//
// Regenerate by running, in src/Vora.Web/:
//   npm run emit-tokens
//
// Themes included: ${themeNames}
//
// Consume in a SwiftUI client by adding this file to your VoraCore Swift package.
// Pick a preset via \`VoraThemes.voraDefault\`; read the active theme inside a view
// via the environment value the hand-written VoraTheme accessor exposes.

import SwiftUI

${swiftTypeDefinitions}

public enum VoraThemes {
${themeStatics}
}
`;
}

function emitKotlinFile(): string {
    const themeStatics = themes.map(emitKotlinTheme).join('\n\n');
    const themeNames = themes.map((t) => `VoraThemes.${kotlinIdentifier(t.id)}`).join(', ');
    const allEntries = themes.map((t) => `        ${kotlinIdentifier(t.id)},`).join('\n');
    return `// VoraTokens.kt — generated by Vora.Web/scripts/emit-tokens.ts. Do not edit by hand.
//
// Regenerate by running, in src/Vora.Web/:
//   npm run emit-tokens
//
// Themes included: ${themeNames}
//
// Consume in a Compose client by placing this file in your :core module's source set
// (package com.vora.tokens). Pick a preset via \`VoraThemes.VoraDefault\`; read the
// active theme inside a Composable via \`VoraTheme.tokens\` (provided by the
// hand-written VoraThemeProvider.kt sibling file).

package com.vora.tokens

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import kotlin.time.Duration
import kotlin.time.Duration.Companion.milliseconds

${kotlinTypeDefinitions}

object VoraThemes {
${themeStatics}

    /** Every bundled theme in declaration order. Stable across regenerations. */
    val all: List<VoraTokens> = listOf(
${allEntries}
    )

    /**
     * Look up a bundled theme by its server-side id (e.g. \`vora-cinema\`).
     * Returns null for unknown ids — clients should fall through to fetching
     * the runtime manifest via /api/templates/{id}/manifest in that case.
     */
    fun byId(id: String): VoraTokens? = all.firstOrNull { it.id == id }
}
`;
}

function main(): void {
    if (existsSync(outRoot)) {
        rmSync(outRoot, { recursive: true, force: true });
    }
    mkdirSync(outRoot, { recursive: true });

    writeFileSync(resolve(outRoot, 'VoraTokens.swift'), emitSwiftFile());
    writeFileSync(resolve(outRoot, 'VoraTokens.kt'), emitKotlinFile());

    console.log(`[emit-tokens] ${themes.length} theme(s) → ${outRoot}/VoraTokens.{swift,kt}`);
}

main();
