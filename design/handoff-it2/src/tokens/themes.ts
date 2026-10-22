/**
 * Bluetech Display — i dieci temi (iterazione 2).
 *
 * Cinque scuri e cinque chiari. Sostituisce le otto palette dell'iterazione 1
 * (quattro temi × giorno/notte): la migrazione dei valori salvati è in
 * LEGACY_THEME in fondo al file.
 *
 * Quattro coppie si scambiano con `modo: 'auto'` sull'orario di apertura:
 * Bluetech↔Giorno, Brace↔Forno, Riserva↔Sale, Onda↔Menta. Notturno e Latte
 * non hanno gemello: con 'auto' restano fissi, e il back office lo dice.
 *
 * Tutti i rapporti nei commenti sono CALCOLATI (WCAG 2.1, luminanza relativa)
 * da uno script, non stimati. `faint` è derivato per stare ≥ 4,6:1 sia sul
 * fondo sia sulla superficie piena. Nessuna coppia scende sotto 4,5:1.
 * Quando il primario non regge il bianco sopra (Notturno, Riserva),
 * `onPrimary` è il colore del fondo.
 */

export type ThemeId =
  | 'bluetech' | 'brace' | 'notturno' | 'riserva' | 'onda'
  | 'giorno' | 'forno' | 'latte' | 'sale' | 'menta';

export type ThemeModeSetting = 'fisso' | 'auto';

export interface Palette {
  id: ThemeId;
  nome: string;
  scuro: boolean;
  /** Il tema con cui si scambia in modo 'auto'. null = nessun gemello. */
  gemello: ThemeId | null;

  bg: string;
  /** card: rgba su bg quando glass > 0 */
  surface: string;
  surfaceSolid: string;
  line: string;
  text: string;
  muted: string;
  /** righe stornate, didascalie minori. ≥ 4,6:1 su bg e su surfaceSolid */
  faint: string;
  primary: string;
  onPrimary: string;
  /** sconti e maggiorazioni */
  accent: string;
  secondary: string;

  fontDisplay: string;
  fontText: string;
  radius: number;
  /** 0–100. Sui temi chiari è 0: al posto del vetro c'è un'ombra. */
  glass: number;

  totalFill: string;
  totalBorder: string;
  totalText: string;

  glow: string;
  glow2: string;

  footerVariant: 'light' | 'dark';
}

