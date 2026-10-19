import { describe, expect, it } from 'vitest';
import { PALETTES } from '../tokens/themes';
import { FALLBACK_KEY, modeAt, presetConfig, resolveTheme } from './theme';

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
    expect(theme.palette.totalFill).toBe(PALETTES['bluetech-notte'].totalFill);
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
