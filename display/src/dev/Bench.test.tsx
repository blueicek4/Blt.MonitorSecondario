// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { render, resizeTo } from '../test/render';
import { presetConfig } from '../theme/theme';
import { createMockTransport } from '../transport/mock';
import { Bench } from './Bench';

describe('Bench', () => {
  let cleanup: (() => void) | undefined;
  afterEach(() => cleanup?.());

  it("a connessione persa l'anteprima resta ferma e si allinea al ritorno", () => {
    resizeTo(1920, 1080);
    const cashier = createMockTransport();
    let config = presetConfig('bluetech', 'notte');
    const view = render(
      <Bench config={config} onConfigChange={(next) => (config = next)} cashier={cashier} />,
    );
    cleanup = view.unmount;
    const click = (name: string) =>
      act(() => {
        const button = [...view.container.querySelectorAll('button')].find(
          (b) => b.textContent === name,
        );
        if (!button) throw new Error(`manca il pulsante ${name}`);
        button.click();
      });
    const rows = () => view.container.querySelectorAll('.bench-row').length;

    click('Aggiungi riga');
    expect(rows()).toBe(1);
    click('Connessione persa');
    click('Aggiungi riga');
    click('Aggiungi riga');
    view.rerender(
      <Bench
        config={presetConfig('trattoria', 'notte')}
        onConfigChange={() => {}}
        cashier={cashier}
      />,
    );
    expect(rows()).toBe(1);
    click('Ripristina connessione');
    expect(rows()).toBe(3);
  });

  it('data-qr compare sul nodo radice solo col QR a video', () => {
    resizeTo(1920, 1080);
    const cashier = createMockTransport();
    const view = render(
      <Bench
        config={presetConfig('bluetech', 'notte')}
        onConfigChange={() => {}}
        cashier={cashier}
      />,
    );
    cleanup = view.unmount;
    act(() => cashier.addLine());
    expect(document.documentElement.dataset.qr).toBeUndefined();
    act(() => cashier.showQr());
    expect(document.documentElement.dataset.qr).toBe('beside');
    act(() => cashier.clearQr());
    expect(document.documentElement.dataset.qr).toBeUndefined();
  });
});