export const PALETTES: Record<ThemeId, Palette> = {
  // BLUETECH — Il default. Neutro e tecnologico.
  bluetech: {
    id: 'bluetech', nome: 'Bluetech', scuro: true, gemello: 'giorno',
    bg: '#0B1017',
    surface: 'rgba(255,255,255,0.06)',
    surfaceSolid: '#141B26',
    line: 'rgba(255,255,255,0.1)',
    text: '#E6EAF2', // 15,82:1 su bg, 14,34:1 su surfaceSolid
    muted: '#9AA6BA', // 7,76:1 su bg, 7,03:1 su surfaceSolid
    faint: '#7B8596', // 5,12:1 su bg, 4,64:1 su surfaceSolid
    primary: '#1F6FEB',
    onPrimary: '#FFFFFF', // 4,63:1 su primary
    accent: '#3DDC97', // 10,80:1 su bg, 9,78:1 su surfaceSolid
    secondary: '#8FA3C0',
    fontDisplay: "'Archivo', 'Arial Narrow', sans-serif",
    fontText: "'IBM Plex Sans', 'Segoe UI', sans-serif",
    radius: 10,
    glass: 40,
    totalFill: 'linear-gradient(180deg, rgba(31,111,235,0.24), rgba(31,111,235,0.06))',
    totalBorder: 'rgba(31,111,235,0.5)',
    totalText: '#E6EAF2',
    glow: '#1F6FEB',
    glow2: '#3DDC97',
    footerVariant: 'light',
  },
  // BRACE — Griglia, carne, steakhouse.
  brace: {
    id: 'brace', nome: 'Brace', scuro: true, gemello: 'forno',
    bg: '#121010',
    surface: 'rgba(242,232,224,0.06)',
    surfaceSolid: '#1C1817',
    line: 'rgba(242,232,224,0.13)',
    text: '#F2E8E0', // 15,71:1 su bg, 14,59:1 su surfaceSolid
    muted: '#B5A69C', // 8,04:1 su bg, 7,46:1 su surfaceSolid
    faint: '#8C8179', // 5,00:1 su bg, 4,64:1 su surfaceSolid
    primary: '#BC4A17',
    onPrimary: '#FFFFFF', // 5,08:1 su primary
    accent: '#7FC98F', // 9,62:1 su bg, 8,93:1 su surfaceSolid
    secondary: '#B5A69C',
    fontDisplay: "'Oswald', 'Arial Narrow', sans-serif",
    fontText: "'IBM Plex Sans', 'Segoe UI', sans-serif",
    radius: 6,
    glass: 40,
    totalFill: '#BC4A17',
    totalBorder: '#BC4A17',
    totalText: '#FFFFFF', // 5,08:1
    glow: '#BC4A17',
    glow2: '#7FC98F',
    footerVariant: 'light',
  },
  // NOTTURNO — Cocktail bar e locali serali. Nessuna variante giorno.
  notturno: {
    id: 'notturno', nome: 'Notturno', scuro: true, gemello: null,
    bg: '#0A0616',
    surface: 'rgba(240,234,255,0.07)',
    surfaceSolid: '#16102B',
    line: 'rgba(240,234,255,0.15)',
    text: '#F0EAFF', // 17,06:1 su bg, 15,67:1 su surfaceSolid
    muted: '#A99BC8', // 7,81:1 su bg, 7,18:1 su surfaceSolid
    faint: '#867AA1', // 5,06:1 su bg, 4,65:1 su surfaceSolid
    primary: '#C06CFF',
    onPrimary: '#0A0616', // 6,50:1 su primary
    accent: '#35F5C8', // 14,32:1 su bg, 13,16:1 su surfaceSolid
    secondary: '#A99BC8',
    fontDisplay: "'Syne', 'Segoe UI', sans-serif",
    fontText: "'Space Grotesk', 'Segoe UI', sans-serif",
    radius: 9,
    glass: 60,
    totalFill: 'linear-gradient(180deg, rgba(192,108,255,0.3), rgba(53,245,200,0.08))',
    totalBorder: 'rgba(192,108,255,0.6)',
    totalText: '#F0EAFF',
    glow: '#C06CFF',
    glow2: '#35F5C8',
    footerVariant: 'light',
  },
  // RISERVA — Fine dining. Nessun vetro, nessun gradiente.
  riserva: {
    id: 'riserva', nome: 'Riserva', scuro: true, gemello: 'sale',
    bg: '#0C0B09',
    surface: 'rgba(237,230,215,0.05)',
    surfaceSolid: '#16140F',
    line: 'rgba(237,230,215,0.14)',
    text: '#EDE6D7', // 15,83:1 su bg, 14,81:1 su surfaceSolid
    muted: '#A39781', // 6,84:1 su bg, 6,40:1 su surfaceSolid
    faint: '#897F6D', // 4,98:1 su bg, 4,66:1 su surfaceSolid
    primary: '#C9A227',
    onPrimary: '#0C0B09', // 8,13:1 su primary
    accent: '#8FB39B', // 8,51:1 su bg, 7,97:1 su surfaceSolid
    secondary: '#A39781',
    fontDisplay: "'Cormorant Garamond', Georgia, serif",
    fontText: "'Jost', 'Segoe UI', sans-serif",
    radius: 2,
    glass: 0,
    totalFill: 'transparent',
    totalBorder: '#C9A227',
    totalText: '#EDE6D7',
    glow: '#C9A227',
    glow2: '#8FB39B',
    footerVariant: 'light',
  },
  // ONDA — Cucina di mare.
  onda: {
    id: 'onda', nome: 'Onda', scuro: true, gemello: 'menta',
    bg: '#06151A',
    surface: 'rgba(230,242,242,0.06)',
    surfaceSolid: '#0D2129',
    line: 'rgba(230,242,242,0.13)',
    text: '#E6F2F2', // 16,24:1 su bg, 14,48:1 su surfaceSolid
    muted: '#94B0B4', // 8,08:1 su bg, 7,21:1 su surfaceSolid
    faint: '#738C91', // 5,21:1 su bg, 4,65:1 su surfaceSolid
    primary: '#15707F',
    onPrimary: '#FFFFFF', // 5,74:1 su primary
    accent: '#F2B544', // 10,15:1 su bg, 9,05:1 su surfaceSolid
    secondary: '#94B0B4',
    fontDisplay: "'Spectral', Georgia, serif",
    fontText: "'Karla', 'Segoe UI', sans-serif",
    radius: 16,
    glass: 40,
    totalFill: '#15707F',
    totalBorder: '#15707F',
    totalText: '#FFFFFF', // 5,74:1
    glow: '#15707F',
    glow2: '#F2B544',
    footerVariant: 'light',
  },
  // GIORNO — Il default chiaro. Ombra al posto del vetro.
  giorno: {
    id: 'giorno', nome: 'Giorno', scuro: false, gemello: 'bluetech',
    bg: '#F4F6FA',
    surface: '#FFFFFF',
    surfaceSolid: '#FFFFFF',
    line: 'rgba(16,21,29,0.1)',
    text: '#10151D', // 16,92:1 su bg, 18,31:1 su surfaceSolid
    muted: '#5A6679', // 5,37:1 su bg, 5,81:1 su surfaceSolid
    faint: '#657082', // 4,63:1 su bg, 5,01:1 su surfaceSolid
    primary: '#1757C2',
    onPrimary: '#FFFFFF', // 6,61:1 su primary
    accent: '#0E7A52', // 4,95:1 su bg, 5,35:1 su surfaceSolid
    secondary: '#5A6679',
    fontDisplay: "'Archivo', 'Arial Narrow', sans-serif",
    fontText: "'IBM Plex Sans', 'Segoe UI', sans-serif",
    radius: 10,
    glass: 0,
    totalFill: '#1757C2',
    totalBorder: '#1757C2',
    totalText: '#FFFFFF', // 6,61:1
    glow: '#1757C2',
    glow2: '#0E7A52',
    footerVariant: 'dark',
  },
  // FORNO — Pizzeria e trattoria.
  forno: {
    id: 'forno', nome: 'Forno', scuro: false, gemello: 'brace',
    bg: '#F6EEE2',
    surface: '#FFFBF4',
    surfaceSolid: '#FFFBF4',
    line: 'rgba(43,33,26,0.13)',
    text: '#2B211A', // 13,67:1 su bg, 15,25:1 su surfaceSolid
    muted: '#6B5B49', // 5,67:1 su bg, 6,33:1 su surfaceSolid
    faint: '#786857', // 4,66:1 su bg, 5,20:1 su surfaceSolid
    primary: '#9C3A1F',
    onPrimary: '#FFFFFF', // 6,91:1 su primary
    accent: '#1E6B4F', // 5,58:1 su bg, 6,22:1 su surfaceSolid
    secondary: '#6B5B49',
    fontDisplay: "'Newsreader', Georgia, serif",
    fontText: "'Alegreya Sans', 'Segoe UI', sans-serif",
    radius: 6,
    glass: 0,
    totalFill: '#9C3A1F',
    totalBorder: '#9C3A1F',
    totalText: '#FFFFFF', // 6,91:1
    glow: '#9C3A1F',
    glow2: '#1E6B4F',
    footerVariant: 'dark',
  },
  // LATTE — Caffetteria, forneria, gastronomia. Nessuna variante notte.
  latte: {
    id: 'latte', nome: 'Latte', scuro: false, gemello: null,
    bg: '#FBF7F2',
    surface: '#FFFFFF',
    surfaceSolid: '#FFFFFF',
    line: 'rgba(42,33,26,0.1)',
    text: '#2A211A', // 14,79:1 su bg, 15,78:1 su surfaceSolid
    muted: '#6E6155', // 5,62:1 su bg, 5,99:1 su surfaceSolid
    faint: '#7B6E63', // 4,63:1 su bg, 4,94:1 su surfaceSolid
    primary: '#7A5230',
    onPrimary: '#FFFFFF', // 6,82:1 su primary
    accent: '#1F6E52', // 5,77:1 su bg, 6,16:1 su surfaceSolid
    secondary: '#6E6155',
    fontDisplay: "'Lora', Georgia, serif",
    fontText: "'Work Sans', 'Segoe UI', sans-serif",
    radius: 12,
    glass: 0,
    totalFill: '#7A5230',
    totalBorder: '#7A5230',
    totalText: '#FFFFFF', // 6,82:1
    glow: '#7A5230',
    glow2: '#1F6E52',
    footerVariant: 'dark',
  },
  // SALE — Fine dining di giorno.
  sale: {
    id: 'sale', nome: 'Sale', scuro: false, gemello: 'riserva',
    bg: '#F7F4EC',
    surface: '#FFFDF7',
    surfaceSolid: '#FFFDF7',
    line: 'rgba(26,23,18,0.13)',
    text: '#1A1712', // 16,26:1 su bg, 17,57:1 su surfaceSolid
    muted: '#6E6454', // 5,29:1 su bg, 5,71:1 su surfaceSolid
    faint: '#766D5D', // 4,64:1 su bg, 5,02:1 su surfaceSolid
    primary: '#8A6D14',
    onPrimary: '#FFFFFF', // 4,91:1 su primary
    accent: '#3F6B55', // 5,55:1 su bg, 5,99:1 su surfaceSolid
    secondary: '#6E6454',
    fontDisplay: "'Cormorant Garamond', Georgia, serif",
    fontText: "'Jost', 'Segoe UI', sans-serif",
    radius: 2,
    glass: 0,
    totalFill: 'transparent',
    totalBorder: '#8A6D14',
    totalText: '#1A1712',
    glow: '#8A6D14',
    glow2: '#3F6B55',
    footerVariant: 'dark',
  },
  // MENTA — Bistrot e bar moderni.
  menta: {
    id: 'menta', nome: 'Menta', scuro: false, gemello: 'onda',
    bg: '#F2F7F4',
    surface: '#FFFFFF',
    surfaceSolid: '#FFFFFF',
    line: 'rgba(16,35,28,0.1)',
    text: '#10231C', // 15,15:1 su bg, 16,42:1 su surfaceSolid
    muted: '#51665E', // 5,68:1 su bg, 6,15:1 su surfaceSolid
    faint: '#5F736C', // 4,67:1 su bg, 5,05:1 su surfaceSolid
    primary: '#116B4F',
    onPrimary: '#FFFFFF', // 6,48:1 su primary
    accent: '#B4451A', // 5,09:1 su bg, 5,52:1 su surfaceSolid
    secondary: '#51665E',
    fontDisplay: "'Outfit', 'Segoe UI', sans-serif",
    fontText: "'Karla', 'Segoe UI', sans-serif",
    radius: 14,
    glass: 0,
    totalFill: '#116B4F',
    totalBorder: '#116B4F',
    totalText: '#FFFFFF', // 6,48:1
    glow: '#116B4F',
    glow2: '#B4451A',
    footerVariant: 'dark',
  },
};

