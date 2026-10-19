// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PALETTES, paletteToCssVars } from '../tokens/themes';
import { render, resizeTo } from '../test/render';
import { presetConfig, type ThemeConfig } from './theme';
import { ThemeProvider } from './ThemeProvider';
import { useTheme } from './themeContext';
import type { DisplayPhase } from '../types/protocol';

const root = document.documentElement;
const cssVar = (name: string) => root.style.getPropertyValue(name);

function Probe() {
  const theme = useTheme();
  return <output>{`${theme.key} ${theme.qrLayout}`}</output>;
}

describe('ThemeProvider', () => {
  let cleanup: (() => void) | undefined;
  beforeEach(() => resizeTo(1920, 1080));
  afterEach(() => {
    cleanup?.();
    vi.restoreAllMocks();
  });

  const mount = (config: ThemeConfig, now?: () => Date, phase?: DisplayPhase) => {
    const view = render(
      <ThemeProvider config={config} now={now} phase={phase}>
        <Probe />
      </ThemeProvider>,
    );
    cleanup = view.unmount;
    return view;
  };

  it('applica tutte le variabili della palette e gli attributi letti da tokens.css', () => {
    mount(presetConfig('fine', 'giorno', { motion: 'light', compactRows: true }));
    for (const [name, value] of Object.entries(paletteToCssVars(PALETTES['fine-giorno']))) {
      expect(cssVar(name)).toBe(value);
    }
    expect(root.dataset).toMatchObject({
      theme: 'fine-giorno',
      mode: 'giorno',
      motion: 'light',
      format: '1920x1080',
      layout: 'wide',
      compactRows: 'true',
    });
    expect(root.dataset.qr).toBeUndefined();
  });

  it('data-qr solo durante il pagamento: fuori dal pagamento lo scontrino resta largo', () => {
    const config = presetConfig('bluetech', 'notte', { qrLayout: 'beside' });
    const view = mount(config, undefined, 'sale');
    expect(root.dataset.qr).toBeUndefined();
    view.rerender(
      <ThemeProvider config={config} phase="payment">
        <Probe />
      </ThemeProvider>,
    );
    expect(root.dataset.qr).toBe('beside');
    view.rerender(
      <ThemeProvider config={config} phase="thanks">
        <Probe />
      </ThemeProvider>,
    );
    expect(root.dataset.qr).toBeUndefined();
  });

  it('cambiare tema e modo cambia le variabili, non il formato', () => {
    const view = mount(presetConfig('bluetech', 'notte'));
    const before = { format: root.dataset.format, layout: root.dataset.layout };
    view.rerender(
      <ThemeProvider config={presetConfig('cocktail', 'giorno')}>
        <Probe />
      </ThemeProvider>,
    );
    expect(cssVar('--c-bg')).toBe(PALETTES['cocktail-giorno'].bg);
    expect({ format: root.dataset.format, layout: root.dataset.layout }).toEqual(before);
  });

  it("in auto sceglie giorno o notte dall'orologio", () => {
    mount(presetConfig('trattoria', 'auto'), () => new Date(2026, 9, 19, 12, 0));
    expect(root.dataset.theme).toBe('trattoria-giorno');
    expect(cssVar('--c-bg')).toBe(PALETTES['trattoria-giorno'].bg);
  });

  it('su narrow il QR è sempre a tutto schermo, anche se il tema dice beside', () => {
    resizeTo(1024, 768);
    const view = mount(
      presetConfig('bluetech', 'notte', { qrLayout: 'beside' }),
      undefined,
      'payment',
    );
    expect(root.dataset.qr).toBe('fullscreen');
    expect(view.container.textContent).toBe('bluetech-notte fullscreen');
  });

  it('un tema illeggibile ricade su bluetech-notte e lo scrive nel log', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const config = presetConfig('bluetech', 'giorno');
    config.tokens.cOnPrimary = '#2B6BD0';
    mount(config);
    expect(root.dataset.theme).toBe('bluetech-notte');
    expect(cssVar('--c-primary')).toBe(PALETTES['bluetech-notte'].primary);
    expect(warn).toHaveBeenCalledWith(expect.stringMatching(/testo sul primario/));
  });
});
