import { describe, expect, it } from 'vitest';
import { composite, mix, parseColor, recolor, type Rgb } from './color';

describe('color', () => {
  it('legge #rrggbb, rgb() e rgba()', () => {
    expect(parseColor('#1F6FEB')).toEqual({ rgb: [31, 111, 235], alpha: 1 });
    expect(parseColor('rgba(255, 255, 255, 0.055)')).toEqual({
      rgb: [255, 255, 255],
      alpha: 0.055,
    });
    expect(parseColor('rgb(1,2,3)')).toEqual({ rgb: [1, 2, 3], alpha: 1 });
    expect(parseColor('rgba(300,0,0,1)')).toBeNull();
    expect(parseColor('transparent')).toBeNull();
  });

  it('stende un colore trasparente su un fondo opaco', () => {
    expect(composite('rgba(255,255,255,0.5)', '#000000')).toBe('#808080');
    expect(composite('#123456', '#FFFFFF')).toBe('#123456');
    expect(mix('#000000', '#FFFFFF', 0.25)).toBe('#404040');
  });

  it('ricolora gradienti e rgba in un solo passaggio, senza catene', () => {
    const map = new Map<string, Rgb>([
      ['31,111,235', [11, 93, 59]],
      ['11,93,59', [255, 0, 0]],
    ]);
    expect(recolor('linear-gradient(180deg, rgba(31,111,235,0.20), #1F6FEB)', map)).toBe(
      'linear-gradient(180deg, rgba(11,93,59,0.20), #0B5D3B)',
    );
    expect(recolor('transparent', map)).toBe('transparent');
    expect(recolor('#1F6FEBCC', map)).toBe('#1F6FEBCC');
  });
});
