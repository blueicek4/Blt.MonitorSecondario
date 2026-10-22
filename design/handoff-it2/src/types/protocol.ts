/**
 * Bluetech Display — protocollo Agent → display e stato dello schermo.
 * ITERAZIONE 2. Le differenze dall'iterazione 1 sono marcate con [IT2].
 *
 * ATTENZIONE: resta una PROPOSTA ragionata, non una lettura del codice
 * esistente. Se il repo ha già un protocollo che funziona, vince il repo:
 * aggiungi solo i campi [IT2] che mancano. Le domande aperte sono in CLAUDE.md.
 *
 * Regola che attraversa tutto il file: GLI IMPORTI SONO INTERI IN CENTESIMI.
 */

import type { ThemeId, ThemeModeSetting } from '../tokens/themes';

export type Cents = number;

// ---------------------------------------------------------------------------
// Righe dello scontrino
// ---------------------------------------------------------------------------

export type SaleLine =
  | {
      kind: 'item';
      /** Stabile per tutta la vendita. Se la cassa ristampa tutto, l'id lo assegna l'Agent col diff. */
      id: string;
      desc: string;
      qty: number;
      amount: Cents;
      photo?: string;
    }
  | { kind: 'discount'; id: string; desc: string; amount: Cents } // amount < 0
  | { kind: 'surcharge'; id: string; desc: string; amount: Cents } // amount > 0
  | { kind: 'void'; id: string; desc: string; amount: Cents; voidsId?: string }
  | { kind: 'note'; id: string; text: string };

export type LineKind = SaleLine['kind'];

/**
 * [IT2] Una riga COME LA VEDE IL DISPLAY, dopo l'accorpamento degli articoli
 * ripetuti (SPEC §3.2). `sourceIds` tiene gli id originali: uno storno che
 * punta a uno qualunque di loro riduce la quantità della riga accorpata.
 * La `key` React è `id` = il primo di sourceIds. Mai l'indice.
 */
export type DisplayLine = SaleLine & {
  sourceIds: string[];
  /** Per l'animazione: cosa è appena successo a questa riga. */
  last?: 'entered' | 'qtyBumped' | 'voiding';
};

// ---------------------------------------------------------------------------
// Messaggi in ingresso (Agent → display)
// ---------------------------------------------------------------------------

export type CashIn =
  | { t: 'sale.open'; register?: string; table?: string }
  | { t: 'sale.line'; line: SaleLine }
  | { t: 'sale.total'; total: Cents; cover: number }
  | { t: 'sale.close' }
  | { t: 'display.msg'; text: string; ttl?: number }
  | { t: 'pay.qr'; provider: 'satispay'; png: string; amount: Cents }
  | { t: 'pay.clear' }
  /** [IT2] Scontrino digitale. Vedi SPEC §2.7. */
  | { t: 'receipt.show'; qr: string; paper?: ReceiptPaper; ttlMs?: number }
  /** [IT2] Esito di una richiesta di copia di carta. */
  | { t: 'receipt.print'; status: PrintStatus }
  /** [IT2] Configurazione GIÀ RISOLTA per questo schermo. Il display non eredita niente: lo fa l'Agent. */
  | { t: 'cfg'; screen: ScreenConfig; showcase: ShowcaseByPlace; ticker?: string }
  | { t: 'sync'; state: Partial<DisplayState> };

/** [IT2] Messaggi in uscita (display → Agent). Solo questo, e solo sugli schermi touch. */
export type DisplayOut = { t: 'receipt.print.request'; saleId?: string };

export type PrintStatus = 'idle' | 'printing' | 'ready' | 'failed';

/**
 * [IT2] Un blocco dello scontrino digitale e il suo posto.
 * Vincoli (li valida l'Agent al salvataggio, il display si fida):
 *  - 'carta' ha sempre riga 'piena';
 *  - due blocchi nella stessa colonna solo come 'alto' + 'basso';
 *  - almeno un blocco attivo.
 * Default: [{carta,1,piena}, {qr,2,piena}, {reclame off}].
 */
export interface ReceiptBlock {
  tipo: 'carta' | 'qr' | 'reclame';
  attivo: boolean;
  colonna: 1 | 2 | 3;
  riga: 'alto' | 'basso' | 'piena';
}

export interface ReceiptPaper {
  /** Righe di testo già impaginate dall'Agent, a larghezza fissa. Il display non le ricalcola. */
  lines: string[];
  /** Colonne della stampante di riferimento, per il font monospazio. */
  columns: number;
}

// ---------------------------------------------------------------------------
// [IT2] Contenuti: cosa va in onda, già scelto dall'Agent per posto
// ---------------------------------------------------------------------------

/** I tre posti dello schermo. Fissi: sono il prodotto, non si configurano. */
export type Place = 'attesa' | 'conto' | 'grazie';

/**
 * A solo testo · B foto parlante · C foto muta · D con QR.
 * Il modello lo decide la risposta a "la foto ha già delle scritte?", non il titolare.
 */
export type SlideModel = 'A' | 'B' | 'C' | 'D';

export type PromoLabel =
  | 'promozione' | 'novita' | 'settimana' | 'consiglio' | 'evento' | 'cliente' | null;

export interface ShowcaseItem {
  id: string;
  tipo: 'foto' | 'video' | 'promozione';
  modello: SlideModel;
  /** URL servito dall'Agent. Per i video: anche i due derivati (primo fotogramma, sfocato). */
  media?: { src: string; poster?: string; posterBlur?: string; focus?: { x: number; y: number } };
  etichetta?: PromoLabel;
  /** Max 60 caratteri. */
  titolo?: string;
  testo?: string;
  /** PNG o testo da codificare. Un QR solo per elemento. */
  qr?: { png: string; didascalia?: string };
  /** ms. Default 7000, 15000 se c'è un QR, durata nativa per i video. */
  durataMs?: number;
}

