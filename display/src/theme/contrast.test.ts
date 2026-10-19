import { describe, expect, it } from 'vitest';
import { PALETTES, type PaletteKey } from '../tokens/themes';
import { contrastRatio } from './contrast';
import { MIN_CONTRAST } from './theme';

describe('contrastRatio', () => {
  it('nero su bianco è 21:1, colori uguali 1:1', () => {
    expect(contrastRatio('#000000', '#FFFFFF')).toBeCloseTo(21, 5);
    expect(contrastRatio('#fff', '#ffffff')).toBeCloseTo(1, 5);
  });

  it('non verifica i colori non opachi o non esadecimali', () => {
    expect(contrastRatio('rgba(255,255,255,0.5)', '#000000')).toBeNull();
    expect(contrastRatio('transparent', '#000000')).toBeNull();
  });
});

describe('le otto palette del pacchetto', () => {
  const keys = Object.keys(PALETTES) as PaletteKey[];

  it('sono otto', () => {
    expect(keys).toHaveLength(8);
  });

  it.each(keys)('%s: testo su fondo e testo sul primario ≥ 4,5:1', (key) => {
    const p = PALETTES[key];
    expect(contrastRatio(p.text, p.bg)).toBeGreaterThanOrEqual(MIN_CONTRAST);
    expect(contrastRatio(p.onPrimary, p.primary)).toBeGreaterThanOrEqual(MIN_CONTRAST);
  });
});
