/**
 * Bluetech Display — le otto palette.
 *
 * Quattro temi × due varianti (notte, giorno). Gli stessi nomi di token in
 * tutti e otto i set. Le varianti giorno NON sono derivate schiarendo le
 * notte: sono progettate separatamente, perché i primari vanno scuriti per
 * tenere il bianco sopra sopra 4,5:1.
 *
 * Tutti i rapporti di contrasto nei commenti sono calcolati (WCAG 2.1
 * relative luminance), non stimati. Nessuna coppia scende sotto 4,5:1.
 */

import type { ThemeId, ThemeMode } from '../types/protocol';

export interface Palette {
  /** fondo dello schermo */
  bg: string;
  /** card dello scontrino: può essere rgba su bg quando glass > 0 */
  surface: string;
  /** versione opaca di surface, per i fondi che non possono essere trasparenti */
  surfaceSolid: string;
  /** bordi e separatori di riga */
  line: string;
  text: string;
  /** etichette secondarie, quantità */
  muted: string;
  /** righe stornate, didascalie minori */
  faint: string;
  primary: string;
  onPrimary: string;
  /** sconti e maggiorazioni */
  accent: string;
  secondary: string;

  fontDisplay: string;
  fontText: string;
  /** px */
  radius: number;
  /** 0–100 */
  glass: number;

  /** fondo del blocco totale: colore, gradiente o 'transparent' */
  totalFill: string;
  totalBorder: string;
  totalText: string;

  /** tinte dei blob del mesh di fondo */
  glow: string;
  glow2: string;

  footerVariant: 'light' | 'dark';
}

export type PaletteKey = `${ThemeId}-${ThemeMode}`;

const ARCHIVO = "'Archivo', 'Arial Narrow', sans-serif";
const PLEX = "'IBM Plex Sans', 'Segoe UI', sans-serif";
const NEWSREADER = "'Newsreader', Georgia, serif";
const ALEGREYA = "'Alegreya Sans', 'Segoe UI', sans-serif";
const CORMORANT = "'Cormorant Garamond', Georgia, serif";
const JOST = "'Jost', 'Segoe UI', sans-serif";
const SYNE = "'Syne', sans-serif";
const GROTESK = "'Space Grotesk', 'Segoe UI', sans-serif";

