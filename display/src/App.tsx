import { useMemo, useState } from 'react';
import { presetConfig, type ThemeConfig } from './theme/theme';
import { createMockTransport } from './transport/mock';
import { Bench } from './dev/Bench';

/**
 * M1: tema, formato e cassa finta, verificabili dal banco di prova.
 * Da M2 qui monta <DisplayRoot> con i sei stati; il banco resta per lo sviluppo.
 */
export function App() {
  const [config, setConfig] = useState<ThemeConfig>(() => presetConfig('bluetech', 'notte'));
  const cashier = useMemo(() => createMockTransport(), []);

  // Il banco monta il ThemeProvider: è lui a sapere in che fase è la cassa finta.
  return <Bench config={config} onConfigChange={setConfig} cashier={cashier} />;
}
