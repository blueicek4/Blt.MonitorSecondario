import { PALETTES, getPalette, type Palette, type PaletteKey } from '../tokens/themes';
import type { ThemeId, ThemeMode, ThemeTokens } from '../types/protocol';
import { alphaOf, composite, mix, parseHex, recolor, withAlpha, type Rgb } from './color';
import { contrastRatio } from './contrast';

/** Sotto questa soglia un tema non si applica (SPEC §6, §10). */
export const MIN_CONTRAST = 4.5;
export const FALLBACK_KEY: PaletteKey = 'bluetech-notte';

/**
 * Cosa sceglie il locale. Il pacchetto passa ai componenti un `ThemeTokens`, ma i token non dicono
 * da quale tema partono e portano una sola serie di colori: con `mode: 'auto'` non si saprebbe
 * cosa mostrare di giorno. Decisione M1 (design/ALLINEAMENTO.md §7):
 *
 * - `preset` è uno degli otto set progettati (tema × giorno/notte) e fornisce anche i colori che
 *   ThemeTokens non ha (superficie opaca, filetti, blocco totale, bagliori);
 * - con `mode: 'auto'` si alternano le due varianti del preset: i colori dei token sono ignorati,
 *   font e raggio no (sono gli stessi nelle due varianti);
 * - con `mode` fisso i colori dei token (personalizzazioni del locale) vincono sul preset, e i
 *   colori che il preset ricava da quelli (blocco totale, bagliori…) vengono ricalcolati.
 */
export interface ThemeConfig {
  preset: ThemeId;
  tokens: ThemeTokens;
}

/** Ore del giorno, 0–24: è giorno se `start <= ora < end`. */
export interface DayHours {
  start: number;
  end: number;
}

export const DEFAULT_DAY_HOURS: DayHours = { start: 7, end: 19 };

export function modeAt(date: Date, hours: DayHours = DEFAULT_DAY_HOURS): ThemeMode {
  const hour = date.getHours() + date.getMinutes() / 60;
  return hour >= hours.start && hour < hours.end ? 'giorno' : 'notte';
}

/**
 * Comportamento di default: foto articolo spenta, righe normali, QR accanto al conto (prototipo),
 * animazioni "leggere" (MOTION.md: il default consigliato, quello su cui si tara la demo).
 */
const DEFAULT_BEHAVIOR = {
  bgStyle: 'mesh',
  motion: 'light',
  qrLayout: 'beside',
  compactRows: false,
  showLastItemPhoto: false,
} as const satisfies Partial<ThemeTokens>;

/** Configurazione di un preset, con i token presi dalla sua variante (notte se `auto`). */
export function presetConfig(
  preset: ThemeId,
  mode: ThemeMode | 'auto',
  behavior: Partial<Omit<ThemeTokens, 'mode'>> = {},
): ThemeConfig {
  const palette = getPalette(preset, mode === 'auto' ? 'notte' : mode);
  return {
    preset,
    tokens: {
      cPrimary: palette.primary,
      cSecondary: palette.secondary,
      cAccent: palette.accent,
      cBg: palette.bg,
      cSurface: palette.surface,
      cText: palette.text,
      cMuted: palette.muted,
      cOnPrimary: palette.onPrimary,
      fontDisplay: palette.fontDisplay,
      fontText: palette.fontText,
      radius: palette.radius,
      glass: palette.glass,
      footerVariant: palette.footerVariant,
      ...DEFAULT_BEHAVIOR,
      ...behavior,
      mode,
    },
  };
}

export interface ResolvedTheme {
  palette: Palette;
  /** Set applicato; 'custom' se i colori del locale hanno modificato il preset. */
  key: PaletteKey | 'custom';
  mode: ThemeMode;
  /** true se il tema non passava il controllo di contrasto ed è stato sostituito. */
  fallback: boolean;
  problems: string[];
}

