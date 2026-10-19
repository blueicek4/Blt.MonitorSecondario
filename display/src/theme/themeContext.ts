import { createContext, useContext } from 'react';
import type { FormatKey, LayoutVariant, QrLayout } from '../types/protocol';
import type { ResolvedTheme, ThemeConfig } from './theme';

export interface ThemeContextValue extends ResolvedTheme {
  config: ThemeConfig;
  layout: LayoutVariant;
  format: FormatKey;
  /** Quello effettivo: su narrow e portrait è sempre 'fullscreen' (SPEC §2.4). */
  qrLayout: QrLayout;
}

export const ThemeContext = createContext<ThemeContextValue | null>(null);

export function useTheme(): ThemeContextValue {
  const value = useContext(ThemeContext);
  if (!value) throw new Error('useTheme va usato dentro <ThemeProvider>');
  return value;
}
