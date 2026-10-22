/**
 * impostazioni.json — versione 3 (iterazione 2).
 *
 * Resta UN file JSON che il tecnico legge, copia e porta via. Nessun database.
 * Cambia l'organizzazione: quattro livelli (installazione → marchio → cassa →
 * schermo), regole riusabili, override espliciti, due ruoli.
 *
 * Fonte: artboard Config.dc.html e Contenuti.dc.html (design-reference/iterazione-2).
 * Se il repo ha già i tipi della v2, questo file descrive la destinazione,
 * e migrateV2toV3 la strada. La migrazione gira da sola al primo avvio.
 */

import type { ThemeId, ThemeModeSetting } from '../tokens/themes';
import type {
  AttractionLevel, BgStyle, ColumnAlign, LayoutVariant, MotionLevel,
  Place, PromoLabel, QrLayout, ReceiptBlock, SlideModel,
} from './protocol';

export interface ImpostazioniV3 {
  versione: 3;
  installazione: Installazione;
  marchi: Marchio[];
  casse: Cassa[];
  schermi: Schermo[];
  stampanti: Stampante[];
}

// --- livello 1: solo il tecnico, vale al riavvio dell'Agent -----------------

export interface Installazione {
  cartelle: { dati: string; log: string; display: string; giorniLog: number };
  rete: { porta: number; apertoInRete: boolean; https: boolean };
  backOfficeDallaRete: boolean;
  /** Due hash nello stesso array. Nessun account, nessun server. */
  accessi: { ruolo: 'titolare' | 'tecnico'; hash: string }[];
}

// --- livello 2: il titolare, vale subito ------------------------------------

export interface Marchio {
  id: string;
  nome: string;
  identita: { nomeLocale: string; sottotitolo?: string; logo?: string; logoChiaro?: string };
  aspetto: {
    tema: ThemeId;
    modo: ThemeModeSetting;
    giornoDalle?: string;
    giornoAlle?: string;
    colori?: Partial<Record<'bg' | 'surface' | 'text' | 'muted' | 'primary' | 'onPrimary' | 'accent' | 'secondary', string>>;
    fontTitoli?: string;
    fontTesto?: string;
    raggio?: number;
    vetro?: number;
    footer: 'light' | 'dark';
    fondo: BgStyle;
    /** id di un media in contenuti.media */
    fondoMedia?: string;
  };
  schermo: {
    animazioni: MotionLevel;
    attrazione: AttractionLevel;
    colonneConto: number;
    colonneContoPagamento: number;
    colonnaAllineamento: ColumnAlign;
    qrPagamento: QrLayout;
    prezziSoloSulConto: boolean;
    righeCompatte: boolean;
  };
  scritte: {
    benvenuto?: string;
    scorrevole?: string;
    titoloConto?: string;
    grazie?: string;
    secondiGrazie: number;
    etichettaPromo?: PromoLabel;
  };
  vendita: {
    chiusuraDopoSecondi: number;
    scontrinoDigitale: { attivo: boolean; secondi: number };
    /** Migrazione dal primo disegno: carta-qr → carta col 1, qr col 2; qr-carta → il contrario. */
    receipt: { blocchi: ReceiptBlock[] };
  };
  contenuti: Contenuti;
}

export interface Contenuti {
  media: Media[];
  promozioni: Promozione[];
  /** Riusabili: "il venerdì sera" serve a più vetrine. */
  regole: Regola[];
  vetrine: Vetrina[];
}

export interface Media {
  /** L'impronta del file: è anche il nome in dati\media\. */
  id: string;
  nome: string;
  tipo: 'foto' | 'video';
  byte: number;
  caricatoIl: string;
  /** 0–1. Il punto che sopravvive a ogni ritaglio. */
  focus: { x: number; y: number };
  /** La risposta a "questa foto ha già delle scritte dentro?". Decide il modello B o C. */
  haScritte: boolean;
}

export interface Promozione {
  id: string;
  nome: string;
  etichetta: PromoLabel;
  titolo: string;
  testo?: string;
  /** id di un media */
  immagine?: string;
  modello: SlideModel;
  qr?: string;
  qrDidascalia?: string;
  /** 'AAAA-MM-GG'. Fuori da qui la promozione non va in onda. */
  dalGiorno?: string;
  alGiorno?: string;
}

export interface Regola {
  id: string;
  nome: string;
  /** Vuoto = tutte. */
  casse: string[];
  /** 0 = lunedì … 6 = domenica. Vuoto = tutti. */
  giorni: number[];
  /** 'HH:MM'. alle < dalle = passa la mezzanotte. */
  dalle?: string;
  alle?: string;
  dalGiorno?: string;
  alGiorno?: string;
}

export interface Vetrina {
  id: string;
  nome: string;
  attiva: boolean;
  posti: Place[];
  /** id di una Regola. Assente = sempre. */
  regola?: string;
  /** Più basso = più in alto = vince. */
  ordine: number;
  qrOgni: number;
  secondi: number;
  elementi: { id: string; secondi?: number }[];
}

// --- livello 3: cassa --------------------------------------------------------

export interface Cassa {
  id: string;
  nome: string;
  marchio: string;
  /** Solo il tecnico lo vede. */
  fileCassa: string;
  override?: { scritte?: Partial<Marchio['scritte']> };
}

// --- livello 4: schermo ------------------------------------------------------

export interface Schermo {
  id: string;
  nome: string;
  /** hash del token di abbinamento */
  token: string;
  cassa: string;
  stampante?: string;
  autorizzatoIl: string;
  override?: {
    disposizione?: LayoutVariant;
    animazioni?: MotionLevel;
    attrazione?: AttractionLevel;
    colonneConto?: number;
    colonnaAllineamento?: ColumnAlign;
  };
}

export interface Stampante {
  id: string;
  nome: string;
  collegamento: 'rete' | 'usb' | 'seriale' | 'windows';
  indirizzo: string;
  larghezzaMm: 58 | 80;
  colonne: number;
  tabellaCaratteri: string;
  qrPuntiPerModulo: number;
  taglio: boolean;
  testoRitiro?: string;
  timeoutSecondi: number;
}

// --- stato di una vetrina, come lo mostra il pannello -------------------------

/**
 * I cinque stati, sempre visibili nell'elenco. "Coperta" è quello che salva le
 * chiamate al supporto: valida, piena, nell'orario giusto, ma una più in alto
 * la precede. Il nome di chi la copre va mostrato e reso cliccabile.
 */
export type StatoVetrina =
  | { stato: 'in-onda'; elementoCorrente?: string }
  | { stato: 'coperta'; da: string }
  | { stato: 'fuori-orario'; torna?: string }
  | { stato: 'vuota' }
  | { stato: 'spenta' };

/**
 * La catena di decisione, nell'ordine. Deve essere UNA funzione pura usata sia
 * dall'Agent (per mandare `showcase` al display) sia dal pannello (per spiegare
 * "perché vedo questo adesso?"). Due implementazioni divergono, sempre.
 *
 *  1. filtra per posto
 *  2. filtra per regola (cassa, giorno, ora, date)
 *  3. scarta le vuote (nessun elemento valido oggi)
 *  4. prende la prima per `ordine`; le altre valide diventano 'coperta'
 */
export type ScegliVetrina = (
  vetrine: Vetrina[], regole: Regola[], promozioni: Promozione[],
  ctx: { cassa: string; posto: Place; adesso: Date },
) => { vincente?: Vetrina; stati: Record<string, StatoVetrina> };