export function resolveTheme(config: ThemeConfig, mode: ThemeMode): ResolvedTheme {
  const base = getPalette(config.preset, mode);
  const t = config.tokens;
  const shape = { fontDisplay: t.fontDisplay, fontText: t.fontText, radius: t.radius };
  const palette: Palette =
    t.mode === 'auto'
      ? { ...base, ...shape }
      : {
          ...withLocaleColors(base, {
            bg: t.cBg,
            surface: t.cSurface,
            text: t.cText,
            muted: t.cMuted,
            primary: t.cPrimary,
            onPrimary: t.cOnPrimary,
            accent: t.cAccent,
            secondary: t.cSecondary,
          }),
          ...shape,
          glass: t.glass,
          footerVariant: t.footerVariant,
        };

  const problems = contrastProblems(palette);
  if (problems.length > 0) {
    return {
      palette: PALETTES[FALLBACK_KEY],
      key: FALLBACK_KEY,
      mode: 'notte',
      fallback: true,
      problems,
    };
  }
  const key: PaletteKey = `${config.preset}-${mode}`;
  return {
    palette,
    key: samePalette(palette, base) ? key : 'custom',
    mode,
    fallback: false,
    problems,
  };
}

type LocaleColors = Pick<
  Palette,
  'bg' | 'surface' | 'text' | 'muted' | 'primary' | 'onPrimary' | 'accent' | 'secondary'
>;

/** In quest'ordine, se due colori del preset coincidono vince il primo. */
const RECOLOR_ORDER = [
  'primary',
  'onPrimary',
  'text',
  'secondary',
  'accent',
  'bg',
  'muted',
] as const;

/**
 * Colori del locale sopra il preset. I colori che il preset ricava dagli altri vanno ricalcolati:
 * altrimenti, per esempio, il testo chiaro del totale di un preset scuro resterebbe su un fondo
 * chiaro scelto dal locale. Quelli che dipendono solo da colori non cambiati restano del preset.
 */
function withLocaleColors(base: Palette, colors: LocaleColors): Palette {
  const map = new Map<string, Rgb>();
  for (const key of RECOLOR_ORDER) {
    const from = parseHex(base[key]);
    const to = parseHex(colors[key]);
    if (from && to && colors[key] !== base[key] && !map.has(from.join(','))) {
      map.set(from.join(','), to);
    }
  }
  const unchanged = (...keys: Array<keyof LocaleColors>) =>
    keys.every((key) => colors[key] === base[key]);
  return {
    ...base,
    ...colors,
    surfaceSolid: unchanged('surface', 'bg')
      ? base.surfaceSolid
      : (composite(colors.surface, colors.bg) ?? recolor(base.surfaceSolid, map)),
    line: unchanged('text')
      ? base.line
      : (withAlpha(colors.text, alphaOf(base.line) ?? 0.12) ?? recolor(base.line, map)),
    faint: unchanged('muted', 'bg')
      ? base.faint
      : (mix(colors.muted, colors.bg, 0.35) ?? recolor(base.faint, map)),
    totalFill: recolor(base.totalFill, map),
    totalBorder: recolor(base.totalBorder, map),
    totalText: recolor(base.totalText, map),
    glow: recolor(base.glow, map),
    glow2: recolor(base.glow2, map),
  };
}

/**
 * Le coppie critiche: testo su fondo e sulla card, testo sopra il primario, cifra del totale sul suo
 * blocco (se il blocco è trasparente o sfumato, sulla card che gli sta sotto).
 */
export function contrastProblems(palette: Palette): string[] {
  const totalBehind = parseHex(palette.totalFill) ? palette.totalFill : palette.surfaceSolid;
  const pairs: Array<[label: string, fg: string, bg: string]> = [
    ['testo su fondo', palette.text, palette.bg],
    ['testo sulla card', palette.text, palette.surfaceSolid],
    ['testo sul primario', palette.onPrimary, palette.primary],
    ['totale', palette.totalText, totalBehind],
  ];
  const problems: string[] = [];
  for (const [label, fg, bg] of pairs) {
    const ratio = contrastRatio(fg, bg);
    if (ratio === null)
      problems.push(`${label}: colori non verificabili (${fg} su ${bg}), servono #rrggbb opachi`);
    else if (ratio < MIN_CONTRAST)
      problems.push(`${label}: contrasto ${ratio.toFixed(2)}:1 sotto ${MIN_CONTRAST}:1`);
  }
  return problems;
}

function samePalette(a: Palette, b: Palette): boolean {
  return (Object.keys(a) as Array<keyof Palette>).every((k) => a[k] === b[k]);
}
