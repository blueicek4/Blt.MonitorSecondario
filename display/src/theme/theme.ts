import { PALETTES, getPalette, type Palette, type PaletteKey } from '../tokens/themes';
import type { ThemeId, ThemeMode, ThemeTokens } from '../types/protocol';
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
 * - con `mode: 'auto'` si alternano le due varianti del preset e i colori dei token sono ignorati;
 * - con `mode` fisso i colori dei token (personalizzazioni del locale) vincono sul preset.
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

/** Comportamento di default (SPEC): foto articolo spenta, righe normali, QR accanto al conto. */
const DEFAULT_BEHAVIOR = {
  bgStyle: 'mesh',
  motion: 'full',
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
  const palette: Palette =
    t.mode === 'auto'
      ? base
      : {
          ...base,
          bg: t.cBg,
          surface: t.cSurface,
          text: t.cText,
          muted: t.cMuted,
          primary: t.cPrimary,
          onPrimary: t.cOnPrimary,
          accent: t.cAccent,
          secondary: t.cSecondary,
          fontDisplay: t.fontDisplay,
          fontText: t.fontText,
          radius: t.radius,
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

/** Le due coppie critiche: testo su fondo, testo sopra il primario. */
export function contrastProblems(palette: Palette): string[] {
  const pairs: Array<[label: string, fg: string, bg: string]> = [
    ['testo su fondo', palette.text, palette.bg],
    ['testo sul primario', palette.onPrimary, palette.primary],
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
