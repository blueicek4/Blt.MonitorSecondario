import { describe, expect, it } from 'vitest';
import { PALETTES, type PaletteKey } from '../tokens/themes';
import type { ThemeId, ThemeMode } from '../types/protocol';
import { contrastRatio } from './contrast';
import {
  FALLBACK_KEY,
  MIN_CONTRAST,
  contrastProblems,
  modeAt,
  presetConfig,
  resolveTheme,
} from './theme';

const at = (hour: number, minute = 0) => new Date(2026, 9, 19, hour, minute);

describe('modeAt', () => {
  it('di giorno dalle 7 alle 19, altrimenti notte', () => {
    expect(modeAt(at(6, 59))).toBe('notte');
    expect(modeAt(at(7))).toBe('giorno');
    expect(modeAt(at(18, 59))).toBe('giorno');
    expect(modeAt(at(19))).toBe('notte');
  });

  it('rispetta orari diversi', () => {
    expect(modeAt(at(20), { start: 10, end: 23 })).toBe('giorno');
  });
});

describe('resolveTheme', () => {
  it('in auto usa le due varianti del preset', () => {
    const config = presetConfig('trattoria', 'auto');
    expect(resolveTheme(config, 'giorno')).toMatchObject({
      key: 'trattoria-giorno',
      fallback: false,
    });
    expect(resolveTheme(config, 'notte').palette).toEqual(PALETTES['trattoria-notte']);
  });

  it('con modo fisso i colori del locale vincono sul preset', () => {
    const config = presetConfig('bluetech', 'notte');
    config.tokens.cPrimary = '#0B5D3B';
    const theme = resolveTheme(config, 'notte');
    expect(theme.key).toBe('custom');
    expect(theme.palette.primary).toBe('#0B5D3B');
    // Il blocco totale del preset è una sfumatura del primario: segue il primario del locale.
    expect(theme.palette.totalFill).toBe(
      'linear-gradient(180deg, rgba(11,93,59,0.20), rgba(11,93,59,0.05))',
    );
    expect(theme.palette.totalBorder).toBe('rgba(11,93,59,0.42)');
    expect(theme.palette.surfaceSolid).toBe(PALETTES['bluetech-notte'].surfaceSolid);
  });

  it('colori chiari del locale su un preset scuro: anche il totale diventa scuro', () => {
    const config = presetConfig('bluetech', 'notte');
    Object.assign(config.tokens, {
      cBg: '#FAFAFA',
      cSurface: '#FFFFFF',
      cText: '#111111',
      cMuted: '#555555',
    });
    const theme = resolveTheme(config, 'notte');
    expect(theme.fallback).toBe(false);
    expect(theme.palette.totalText).toBe('#111111');
    expect(theme.palette.surfaceSolid).toBe('#FFFFFF');
    expect(theme.palette.line).toBe('rgba(17,17,17,0.09)');
  });

  it('testo chiaro su una card rimasta chiara: il tema non passa', () => {
    const config = presetConfig('fine', 'giorno');
    Object.assign(config.tokens, { cBg: '#1B1B1B', cText: '#F0F0F0' });
    const theme = resolveTheme(config, 'giorno');
    expect(theme.fallback).toBe(true);
    expect(theme.problems.join()).toMatch(/testo sulla card/);
  });

  it('nessun tema applicato ha il totale illeggibile', () => {
    const cases: Array<[ThemeId, ThemeMode, Record<string, string>]> = [
      [
        'fine',
        'giorno',
        { cBg: '#1B1B1B', cSurface: '#262626', cText: '#F0F0F0', cMuted: '#BBBBBB' },
      ],
      ['bluetech', 'notte', { cBg: '#FAFAFA', cSurface: '#FFFFFF', cText: '#111111' }],
      ['cocktail', 'giorno', { cPrimary: '#FFD400', cOnPrimary: '#111111' }],
    ];
    for (const [preset, mode, colors] of cases) {
      const config = presetConfig(preset, mode);
      Object.assign(config.tokens, colors);
      const { palette, fallback } = resolveTheme(config, mode);
      if (fallback) continue;
      const behind = palette.totalFill.startsWith('#') ? palette.totalFill : palette.surfaceSolid;
      expect(contrastRatio(palette.totalText, behind)).toBeGreaterThanOrEqual(MIN_CONTRAST);
    }
  });

  it('gli otto preset passano tutti i controlli e restano riconoscibili', () => {
    for (const key of Object.keys(PALETTES) as PaletteKey[]) {
      expect(contrastProblems(PALETTES[key]), key).toEqual([]);
      const [preset, mode] = key.split('-') as [ThemeId, ThemeMode];
      expect(resolveTheme(presetConfig(preset, mode), mode).key).toBe(key);
    }
  });

  it('in auto tiene font e raggio del locale', () => {
    const config = presetConfig('bluetech', 'auto');
    config.tokens.radius = 0;
    config.tokens.fontDisplay = "'Syne', sans-serif";
    const theme = resolveTheme(config, 'giorno');
    expect(theme.palette.radius).toBe(0);
    expect(theme.palette.fontDisplay).toBe("'Syne', sans-serif");
    expect(theme.palette.bg).toBe(PALETTES['bluetech-giorno'].bg);
  });

  it('un tema illeggibile ricade su bluetech-notte e dice perché', () => {
    const config = presetConfig('bluetech', 'giorno');
    config.tokens.cText = '#C9CED6';
    const theme = resolveTheme(config, 'giorno');
    expect(theme).toMatchObject({ key: FALLBACK_KEY, mode: 'notte', fallback: true });
    expect(theme.palette).toEqual(PALETTES[FALLBACK_KEY]);
    expect(theme.problems[0]).toMatch(/testo su fondo/);
  });

  it('colori non verificabili sono trattati come illeggibili', () => {
    const config = presetConfig('cocktail', 'notte');
    config.tokens.cBg = 'rgba(0,0,0,0.5)';
    expect(resolveTheme(config, 'notte').fallback).toBe(true);
  });
});
