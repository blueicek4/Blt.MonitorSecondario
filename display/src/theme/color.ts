/** Colori #rgb/#rrggbb e rgb()/rgba(): quanto basta per ricavare i colori di un tema da quelli del locale. */

export type Rgb = [number, number, number];

export function parseHex(color: string): Rgb | null {
  const match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(color.trim());
  const hex = match?.[1];
  if (!hex) return null;
  const full = hex.length === 3 ? [...hex].map((c) => c + c).join('') : hex;
  return [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16)) as Rgb;
}

/** #rrggbb, rgb(r,g,b) o rgba(r,g,b,a): colore e trasparenza. */
export function parseColor(color: string): { rgb: Rgb; alpha: number } | null {
  const hex = parseHex(color);
  if (hex) return { rgb: hex, alpha: 1 };
  const match =
    /^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(\d*\.?\d+)\s*)?\)$/i.exec(
      color.trim(),
    );
  if (!match) return null;
  const rgb = [match[1], match[2], match[3]].map(Number) as Rgb;
  const alpha = match[4] === undefined ? 1 : Number(match[4]);
  if (rgb.some((channel) => channel > 255) || alpha > 1) return null;
  return { rgb, alpha };
}

export function toHex(rgb: Rgb): string {
  return `#${rgb.map((c) => Math.round(c).toString(16).padStart(2, '0')).join('')}`.toUpperCase();
}

/** `top` (anche trasparente) steso su `bottom` opaco: il colore che si vede. */
export function composite(top: string, bottom: string): string | null {
  const over = parseColor(top);
  const under = parseHex(bottom);
  if (!over || !under) return null;
  return toHex(over.rgb.map((c, i) => c * over.alpha + under[i]! * (1 - over.alpha)) as Rgb);
}

/** `a` mescolato con `b`: `amount` 0 dà `a`, 1 dà `b`. */
export function mix(a: string, b: string, amount: number): string | null {
  const from = parseHex(a);
  const to = parseHex(b);
  if (!from || !to) return null;
  return toHex(from.map((c, i) => c + (to[i]! - c) * amount) as Rgb);
}

export function alphaOf(color: string): number | null {
  return parseColor(color)?.alpha ?? null;
}

export function withAlpha(color: string, alpha: number): string | null {
  const rgb = parseHex(color);
  return rgb ? `rgba(${rgb.join(',')},${alpha})` : null;
}

/**
 * Sostituisce i colori dentro `value` (anche in un gradiente) in un solo passaggio: #rrggbb diventa il
 * nuovo #rrggbb, rgba(r,g,b,a) prende il nuovo colore e tiene la sua trasparenza. `map` va da
 * "r,g,b" al nuovo colore; i colori che non ci sono restano come sono.
 */
export function recolor(value: string, map: ReadonlyMap<string, Rgb>): string {
  return value.replace(
    /#[0-9a-f]{6}\b|(rgba?\(\s*)(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})/gi,
    (match, prefix?: string, r?: string, g?: string, b?: string) => {
      const rgb = prefix === undefined ? parseHex(match) : ([r, g, b].map(Number) as Rgb);
      const target = rgb && map.get(rgb.join(','));
      if (!target) return match;
      return prefix === undefined ? toHex(target) : `${prefix}${target.join(',')}`;
    },
  );
}