/**
 * Il tema effettivo in questo momento.
 * `diGiorno` lo calcola chi chiama, dagli orari giornoDalle/giornoAlle del marchio.
 */
export function resolveTheme(tema: ThemeId, modo: ThemeModeSetting, diGiorno: boolean): Palette {
  const p = PALETTES[tema];
  if (modo !== 'auto' || !p.gemello) return p;
  const voglioScuro = !diGiorno;
  return p.scuro === voglioScuro ? p : PALETTES[p.gemello];
}

/**
 * Migrazione dai valori salvati con l'iterazione 1 (`tema-modo`).
 * cocktail-giorno non ha un equivalente: va su Notturno e la migrazione lo scrive nel log.
 */
export const LEGACY_THEME: Record<string, ThemeId> = {
  'bluetech-notte': 'bluetech',
  'bluetech-giorno': 'giorno',
  'trattoria-notte': 'brace',
  'trattoria-giorno': 'forno',
  'fine-notte': 'riserva',
  'fine-giorno': 'sale',
  'cocktail-notte': 'notturno',
  'cocktail-giorno': 'notturno',
};

/** Derivati del vetro. Vedi SPEC §6. */
export const glassAlpha = (glass: number) => clamp(glass, 0, 100) * 0.0012;
export const glassBlurPx = (glass: number) => clamp(glass, 0, 100) * 0.28;

