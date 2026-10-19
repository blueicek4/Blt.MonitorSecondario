import type {
  CashIn,
  Cents,
  DisplayState,
  LinkState,
  PaymentRequest,
  SaleLine,
  Transport,
} from '../types/protocol';
import { formatEuro } from '../money/money';
import { grayPngDataUri } from './png';

/**
 * Cassa finta per sviluppare senza Passepartout (TASKS M1). Riproduce i comandi del prototipo di
 * Claude Design: aggiungi riga, sconto, storno, testo libero, messaggio, QR, fine vendita, offline.
 *
 * È "la cassa": tiene il suo conto e calcola il totale, che manda come `sale.total`. Il monitor non
 * somma mai le righe (CLAUDE.md). Tutti gli importi sono interi in centesimi.
 */
export interface MockCashier extends Transport {
  addLine(): void;
  addDiscount(): void;
  voidLast(): void;
  addNote(): void;
  sendMessage(): void;
  showQr(): void;
  clearQr(): void;
  endSale(): void;
  /** true: la cassa non raggiunge più il monitor (link 'lost'); i comandi continuano a cambiare il conto. */
  setOffline(offline: boolean): void;
  /** Stato attuale della cassa, per i banchi di prova. */
  snapshot(): Partial<DisplayState>;
}

interface CatalogItem {
  desc: string;
  qty: number;
  /** Importo della riga (quantità già compresa), in centesimi. */
  amount: Cents;
}

/** Dal prototipo (design-reference/Prototipo.dc.html). */
const CATALOG: readonly CatalogItem[] = [
  { desc: 'Coperto', qty: 2, amount: 400 },
  { desc: 'Pizza Margherita', qty: 1, amount: 850 },
  { desc: 'Acqua naturale 0,75 l', qty: 1, amount: 250 },
  { desc: 'Tiramisù', qty: 1, amount: 600 },
  { desc: 'Birra artigianale 0,4 l', qty: 2, amount: 1200 },
  { desc: 'Caffè', qty: 2, amount: 240 },
];

const NOTES = ['Ben cotta, poco sale', 'Senza basilico', 'Da dividere in due'];
const MESSAGES = ['Buongiorno', `Resto ${formatEuro(150)}`, 'Grazie, a presto'];

type Listener = Parameters<Transport['connect']>[0];

export function createMockTransport(): MockCashier {
  const listeners = new Set<Listener>();
  let offline = false;

  let lines: SaleLine[] = [];
  let open = false;
  let payment: PaymentRequest | undefined;
  let seq = 0;
  let itemCount = 0;
  let noteIndex = 0;
  let messageIndex = 0;

  const nextId = () => `L${++seq}`;

  const send = (message: CashIn) => {
    if (offline) return;
    for (const listener of listeners) listener.onMessage(message);
  };

  const setLink = (state: LinkState) => {
    for (const listener of listeners) listener.onLink(state);
  };

  const voidedIds = () =>
    new Set(lines.flatMap((line) => (line.kind === 'void' && line.voidsId ? [line.voidsId] : [])));

  const total = (): Cents => {
    const voided = voidedIds();
    return lines.reduce((sum, line) => {
      if (line.kind === 'item' && !voided.has(line.id)) return sum + line.amount;
      if (line.kind === 'discount' || line.kind === 'surcharge') return sum + line.amount;
      return sum;
    }, 0);
  };

  const cover = (): number => {
    const voided = voidedIds();
    return lines.reduce(
      (sum, line) =>
        line.kind === 'item' && !voided.has(line.id) && line.desc.startsWith('Coperto')
          ? sum + line.qty
          : sum,
      0,
    );
  };

  const push = (line: SaleLine) => {
    if (!open) {
      open = true;
      send({ t: 'sale.open' });
    }
    lines = [...lines, line];
    send({ t: 'sale.line', line });
    send({ t: 'sale.total', total: total(), cover: cover() });
  };

  const snapshot = (): Partial<DisplayState> => ({
    phase: payment ? 'payment' : open ? 'sale' : 'idle',
    lines,
    total: total(),
    cover: cover(),
    payment,
  });

  return {
    connect(handlers) {
      listeners.add(handlers);
      handlers.onLink(offline ? 'lost' : 'online');
      return () => {
        listeners.delete(handlers);
      };
    },

    requestSync() {
      send({ t: 'sync', state: snapshot() });
    },

    addLine() {
      const item = CATALOG[itemCount % CATALOG.length] ?? CATALOG[0];
      if (!item) return;
      itemCount++;
      push({ kind: 'item', id: nextId(), desc: item.desc, qty: item.qty, amount: item.amount });
    },

    addDiscount() {
      const base = total();
      if (base <= 0) return;
      push({ kind: 'discount', id: nextId(), desc: 'Sconto 10%', amount: -Math.round(base / 10) });
    },

    voidLast() {
      const voided = voidedIds();
      const target = [...lines]
        .reverse()
        .find((line) => line.kind === 'item' && !voided.has(line.id));
      if (!target || target.kind !== 'item') return;
      push({
        kind: 'void',
        id: nextId(),
        desc: target.desc,
        amount: target.amount,
        voidsId: target.id,
      });
    },

    addNote() {
      const text = NOTES[noteIndex % NOTES.length] ?? '';
      noteIndex++;
      push({ kind: 'note', id: nextId(), text });
    },

    sendMessage() {
      const index = messageIndex % (MESSAGES.length + 1);
      messageIndex++;
      const text = index === 0 ? `Totale ${formatEuro(total())}` : (MESSAGES[index - 1] ?? '');
      send({ t: 'display.msg', text });
    },

    showQr() {
      if (!open) return;
      payment = { provider: 'satispay', png: FAKE_QR, amount: total() };
      send({ t: 'pay.qr', ...payment });
    },

    clearQr() {
      if (!payment) return;
      payment = undefined;
      send({ t: 'pay.clear' });
    },

    endSale() {
      if (!open) return;
      send({ t: 'sale.close' });
      lines = [];
      open = false;
      payment = undefined;
    },

    setOffline(value) {
      if (offline === value) return;
      offline = value;
      setLink(value ? 'lost' : 'online');
    },

    snapshot,
  };
}

