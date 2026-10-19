# Albero dei componenti e firme delle props

```
<DisplayRoot>                     applica i token, legge il formato, monta i livelli
├─ <Background />                 tinta | gradiente | mesh | immagine | video
├─ <VenueLogo />
├─ <StateLayer>                   crossfade fra le fasi, elemento condiviso
│  ├─ <IdleScreen>
│  │  ├─ <AdPlaylist />           precarica 1 slide avanti, max 2 in memoria
│  │  └─ <Ticker />
│  ├─ <SaleScreen>
│  │  ├─ <ReceiptCard>
│  │  │  ├─ <ReceiptList />       finestra scorrevole, non lista virtuale
│  │  │  │  └─ <ReceiptRow />     6 varianti, discriminated union
│  │  │  └─ <TotalBlock>
│  │  │     └─ <Odometer />
│  │  └─ <SideSlot />             promo del locale | foto ultimo articolo
│  ├─ <PaymentScreen>             legge qrLayout: 'fullscreen' | 'beside'
│  │  ├─ <ReceiptCard compact />  solo in 'beside'
│  │  ├─ <QrStage />              fondo bianco, quiet zone, nessun effetto sopra
│  │  └─ <WaitingHalo />          attesa attorno al QR, mai sopra
│  └─ <ThanksScreen>
│     ├─ <Celebration />
│     └─ <QrStage small />        recensione | fidelity | menu, opzionale
├─ <MessageBand />                livello: spinge, non copre
├─ <LinkIndicator />              livello: pillola discreta in basso a sinistra
└─ <BluetechFooter />             FUORI da StateLayer: nessuna transizione lo tocca
```

## Perché il footer sta fuori

Se `BluetechFooter` vivesse dentro `StateLayer`, ogni transizione lo farebbe sfarfallare
e un tema potrebbe nasconderlo cambiando il contenitore. Fuori, è strutturalmente sempre
a schermo: il requisito commerciale diventa un fatto architetturale, non una regola da
ricordare in code review.

## Perché una finestra scorrevole e non una virtual list

Con 30+ righe si mostrano solo le ultime N che entrano nello spazio disponibile,
calcolate da altezza riga e altezza card. Nessuna virtualizzazione: la lista cresce di una
riga per volta, e una libreria di virtual scroll su grafica integrata costa più di quanto
risolva.

## Firme

```ts
interface DisplayRootProps {
  state: DisplayState;
  theme: ThemeTokens;
}

interface BackgroundProps {
  style: BgStyle;
  asset?: string;
  /** false quando link !== 'online': il fondo si ferma. */
  animated: boolean;
}

interface VenueLogoProps {
  name: string;
  /** SVG del locale, se caricato dal pannello. Altrimenti segnaposto disegnato. */
  mark?: string;
  tagline?: string;
  size: 'large' | 'compact';   // large = attesa, compact = tutti gli altri stati
}

interface StateLayerProps {
  phase: DisplayPhase;
  children: React.ReactNode;
}

interface AdPlaylistProps {
  slides: AdSlide[];
  /** false → nessun Ken Burns, nessuna parallasse (livelli light/eco). */
  effects: boolean;
}

interface TickerProps {
  text?: string;
  /** In pausa durante il pagamento. */
  paused: boolean;
}

interface ReceiptCardProps {
  lines: SaleLine[];
  total: Cents;
  cover: number;
  /** true nella modalità QR 'beside': tipografia ridotta, totale a 92px. */
  compact?: boolean;
  /** id dell'ultima riga arrivata: riceve l'animazione di ingresso. */
  lastId?: string;
  /** Etichetta del blocco totale. 'Da pagare' durante il pagamento. */
  totalLabel?: string;
}

interface ReceiptRowProps {
  line: SaleLine;
  /** true solo per la riga appena arrivata. */
  entering: boolean;
  compact?: boolean;
}

interface TotalBlockProps {
  value: Cents;
  label: string;
  /** Il ref che useFlip misura fra sale e payment. */
  flipRef?: React.Ref<HTMLDivElement>;
}

interface OdometerProps {
  /** Centesimi. Il componente formatta e spezza in cifre. */
  value: Cents;
}

interface SideSlotProps {
  promo?: { kicker: string; title: string; body?: string; image?: string };
  /** Mostrata solo se showLastItemPhoto è true e la foto esiste. */
  lastItemPhoto?: { src: string; caption: string };
}

interface QrStageProps {
  /** data URI o percorso locale. */
  png: string;
  /** Lato del QR in px. Il componente aggiunge la quiet zone di 4 moduli. */
  sizePx: number;
  label: string;          // aria-label
  caption?: string;       // didascalia sotto, solo nello stato Grazie
}

interface WaitingHaloProps {
  /** Deve combaciare col raggio della card del QR. */
  radiusPx: number;
  sizePx: number;
  /** false nei livelli eco e con prefers-reduced-motion. */
  active: boolean;
}

interface MessageBandProps {
  message?: CashierMessage;
}

interface LinkIndicatorProps {
  link: LinkState;   // la pillola compare solo per 'stale' e 'lost'
}

interface BluetechFooterProps {
  variant: 'light' | 'dark';   // l'unica scelta che il tema ha qui
  /** 'center' solo su portrait. */
  align: 'end' | 'center';
}
```

## Hook

```ts
/** FLIP sull'elemento condiviso. Ritorna subito nei livelli light ed eco. */
function useFlip(ref: React.RefObject<HTMLElement>, deps: unknown[]): void;

/** Rapporto d'aspetto → variante + chiave di formato per data-format. */
function useFormat(): { layout: LayoutVariant; format: FormatKey };

/** Contatore fps. Chiama onDegrade sotto 45 fps per 3 s consecutivi. */
function useFpsWatch(onDegrade: (next: MotionLevel) => void): void;

/** Quante righe entrano nello spazio disponibile della card. */
function useVisibleRows(cardRef: React.RefObject<HTMLElement>, rowH: number): number;

/** Stato dal trasporto. Applica le regole di robustezza di SPEC §12. */
function useDisplayState(transport: Transport): DisplayState;
```