function clamp(v: number, lo: number, hi: number) {
  return Math.min(hi, Math.max(lo, v));
}

/** Rapporto di contrasto WCAG 2.1 fra due #RRGGBB. Lo usa il back office prima di salvare. */
export function contrastRatio(a: string, b: string): number {
  const lum = (hex: string) => {
    const c = hex.replace('#', '');
    const ch = [0, 2, 4].map((i) => parseInt(c.slice(i, i + 2), 16) / 255);
    const [r, g, bl] = ch.map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [x, y] = [lum(a), lum(b)].sort((m, n) => n - m);
  return (x + 0.05) / (y + 0.05);
}

/**
 * Le custom properties da mettere sul nodo radice.
 * Non applicare i token in altro modo: tutto il resto del CSS legge var().
 */
export function paletteToCssVars(p: Palette): Record<string, string> {
  return {
    '--c-bg': p.bg,
    '--c-surface': p.surface,
    '--c-surface-solid': p.surfaceSolid,
    '--c-line': p.line,
    '--c-text': p.text,
    '--c-muted': p.muted,
    '--c-faint': p.faint,
    '--c-primary': p.primary,
    '--c-on-primary': p.onPrimary,
    '--c-accent': p.accent,
    '--c-secondary': p.secondary,
    '--font-display': p.fontDisplay,
    '--font-text': p.fontText,
    '--radius': `${clamp(p.radius, 0, 32)}px`,
    '--glass-alpha': String(glassAlpha(p.glass)),
    '--glass-blur': `${glassBlurPx(p.glass)}px`,
    '--total-fill': p.totalFill,
    '--total-border': p.totalBorder,
    '--total-text': p.totalText,
    '--glow': p.glow,
    '--glow-2': p.glow2,
    // sui temi chiari lo scrim è bianco: stesso principio, segno invertito (SPEC §15)
    '--scrim-rgb': p.scuro ? '8, 12, 18' : '244, 246, 250',
  };
}
