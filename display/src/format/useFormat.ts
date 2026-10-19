import { useMemo, useSyncExternalStore } from 'react';
import { resolveFormat, type ScreenFormat } from './format';

function subscribe(onChange: () => void): () => void {
  window.addEventListener('resize', onChange);
  window.addEventListener('orientationchange', onChange);
  return () => {
    window.removeEventListener('resize', onChange);
    window.removeEventListener('orientationchange', onChange);
  };
}

// Stringa come snapshot: stabile finché le dimensioni non cambiano, quindi niente render inutili.
const getSnapshot = () => `${window.innerWidth}x${window.innerHeight}`;

/** Rapporto d'aspetto → variante di layout + chiave di formato per `data-format`. */
export function useFormat(): ScreenFormat {
  const size = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  return useMemo(() => {
    const [width = 0, height = 0] = size.split('x').map(Number);
    return resolveFormat(width, height);
  }, [size]);
}
