import type { FormatKey, LayoutVariant } from '../types/protocol';

/**
 * Variante di layout dal RAPPORTO D'ASPETTO, non dalla larghezza (SPEC §9):
 * 1280×800 e 1280×1024 hanno la stessa larghezza e due layout diversi.
 *
 *   wide      ≥ 1,5
 *   narrow    1 – 1,5   (la SPEC dice 1,2–1,5: anche 1–1,2, rarissimo, va su narrow, la più vicina)
 *   portrait  < 1
 */
export function layoutFor(width: number, height: number): LayoutVariant {
  if (width <= 0 || height <= 0) return 'wide';
  const ratio = width / height;
  if (ratio >= 1.5) return 'wide';
  if (ratio >= 1) return 'narrow';
  return 'portrait';
}

/**
 * Chiave della tabella tipografica (tokens.css). Dimensioni in px CSS del viewport, così uno
 * schermo 1920×1080 con scala di Windows al 125% (1536×864) usa la tabella del 1366×768.
 */
export function formatFor(width: number, height: number): FormatKey {
  const layout = layoutFor(width, height);
  if (layout === 'portrait') return '1080x1920';
  if (layout === 'narrow') return '1024x768';
  if (height >= 1000) return '1920x1080';
  // Sotto i 1000 px di altezza: 16:10 (1,6) contro 16:9 (1,78).
  return width / height < 1.7 ? '1280x800' : '1366x768';
}

export interface ScreenFormat {
  layout: LayoutVariant;
  format: FormatKey;
}

export function resolveFormat(width: number, height: number): ScreenFormat {
  return { layout: layoutFor(width, height), format: formatFor(width, height) };
}