/** Moduli per lato del QR finto (versione 2). */
export const FAKE_QR_MODULES = 25;

/**
 * QR finto come nei mockup: geometria corretta (tre finder, timing, alignment) ma NON scansionabile.
 * Arriva come quello vero: un PNG in Base64, un pixel per modulo e senza quiet zone, che aggiunge
 * QrStage. In produzione il PNG arriva dalla cassa.
 */
const FAKE_QR = fakeQrDataUri(FAKE_QR_MODULES, 18901019);

function fakeQrDataUri(size: number, seed: number): string {
  const grid: boolean[][] = Array.from({ length: size }, () => Array<boolean>(size).fill(false));
  const set = (r: number, c: number, on: boolean) => {
    const row = grid[r];
    if (row) row[c] = on;
  };

  for (const [r0, c0] of [
    [0, 0],
    [0, size - 7],
    [size - 7, 0],
  ] as const) {
    for (let y = 0; y < 7; y++)
      for (let x = 0; x < 7; x++)
        set(
          r0 + y,
          c0 + x,
          y === 0 || y === 6 || x === 0 || x === 6 || (y >= 2 && y <= 4 && x >= 2 && x <= 4),
        );
  }
  for (let i = 8; i < size - 8; i++) {
    set(6, i, i % 2 === 0);
    set(i, 6, i % 2 === 0);
  }
  for (let y = 0; y < 5; y++)
    for (let x = 0; x < 5; x++)
      set(
        size - 9 + y,
        size - 9 + x,
        y === 0 || y === 4 || x === 0 || x === 4 || (y === 2 && x === 2),
      );

  let s = seed;
  const random = () => {
    s = (s * 1103515245 + 12345) % 2147483648;
    return s / 2147483648;
  };
  const reserved = (r: number, c: number) =>
    (r < 9 && c < 9) ||
    (r < 9 && c >= size - 8) ||
    (r >= size - 8 && c < 9) ||
    r === 6 ||
    c === 6 ||
    (r >= size - 10 && r <= size - 4 && c >= size - 10 && c <= size - 4);
  for (let r = 0; r < size; r++)
    for (let c = 0; c < size; c++) if (!reserved(r, c)) set(r, c, random() > 0.52);

  return grayPngDataUri(size, size, (x, y) => (grid[y]?.[x] ? 0 : 255));
}
