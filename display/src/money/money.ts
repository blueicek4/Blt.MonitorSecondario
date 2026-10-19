import type { Cents } from '../types/protocol';

/**
 * Gli importi sono interi in centesimi (CLAUDE.md): qui si formattano, solo in uscita.
 *
 * SPEC §4 chiede `€ 18,90` (simbolo prima, spazio fine) ma indica Intl.NumberFormat it-IT con
 * style 'currency', che produce `18,90 €`. Vale l'esempio: Intl formatta solo il numero, il
 * simbolo lo mettiamo davanti con uno spazio fine non separabile (U+202F).
 */
const number = new Intl.NumberFormat('it-IT', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  useGrouping: true,
});

/** Meno tipografico U+2212, non il trattino (SPEC §3). */
export const MINUS = '−';
const THIN_NBSP = ' ';

export function assertCents(value: Cents): void {
  if (!Number.isSafeInteger(value)) {
    throw new RangeError(`Importo non intero in centesimi: ${value}`);
  }
}

/** `1890` → `18,90`; `-210` → `−2,10`. Con `sign: 'always'` i positivi hanno il `+`. */
export function formatAmount(value: Cents, options: { sign?: 'negative' | 'always' } = {}): string {
  assertCents(value);
  const digits = number.format(Math.abs(value) / 100);
  if (value < 0) return MINUS + digits;
  if (value > 0 && options.sign === 'always') return '+' + digits;
  return digits;
}

/** `1890` → `€ 18,90` con spazio fine. */
export function formatEuro(value: Cents): string {
  assertCents(value);
  const digits = number.format(Math.abs(value) / 100);
  return (value < 0 ? MINUS : '') + '€' + THIN_NBSP + digits;
}
