import { describe, expect, it } from 'vitest';
import { formatAmount, formatEuro } from './money';

describe('importi', () => {
  it('formato € 18,90 con spazio fine e virgola', () => {
    expect(formatEuro(1890)).toBe('€ 18,90');
    expect(formatEuro(123450)).toBe('€ 1.234,50');
    expect(formatEuro(0)).toBe('€ 0,00');
  });

  it('meno tipografico U+2212 e segno + a richiesta', () => {
    expect(formatAmount(-210)).toBe('−2,10');
    expect(formatAmount(150, { sign: 'always' })).toBe('+1,50');
    expect(formatAmount(150)).toBe('1,50');
  });

  it('rifiuta gli importi non interi', () => {
    expect(() => formatEuro(18.9)).toThrow(RangeError);
  });
});