export interface Showcase {
  /** id della vetrina che va in onda: serve alle statistiche e al pannello "Adesso". */
  vetrinaId: string;
  elementi: ShowcaseItem[];
  /** "un QR ogni N": inserisce i QR a cadenza fissa nella sequenza casuale. 0 = mai. */
  qrOgni: number;
}

export type ShowcaseByPlace = Partial<Record<Place, Showcase>>;

// ---------------------------------------------------------------------------
// Stato dello schermo
// ---------------------------------------------------------------------------

/** [IT2] aggiunto 'receipt'. */
export type DisplayPhase = 'idle' | 'sale' | 'payment' | 'thanks' | 'receipt';

export type LinkState = 'online' | 'stale' | 'lost';

export interface CashierMessage {
  text: string;
  amount?: Cents;
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
  /** Come arrivano dalla cassa, append-only. */
  lines: SaleLine[];
  /** [IT2] Derivate da `lines` con l'accorpamento. È questa che si disegna. */
  view: DisplayLine[];
  total: Cents;
  cover: number;
  register?: string;
  table?: string;
  payment?: PaymentRequest;
  /** [IT2] Presente solo in phase 'receipt'. */
  receipt?: { qr: string; paper?: ReceiptPaper; print: PrintStatus; until: number };
  message?: CashierMessage;
  lastItemPhoto?: string;
  link: LinkState;
}

// ---------------------------------------------------------------------------
// [IT2] Configurazione dello schermo, già risolta (marchio → cassa → schermo)
// ---------------------------------------------------------------------------

/** Asse A: quanto il monitor vende. Scelta commerciale. Default 'vivo'. */
export type AttractionLevel = 'sobrio' | 'vivo' | 'spettacolo' | 'troppo';

/** Asse B: quanto la macchina regge. Etichette nel pannello: Piena, Leggera, Eco. */
export type MotionLevel = 'full' | 'light' | 'eco';

export type BgStyle = 'tinta' | 'sfumato' | 'immagine' | 'video' | 'mesh';
export type QrLayout = 'fullscreen' | 'beside';
export type ColumnAlign = 'alto' | 'centro' | 'basso';

export interface ScreenConfig {
  // aspetto (dal marchio)
  tema: ThemeId;
  modo: ThemeModeSetting;
  /** 'HH:MM'. Servono solo con modo 'auto'. */
  giornoDalle?: string;
  giornoAlle?: string;
  /** Sovrascritture di colore del marchio. Validate dall'Agent: mai sotto 4,5:1. */
  colori?: Partial<Record<'bg' | 'surface' | 'text' | 'muted' | 'primary' | 'onPrimary' | 'accent' | 'secondary', string>>;
  fontTitoli?: string;
  fontTesto?: string;
  raggio?: number;
  vetro?: number;
  fondo: BgStyle;
  fondoMedia?: { src: string; poster: string; posterBlur: string };
  footerVariant: 'light' | 'dark';
  identita: { nomeLocale: string; sottotitolo?: string; logo?: string; logoChiaro?: string };

  // comportamento (marchio, sovrascrivibile per schermo)
  attrazione: AttractionLevel;
  animazioni: MotionLevel;
  /** Su 24. Fermi 9–17; a 1366 e 1280 il massimo è 16 (la colonna deve reggere un QR). Default 16. */
  colonneConto: number;
  /** Su 24. Fermi 8–14. Default 10. Solo con qrPagamento 'beside'. */
  colonneContoPagamento: number;
  colonnaAllineamento: ColumnAlign;
  qrPagamento: QrLayout;
  /** I prezzi compaiono solo quando la cassa mostra il conto. Vedi SPEC §2.2.1. */
  prezziSoloSulConto: boolean;
  /** Righe a 30 px sul formato stretto. Default false. */
  righeCompatte: boolean;
  showLastItemPhoto: boolean;
  /** Forza la disposizione invece di dedurla dal rapporto dei lati. */
  disposizione?: LayoutVariant;

  // scritte
  scritte: { benvenuto?: string; titoloConto?: string; grazie?: string; secondiGrazie: number };

  // scontrino digitale
  receipt: {
    /** Ogni blocco in uno dei sei posti (3 colonne × 2 righe). SPEC §2.7. */
    blocchi: ReceiptBlock[];
    /** Default 45. */
    secondi: number;
    /** Il pulsante della copia di carta esiste solo se lo schermo è touch E c'è una stampante. */
    copiaCarta: boolean;
  };
}

// ---------------------------------------------------------------------------
// Formato dello schermo
// ---------------------------------------------------------------------------

/** Dal RAPPORTO D'ASPETTO. [IT2] aggiunto 'palmare' (< 0,5). */
export type LayoutVariant = 'wide' | 'narrow' | 'portrait' | 'palmare';

export type FormatKey =
  | '1920x1080'
  | '1366x768'
  | '1280x800'
  | '1024x768'
  | '1080x1920'
  | '390x844'; // [IT2]

// ---------------------------------------------------------------------------
// Trasporto
// ---------------------------------------------------------------------------

export interface Transport {
  connect(handlers: {
    onMessage: (msg: CashIn) => void;
    onLink: (state: LinkState) => void;
  }): () => void;
  requestSync(): void;
  /** [IT2] */
  send(msg: DisplayOut): void;
}
