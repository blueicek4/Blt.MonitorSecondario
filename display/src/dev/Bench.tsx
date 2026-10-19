import { useEffect, useState } from 'react';
import type {
  CashIn,
  LinkState,
  MotionLevel,
  QrLayout,
  ThemeId,
  ThemeMode,
} from '../types/protocol';
import { formatAmount, formatEuro } from '../money/money';
import { useTheme } from '../theme/themeContext';
import { presetConfig, type ThemeConfig } from '../theme/theme';
import type { MockCashier } from '../transport/mock';
import './bench.css';

interface BenchProps {
  config: ThemeConfig;
  onConfigChange: (config: ThemeConfig) => void;
  cashier: MockCashier;
}

const THEMES: Array<[ThemeId, string]> = [
  ['bluetech', 'Bluetech'],
  ['trattoria', 'Trattoria'],
  ['fine', 'Fine dining'],
  ['cocktail', 'Cocktail bar'],
];
const MODES: Array<[ThemeMode | 'auto', string]> = [
  ['notte', 'Notte'],
  ['giorno', 'Giorno'],
  ['auto', 'Auto'],
];
const MOTIONS: Array<[MotionLevel, string]> = [
  ['full', 'Piena'],
  ['light', 'Leggera'],
  ['eco', 'Eco'],
];
const QR_LAYOUTS: Array<[QrLayout, string]> = [
  ['beside', 'Accanto al conto'],
  ['fullscreen', 'A tutto schermo'],
];

