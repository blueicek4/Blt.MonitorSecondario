/**
 * Bluetech Display — protocollo cassa → monitor e stato dello schermo.
 *
 * ATTENZIONE: questo file è una PROPOSTA ragionata, non una lettura del
 * comportamento reale della cassa Passepartout. Le quattro domande da
 * risolvere prima di implementare il trasporto sono in CLAUDE.md.
 *
 * Regola che attraversa tutto il file: GLI IMPORTI SONO INTERI IN CENTESIMI.
 * Mai number decimale, mai parseFloat su una stringa della cassa.
 */

// ---------------------------------------------------------------------------
// Righe dello scontrino
// ---------------------------------------------------------------------------

/** Centesimi di euro. Positivo = addebito, negativo = sconto. */
export type Cents = number;

export type SaleLine =
  | {
      kind: 'item';
      /** Stabile per tutta la vendita. È la `key` React. Mai l'indice. */
      id: string;
      desc: string;
      /** La UI mostra la quantità solo se > 1. */
      qty: number;
      amount: Cents;
      /** URL o chiave della foto articolo, se l'anagrafica ne ha una. */
      photo?: string;
    }
  | { kind: 'discount'; id: string; desc: string; amount: Cents } // amount < 0
  | { kind: 'surcharge'; id: string; desc: string; amount: Cents } // amount > 0
  | {
      kind: 'void';
      id: string;
      desc: string;
      amount: Cents;
      /** id della riga che questo storno annulla, quando la cassa lo dice. */
      voidsId?: string;
    }
  | { kind: 'note'; id: string; text: string };

export type LineKind = SaleLine['kind'];

// ---------------------------------------------------------------------------
// Messaggi in ingresso dalla cassa
// ---------------------------------------------------------------------------

export type CashIn =
  /** Apre una vendita. Una `sale.line` senza questo la apre implicitamente. */
  | { t: 'sale.open'; register?: string; table?: string }
  | { t: 'sale.line'; line: SaleLine }
  /** Il totale è autoritativo: non si somma lato monitor. */
  | { t: 'sale.total'; total: Cents; cover: number }
  | { t: 'sale.close' }
  /** Messaggio da visore. `ttl` in ms, default 6000. */
  | { t: 'display.msg'; text: string; ttl?: number }
  /** `png` = data URI o percorso locale servito dalla cassa. */
  | { t: 'pay.qr'; provider: 'satispay'; png: string; amount: Cents }
  | { t: 'pay.clear' }
  | { t: 'cfg'; theme: ThemeTokens; ads: AdSlide[]; ticker?: string }
  /** Risposta allo snapshot richiesto dopo una riconnessione. */
  | { t: 'sync'; state: Partial<DisplayState> };

export type CashInKind = CashIn['t'];

export interface AdSlide {
  id: string;
  media: 'image' | 'video';
  src: string;
  /** Solo per le immagini. Default 7000 ms. I video durano quanto durano. */
  durationMs?: number;
}

// ---------------------------------------------------------------------------
// Stato dello schermo
// ---------------------------------------------------------------------------

export type DisplayPhase = 'idle' | 'sale' | 'payment' | 'thanks';

/**
 * 'online'  dati freschi
 * 'stale'   nessun messaggio da 8 s, socket ancora aperto
 * 'lost'    socket caduto, riconnessione in corso
 *
 * In 'stale' e 'lost' l'ultima schermata resta INTATTA. Vedi SPEC §2.6.
 */
export type LinkState = 'online' | 'stale' | 'lost';

export interface CashierMessage {
  text: string;
  /** Estratto dal testo quando contiene una cifra riconoscibile. */
  amount?: Cents;
  /** Es. "Resto", "Totale". Assente → forma a testo semplice. */
  label?: string;
  expiresAt: number;
}

export interface PaymentRequest {
  provider: 'satispay';
  png: string;
  amount: Cents;
}

export interface DisplayState {
  phase: DisplayPhase;
  /** Ordine di battuta, append-only entro la vendita. */
  lines: SaleLine[];
  /** Dalla cassa, non calcolato. */
  total: Cents;
  cover: number;
  register?: string;
  table?: string;
  /** Presente solo in phase 'payment'. */
  payment?: PaymentRequest;
  /** Livello sovrapposto: non è una fase. */
  message?: CashierMessage;
  /** Foto dell'ultimo articolo battuto, se il token la abilita. */
  lastItemPhoto?: string;
  link: LinkState;
}

// ---------------------------------------------------------------------------
// Token del tema — l'unica superficie che il pannello del locale scrive
// ---------------------------------------------------------------------------

export type ThemeId = 'bluetech' | 'trattoria' | 'fine' | 'cocktail';
export type ThemeMode = 'notte' | 'giorno';
export type MotionLevel = 'full' | 'light' | 'eco';
export type BgStyle = 'solid' | 'gradient' | 'mesh' | 'image' | 'video';
export type QrLayout = 'fullscreen' | 'beside';

export interface ThemeTokens {
  // colore
  cPrimary: string;
  cSecondary: string;
  cAccent: string;
  cBg: string;
  cSurface: string;
  cText: string;
  cMuted: string;
  cOnPrimary: string;

  // forma e testo
  fontDisplay: string;
  fontText: string;
  /** 0–32 px. */
  radius: number;

  // fondo
  bgStyle: BgStyle;
  bgAsset?: string;

  /** 0–100. Derivati: alpha = glass × 0.0012, blur = glass × 0.28 px. */
  glass: number;

  motion: MotionLevel;
  /** 'auto' alterna giorno/notte sugli orari di apertura. */
  mode: ThemeMode | 'auto';

  /** Ignorato sui formati narrow e portrait: lì vale sempre 'fullscreen'. */
  qrLayout: QrLayout;

  /** Riga 30 px invece di 23 sul formato narrow. Default false. Vedi SPEC §7. */
  compactRows: boolean;

  /** Foto dell'articolo appena battuto nella colonna laterale. Default false. */
  showLastItemPhoto: boolean;

  /** L'unica scelta che il tema ha sul footer Bluetech. */
  footerVariant: 'light' | 'dark';
}

// ---------------------------------------------------------------------------
// Formato dello schermo
// ---------------------------------------------------------------------------

/** Scelto dal RAPPORTO D'ASPETTO, non dalla larghezza. Vedi SPEC §9. */
export type LayoutVariant = 'wide' | 'narrow' | 'portrait';

/** Alimenta `data-format` sul root per la tabella tipografica. */
export type FormatKey =
  | '1920x1080'
  | '1366x768'
  | '1280x800'
  | '1024x768'
  | '1080x1920';

// ---------------------------------------------------------------------------
// Trasporto — una sola interfaccia, così sostituirlo costa un file
// ---------------------------------------------------------------------------

export interface Transport {
  /** Si chiama una volta all'avvio. Ritorna la funzione di teardown. */
  connect(handlers: {
    onMessage: (msg: CashIn) => void;
    onLink: (state: LinkState) => void;
  }): () => void;
  /** Dopo una riconnessione: chiede alla cassa lo stato completo. */
  requestSync(): void;
}
