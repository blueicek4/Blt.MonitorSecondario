import { useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import { paletteToCssVars } from '../tokens/themes';
import type { QrLayout, ThemeMode } from '../types/protocol';
import { useFormat } from '../format/useFormat';
import { DEFAULT_DAY_HOURS, modeAt, resolveTheme, type DayHours, type ThemeConfig } from './theme';
import { ThemeContext, type ThemeContextValue } from './themeContext';

/** Ogni quanto si ricontrolla l'ora per `mode: 'auto'`. */
const AUTO_MODE_CHECK_MS = 30_000;

interface ThemeProviderProps {
  config: ThemeConfig;
  children: ReactNode;
  /** Orologio iniettabile, per i test. */
  now?: () => Date;
  dayHours?: DayHours;
}

/**
 * Applica il tema sul nodo radice: custom properties da `paletteToCssVars()` e gli attributi
 * `data-motion`, `data-format`, `data-layout`, `data-qr`, `data-compact-rows` letti da tokens.css.
 * Cambiare tema o modo cambia solo variabili: nessun layout.
 */
export function ThemeProvider({
  config,
  children,
  now,
  dayHours = DEFAULT_DAY_HOURS,
}: ThemeProviderProps) {
  const auto = config.tokens.mode === 'auto';
  const autoMode = useAutoMode(auto, now, dayHours);
  const mode: ThemeMode = config.tokens.mode === 'auto' ? autoMode : config.tokens.mode;
  const screen = useFormat();
  const resolved = useMemo(() => resolveTheme(config, mode), [config, mode]);
  const qrLayout: QrLayout = screen.layout === 'wide' ? config.tokens.qrLayout : 'fullscreen';

  useEffect(() => {
    if (resolved.fallback) {
      console.warn(`Tema non leggibile, uso ${resolved.key}: ${resolved.problems.join('; ')}`);
    }
  }, [resolved]);

  useLayoutEffect(() => {
    const root = document.documentElement;
    for (const [name, value] of Object.entries(paletteToCssVars(resolved.palette))) {
      root.style.setProperty(name, value);
    }
    root.dataset.theme = resolved.key;
    root.dataset.mode = resolved.mode;
    root.dataset.motion = config.tokens.motion;
    root.dataset.format = screen.format;
    root.dataset.layout = screen.layout;
    root.dataset.qr = qrLayout;
    root.dataset.compactRows = String(config.tokens.compactRows);
  }, [
    resolved,
    config.tokens.motion,
    config.tokens.compactRows,
    screen.format,
    screen.layout,
    qrLayout,
  ]);

  const value = useMemo<ThemeContextValue>(
    () => ({ ...resolved, config, layout: screen.layout, format: screen.format, qrLayout }),
    [resolved, config, screen.layout, screen.format, qrLayout],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

const systemClock = () => new Date();

/** Modo giorno/notte dall'orologio; si aggiorna da solo quando cambia l'ora. */
function useAutoMode(enabled: boolean, now: (() => Date) | undefined, hours: DayHours): ThemeMode {
  const clock = now ?? systemClock;
  const [mode, setMode] = useState<ThemeMode>(() => modeAt(clock(), hours));

  useEffect(() => {
    if (!enabled) return;
    const update = () => setMode(modeAt(clock(), hours));
    update();
    const timer = window.setInterval(update, AUTO_MODE_CHECK_MS);
    return () => window.clearInterval(timer);
  }, [enabled, clock, hours]);

  return mode;
}
