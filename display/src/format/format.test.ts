import { describe, expect, it } from 'vitest';
import { resolveFormat } from './format';

describe('resolveFormat', () => {
  it.each([
    [1920, 1080, 'wide', '1920x1080'],
    [1366, 768, 'wide', '1366x768'],
    [1280, 800, 'wide', '1280x800'],
    [1024, 768, 'narrow', '1024x768'],
    [1080, 1920, 'portrait', '1080x1920'],
    // stessa larghezza di 1280×800, layout diverso: decide il rapporto d'aspetto
    [1280, 1024, 'narrow', '1024x768'],
    // 1920×1080 con scala di Windows al 125%
    [1536, 864, 'wide', '1366x768'],
    [2560, 1440, 'wide', '1920x1080'],
    [1920, 1200, 'wide', '1920x1080'],
    [768, 1024, 'portrait', '1080x1920'],
  ] as const)('%i×%i → %s, %s', (width, height, layout, format) => {
    expect(resolveFormat(width, height)).toEqual({ layout, format });
  });

  it('dimensioni nulle (finestra non ancora misurata) non rompono niente', () => {
    expect(resolveFormat(0, 0).layout).toBe('wide');
  });
});
