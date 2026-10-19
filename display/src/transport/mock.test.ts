import { describe, expect, it } from 'vitest';
import type { CashIn, LinkState } from '../types/protocol';
import { createMockTransport } from './mock';

function connect() {
  const cashier = createMockTransport();
  const messages: CashIn[] = [];
  const links: LinkState[] = [];
  const disconnect = cashier.connect({
    onMessage: (message) => messages.push(message),
    onLink: (state) => links.push(state),
  });
  const totals = () => messages.flatMap((m) => (m.t === 'sale.total' ? [m.total] : []));
  return { cashier, messages, links, disconnect, totals };
}

describe('cassa finta', () => {
  it('apre la vendita alla prima riga e manda il totale dopo ogni riga', () => {
    const { cashier, messages, totals } = connect();
    cashier.addLine();
    cashier.addLine();
    expect(messages.map((m) => m.t)).toEqual([
      'sale.open',
      'sale.line',
      'sale.total',
      'sale.line',
      'sale.total',
    ]);
    expect(totals()).toEqual([400, 1250]);
  });

  it('importi interi in centesimi e id stabili e unici', () => {
    const { cashier, messages } = connect();
    for (let i = 0; i < 7; i++) cashier.addLine();
    cashier.addDiscount();
    cashier.voidLast();
    cashier.addNote();
    const lines = messages.flatMap((m) => (m.t === 'sale.line' ? [m.line] : []));
    const ids = lines.map((line) => line.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const line of lines)
      if (line.kind !== 'note') expect(Number.isInteger(line.amount)).toBe(true);
    for (const m of messages)
      if (m.t === 'sale.total') expect(Number.isInteger(m.total)).toBe(true);
  });

  it('lo storno punta alla riga annullata e la toglie dal totale', () => {
    const { cashier, messages, totals } = connect();
    cashier.addLine(); // Coperto ×2, 4,00
    cashier.addLine(); // Pizza Margherita, 8,50
    cashier.voidLast();
    const lines = messages.flatMap((m) => (m.t === 'sale.line' ? [m.line] : []));
    const pizza = lines[1];
    expect(lines[2]).toMatchObject({ kind: 'void', desc: 'Pizza Margherita', voidsId: pizza?.id });
    expect(totals().at(-1)).toBe(400);
  });

  it('sconto del 10% arrotondato al centesimo, coperti contati dalla riga Coperto', () => {
    const { cashier, messages } = connect();
    cashier.addLine(); // 4,00
    cashier.addLine(); // 8,50
    cashier.addLine(); // 2,50 → 15,00
    cashier.addDiscount();
    const last = messages.at(-1);
    expect(last).toEqual({ t: 'sale.total', total: 1350, cover: 2 });
  });

  it("QR con l'importo, poi pay.clear; fine vendita riparte da zero", () => {
    const { cashier, messages } = connect();
    cashier.showQr(); // nessuna vendita: niente QR
    cashier.addLine();
    cashier.showQr();
    const qr = messages.at(-1);
    expect(qr).toMatchObject({ t: 'pay.qr', provider: 'satispay', amount: 400 });
    expect(qr?.t === 'pay.qr' && qr.png.startsWith('data:image/svg+xml')).toBe(true);
    cashier.clearQr();
    cashier.endSale();
    expect(messages.slice(-2).map((m) => m.t)).toEqual(['pay.clear', 'sale.close']);
    cashier.addLine();
    expect(messages.slice(-3).map((m) => m.t)).toEqual(['sale.open', 'sale.line', 'sale.total']);
  });

  it('il primo messaggio porta il totale formattato', () => {
    const { cashier, messages } = connect();
    cashier.addLine();
    cashier.sendMessage();
    cashier.sendMessage();
    expect(messages.slice(-2)).toEqual([
      { t: 'display.msg', text: 'Totale € 4,00' },
      { t: 'display.msg', text: 'Buongiorno' },
    ]);
  });

  it('offline: niente messaggi, alla ripresa sync con lo stato completo', () => {
    const { cashier, messages, links } = connect();
    cashier.addLine();
    cashier.setOffline(true);
    const before = messages.length;
    cashier.addLine();
    expect(messages).toHaveLength(before);
    cashier.setOffline(false);
    cashier.requestSync();
    expect(links).toEqual(['online', 'lost', 'online']);
    const sync = messages.at(-1);
    expect(sync?.t).toBe('sync');
    if (sync?.t === 'sync') {
      expect(sync.state).toMatchObject({ phase: 'sale', total: 1250, cover: 2 });
      expect(sync.state.lines).toHaveLength(2);
    }
  });

  it('dopo il teardown non arriva più niente', () => {
    const { cashier, messages, disconnect } = connect();
    disconnect();
    cashier.addLine();
    expect(messages).toHaveLength(0);
  });
});