/** Banco di prova di M1: non è un'interfaccia per il cliente. */
export function Bench({ config, onConfigChange, cashier }: BenchProps) {
  const theme = useTheme();
  const [log, setLog] = useState<CashIn[]>([]);
  const [link, setLink] = useState<LinkState>('online');
  const [, setRevision] = useState(0);

  useEffect(
    () =>
      cashier.connect({
        onMessage: (message) => {
          setLog((entries) => [message, ...entries].slice(0, 14));
          setRevision((n) => n + 1);
        },
        onLink: (state) => {
          setLink(state);
          if (state === 'online') cashier.requestSync();
        },
      }),
    [cashier],
  );

  const tokens = config.tokens;
  const change = (behavior: Partial<typeof tokens>) =>
    onConfigChange({ ...config, tokens: { ...tokens, ...behavior } });
  const pickPreset = (preset: ThemeId, mode: ThemeMode | 'auto') =>
    onConfigChange(
      presetConfig(preset, mode, {
        motion: tokens.motion,
        qrLayout: tokens.qrLayout,
        compactRows: tokens.compactRows,
        showLastItemPhoto: tokens.showLastItemPhoto,
      }),
    );

  const sale = cashier.snapshot();
  const offline = link !== 'online';

  return (
    <div className="bench">
      <header className="bench-head">
        <h1>Banco di prova · M1</h1>
        <dl className="bench-facts">
          <Fact label="Formato" value={`${theme.format} · ${theme.layout}`} />
          <Fact label="Tema" value={`${theme.key} · ${theme.mode}`} />
          <Fact label="QR" value={theme.qrLayout} />
          <Fact label="Collegamento" value={link} />
        </dl>
        {theme.fallback && (
          <p className="bench-warn" role="status">
            Tema sostituito: {theme.problems.join('; ')}
          </p>
        )}
      </header>

      <section className="bench-controls" aria-label="Tema">
        <Chips
          label="Tema"
          options={THEMES}
          value={config.preset}
          onPick={(preset) => pickPreset(preset, tokens.mode)}
        />
        <Chips
          label="Modo"
          options={MODES}
          value={tokens.mode}
          onPick={(mode) => pickPreset(config.preset, mode)}
        />
        <Chips
          label="Animazioni"
          options={MOTIONS}
          value={tokens.motion}
          onPick={(motion) => change({ motion })}
        />
        <Chips
          label="QR"
          options={QR_LAYOUTS}
          value={tokens.qrLayout}
          onPick={(qrLayout) => change({ qrLayout })}
        />
        <label className="bench-check">
          <input
            type="checkbox"
            checked={tokens.compactRows}
            onChange={(event) => change({ compactRows: event.target.checked })}
          />
          Righe grandi sul 10″ (compactRows)
        </label>
      </section>

      <section className="bench-controls" aria-label="Cassa finta">
        <span className="bench-label">Cassa finta</span>
        <button type="button" onClick={cashier.addLine}>
          Aggiungi riga
        </button>
        <button type="button" onClick={cashier.addDiscount}>
          Sconto 10%
        </button>
        <button type="button" onClick={cashier.voidLast}>
          Storna ultima
        </button>
        <button type="button" onClick={cashier.addNote}>
          Testo libero
        </button>
        <button type="button" onClick={cashier.sendMessage}>
          Messaggio
        </button>
        <button type="button" onClick={cashier.showQr}>
          QR Satispay
        </button>
        <button type="button" onClick={cashier.clearQr}>
          Togli QR
        </button>
        <button type="button" onClick={cashier.endSale}>
          Fine vendita
        </button>
        <button type="button" onClick={() => cashier.setOffline(!offline)}>
          {offline ? 'Ripristina connessione' : 'Connessione persa'}
        </button>
      </section>

      <main className="bench-main">
        <section className="bench-card" aria-label="Anteprima token">
          <p className="bench-kicker">Il tuo conto · {sale.phase}</p>
          <ul className="bench-rows">
            {(sale.lines ?? []).map((line) => (
              <li key={line.id} className={`bench-row bench-row-${line.kind}`}>
                <span className="bench-desc">{line.kind === 'note' ? line.text : line.desc}</span>
                {line.kind === 'item' && line.qty > 1 && (
                  <span className="bench-qty bt-num">×{line.qty}</span>
                )}
                {line.kind !== 'note' && (
                  <span className="bench-amount bt-num">
                    {formatAmount(line.amount, {
                      sign: line.kind === 'item' || line.kind === 'void' ? 'negative' : 'always',
                    })}
                  </span>
                )}
              </li>
            ))}
          </ul>
          <div className="bench-total">
            <span className="bench-kicker">Totale</span>
            <span className="bench-total-value bt-num">{formatEuro(sale.total ?? 0)}</span>
          </div>
          <div className="bench-band">Messaggio dalla cassa</div>
        </section>

        <section className="bench-log" aria-label="Messaggi dalla cassa">
          <h2>Ultimi messaggi</h2>
          <ol>
            {log.map((message, index) => (
              <li key={log.length - index}>
                <code>{describe(message)}</code>
              </li>
            ))}
          </ol>
        </section>
      </main>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function Chips<T extends string>({
  label,
  options,
  value,
  onPick,
}: {
  label: string;
  options: Array<[T, string]>;
  value: T;
  onPick: (value: T) => void;
}) {
  return (
    <div className="bench-chips" role="group" aria-label={label}>
      <span className="bench-label">{label}</span>
      {options.map(([key, text]) => (
        <button key={key} type="button" aria-pressed={key === value} onClick={() => onPick(key)}>
          {text}
        </button>
      ))}
    </div>
  );
}

function describe(message: CashIn): string {
  switch (message.t) {
    case 'sale.line': {
      const line = message.line;
      const text = line.kind === 'note' ? line.text : `${line.desc} ${formatAmount(line.amount)}`;
      return `sale.line ${line.kind} ${line.id}${line.kind === 'void' && line.voidsId ? `→${line.voidsId}` : ''} · ${text}`;
    }
    case 'sale.total':
      return `sale.total ${message.total} (${formatEuro(message.total)}) · coperti ${message.cover}`;
    case 'display.msg':
      return `display.msg "${message.text}"`;
    case 'pay.qr':
      return `pay.qr ${message.provider} ${formatEuro(message.amount)}`;
    case 'sync':
      return `sync ${message.state.lines?.length ?? 0} righe · ${formatEuro(message.state.total ?? 0)}`;
    default:
      return message.t;
  }
}