export const PALETTES: Record<PaletteKey, Palette> = {
  // -------------------------------------------------------------------------
  // BLUETECH — neutro, moderno, tecnologico. Il default.
  // -------------------------------------------------------------------------
  'bluetech-notte': {
    bg: '#0B1017',
    surface: 'rgba(255,255,255,0.055)',
    surfaceSolid: '#141B26',
    line: 'rgba(255,255,255,0.09)',
    text: '#E6EAF2', // 15,8:1 su bg
    muted: '#9AA6BA', // 7,7:1 su bg
    faint: '#6F7E95',
    primary: '#1F6FEB',
    onPrimary: '#FFFFFF', // 4,6:1 su primary
    accent: '#3DDC97', // 10,6:1 su bg
    secondary: '#8FA3C0',
    fontDisplay: ARCHIVO,
    fontText: PLEX,
    radius: 24,
    glass: 50,
    totalFill: 'linear-gradient(180deg, rgba(31,111,235,0.20), rgba(31,111,235,0.05))',
    totalBorder: 'rgba(31,111,235,0.42)',
    totalText: '#E6EAF2',
    glow: 'rgba(31,111,235,0.30)',
    glow2: 'rgba(61,220,151,0.14)',
    footerVariant: 'dark',
  },
  'bluetech-giorno': {
    bg: '#F4F6FA',
    surface: '#FFFFFF',
    surfaceSolid: '#FFFFFF',
    line: 'rgba(16,21,29,0.10)',
    text: '#10151D', // 15,3:1 su bg
    muted: '#5A6679', // 5,8:1 su surface
    faint: '#7C8798',
    primary: '#1757C2',
    onPrimary: '#FFFFFF', // 6,6:1 su primary
    accent: '#0E7A52', // 5,4:1 su surface
    secondary: '#7C8798',
    fontDisplay: ARCHIVO,
    fontText: PLEX,
    radius: 24,
    glass: 0, // di giorno niente vetro: ombra 0 2px 24px rgba(16,21,29,.06)
    totalFill: '#1757C2',
    totalBorder: '#1757C2',
    totalText: '#FFFFFF',
    glow: 'rgba(23,87,194,0.14)',
    glow2: 'rgba(14,122,82,0.10)',
    footerVariant: 'light',
  },

  // -------------------------------------------------------------------------
  // TRATTORIA — caldo, carta e terracotta, serif elegante.
  // -------------------------------------------------------------------------
  'trattoria-notte': {
    bg: '#1C1512',
    surface: 'rgba(242,231,217,0.06)',
    surfaceSolid: '#261D18',
    line: 'rgba(242,231,217,0.13)',
    text: '#F2E7D9', // 14,8:1 su bg
    muted: '#B8A694', // 7,6:1 su bg
    faint: '#8E7D6A',
    primary: '#D4714A', // 5,3:1 su bg
    onPrimary: '#1C1512', // testo scuro sopra: 5,3:1
    accent: '#7FC4A3', // 8,9:1 su bg
    secondary: '#8E7D6A',
    fontDisplay: NEWSREADER,
    fontText: ALEGREYA,
    radius: 14,
    glass: 30,
    totalFill: '#D4714A',
    totalBorder: '#D4714A',
    totalText: '#1C1512',
    glow: 'rgba(212,113,74,0.22)',
    glow2: 'rgba(127,196,163,0.10)',
    footerVariant: 'dark',
  },
  'trattoria-giorno': {
    bg: '#F6EEE2',
    surface: '#FFFBF4',
    surfaceSolid: '#FFFBF4',
    line: 'rgba(43,33,26,0.12)',
    text: '#2B211A', // 13,7:1 su bg
    muted: '#6B5B49', // 6,3:1 su surface
    faint: '#8D7F6E',
    primary: '#9C3A1F',
    onPrimary: '#FFFFFF', // 6,9:1 su primary
    accent: '#1E6B4F', // 6,1:1 su surface
    secondary: '#7A6A58',
    fontDisplay: NEWSREADER,
    fontText: ALEGREYA,
    radius: 14,
    glass: 0,
    totalFill: '#9C3A1F',
    totalBorder: '#9C3A1F',
    totalText: '#FFFFFF',
    glow: 'rgba(201,138,94,0.30)',
    glow2: 'rgba(30,107,79,0.08)',
    footerVariant: 'light',
  },

  // -------------------------------------------------------------------------
  // FINE DINING — oro, minimale, molto respiro, angoli vivi.
  // -------------------------------------------------------------------------
  'fine-notte': {
    bg: '#0C0B09',
    surface: 'rgba(237,230,215,0.05)',
    surfaceSolid: '#16140F',
    line: 'rgba(237,230,215,0.13)',
    text: '#EDE6D7', // 15,8:1 su bg
    muted: '#A39781', // 6,9:1 su bg
    faint: '#7A7061',
    primary: '#C9A227', // 8,1:1 su bg
    onPrimary: '#141210', // 7,7:1 su primary
    accent: '#8FB39B', // 8,4:1 su bg
    secondary: '#8A8070',
    fontDisplay: CORMORANT,
    fontText: JOST,
    radius: 4, // angoli quasi vivi: è il tratto distintivo del tema
    glass: 0, // niente vetro, niente gradienti
    totalFill: 'transparent', // solo filetto oro
    totalBorder: '#C9A227',
    totalText: '#EDE6D7',
    glow: 'rgba(201,162,39,0.18)',
    glow2: 'rgba(143,179,155,0.08)',
    footerVariant: 'dark',
  },
  'fine-giorno': {
    bg: '#F7F4EC',
    surface: '#FFFDF7',
    surfaceSolid: '#FFFDF7',
    line: 'rgba(26,23,18,0.12)',
    text: '#1A1712', // 15,1:1 su bg
    muted: '#6E6454', // 5,8:1 su surface
    faint: '#8A8070',
    primary: '#8A6D14', // oro scurito per tenere il bianco sopra
    onPrimary: '#FFFFFF', // 4,8:1 su primary
    accent: '#3F6B55',
    secondary: '#8A8070',
    fontDisplay: CORMORANT,
    fontText: JOST,
    radius: 4,
    glass: 0,
    totalFill: 'transparent',
    totalBorder: '#8A6D14',
    totalText: '#1A1712',
    glow: 'rgba(138,109,20,0.14)',
    glow2: 'rgba(63,107,85,0.08)',
    footerVariant: 'light',
  },

  // -------------------------------------------------------------------------
  // COCKTAIL BAR — neon, gradienti vivi. Il più aggressivo dei quattro.
  // -------------------------------------------------------------------------
  'cocktail-notte': {
    bg: '#0A0616',
    surface: 'rgba(240,234,255,0.06)',
    surfaceSolid: '#16102B',
    line: 'rgba(240,234,255,0.14)',
    text: '#F0EAFF', // 17,1:1 su bg
    muted: '#A99BC8', // 7,8:1 su bg
    faint: '#7E6FA0',
    primary: '#C06CFF', // 6,5:1 su bg
    onPrimary: '#0A0616',
    accent: '#35F5C8', // 14,3:1 su bg
    secondary: '#FF4D9D', // 6,2:1 su bg — solo per gradienti, non per testo piccolo
    fontDisplay: SYNE,
    fontText: GROTESK,
    radius: 20,
    glass: 70,
    totalFill: 'linear-gradient(180deg, rgba(192,108,255,0.26), rgba(53,245,200,0.08))',
    totalBorder: 'rgba(192,108,255,0.55)',
    totalText: '#F0EAFF',
    glow: 'rgba(192,108,255,0.30)',
    glow2: 'rgba(53,245,200,0.14)',
    footerVariant: 'dark',
  },
  'cocktail-giorno': {
    bg: '#F6F2FD',
    surface: '#FFFFFF',
    surfaceSolid: '#FFFFFF',
    line: 'rgba(23,15,35,0.10)',
    text: '#170F23', // 16,4:1 su bg
    muted: '#5C5270', // 7,1:1 su surface
    faint: '#7A6E92',
    primary: '#7A2BC4',
    onPrimary: '#FFFFFF', // 7,1:1 su primary
    accent: '#0C7A6B', // 5,2:1 su surface
    secondary: '#B0246B',
    fontDisplay: SYNE,
    fontText: GROTESK,
    radius: 20,
    glass: 0,
    totalFill: '#7A2BC4',
    totalBorder: '#7A2BC4',
    totalText: '#FFFFFF',
    glow: 'rgba(122,43,196,0.14)', // di giorno gli alpha dei gradienti stanno ≤ 0,14
    glow2: 'rgba(12,122,107,0.08)',
    footerVariant: 'light',
  },
};

export const paletteKey = (theme: ThemeId, mode: ThemeMode): PaletteKey =>
  `${theme}-${mode}`;

export const getPalette = (theme: ThemeId, mode: ThemeMode): Palette =>
  PALETTES[paletteKey(theme, mode)];

/** Derivati del vetro. Vedi SPEC §6. */
export const glassAlpha = (glass: number) => clamp(glass, 0, 100) * 0.0012;
export const glassBlurPx = (glass: number) => clamp(glass, 0, 100) * 0.28;

function clamp(v: number, lo: number, hi: number) {
  return Math.min(hi, Math.max(lo, v));
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
  };
}
