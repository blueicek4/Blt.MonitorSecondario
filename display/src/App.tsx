import { useMemo, useState } from 'react';
import { ThemeProvider } from './theme/ThemeProvider';
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

  return (
    <ThemeProvider config={config}>
      <Bench config={config} onConfigChange={setConfig} cashier={cashier} />
    </ThemeProvider>
  );
}
