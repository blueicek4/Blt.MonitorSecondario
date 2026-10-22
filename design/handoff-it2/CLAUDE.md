# Bluetech Display — istruzioni per l'agente

Stai lavorando sul monitor rivolto al cliente collegato alla cassa, per i locali che
usano Passepartout Menu. Il design è già fatto: il tuo compito è tradurlo, non
riprogettarlo.

> **Iterazione 2.** Il prodotto **esiste e gira**: questo pacchetto descrive come
> cambiarlo, non come rifarlo. Lavora **nel repo esistente**. Prima di toccare qualunque
> cosa leggi `DELTA.md`: è l'elenco di ogni cambio rispetto all'iterazione 1, con il
> motivo e il punto della specifica. Se il codice in esercizio risolve già un punto in
> modo diverso ma equivalente, **tieni il codice** e segnalalo, non riscriverlo per
> farlo somigliare al pacchetto.

**Stack**: Vite + React + TypeScript. Nessun framework CSS, nessuna libreria di
animazione, nessuna libreria di state management. L'ordine dei lavori è in `TASKS.md`.

## Prima di scrivere codice

1. Leggi `DELTA.md`. Poi `SPEC.md` per intero: le parti nuove sono marcate [IT2].
2. Leggi `MOTION.md` prima di toccare qualunque animazione.
3. Il riferimento visivo è in `design-reference/`, in due cartelle: `iterazione-1/`
   (23 artboard) e `iterazione-2/` (39 artboard). Leggi `design-reference/README.md`.
   Non vanno copiati nel progetto e non si aprono nel browser così come sono.
   I più utili: `iterazione-2/Prototipo2.dc.html` (macchina a stati con i dieci temi,
   quattro livelli di attrazione, quattro disposizioni, larghezza del conto) e
   `iterazione-2/F-Regole.dc.html` (cosa fa ogni stato su ogni formato).
   I due canvas navigabili: iterazione 1 https://claude.ai/artifact/1Vqr5fioAwP73svEAoiMrD,
   iterazione 2 https://claude.ai/artifact/HQk2tRopxx1ehLxideLTHR (serve l'account
   Bluetech: se non ce l'hai, usa i sorgenti).
   Quando un numero nei sorgenti e uno nel pacchetto non coincidono, **vince il
   pacchetto**: `src/` e `SPEC.md` sono la fonte autoritativa. Quando l'iterazione 1 e
   la 2 non coincidono, vince la 2.
4. I file in `src/` sono scritti e passano `tsc --strict`. Se il repo ha già file con lo
   stesso ruolo, **non sovrascriverli**: confrontali e porta dentro solo le parti [IT2].
   `themes.ts` invece si sostituisce: i dieci temi rimpiazzano le otto palette, e
   `LEGACY_THEME` migra i valori salvati.

## Invarianti — non negoziabili

Queste regole non sono preferenze estetiche: sono il prodotto. Se un refactoring le
rompe, il refactoring è sbagliato.

- **Il footer Bluetech è sempre a schermo**, in ogni stato e con ogni tema. Vive
  **fuori** da `StateLayer`, così nessuna transizione lo tocca e nessun tema può
  nasconderlo. Il tema del locale ne scegli solo la variante chiara o scura. Altezza
  minima del wordmark 15 px, contrasto minimo 4,5:1.
- **Il totale è l'elemento più grande dello schermo**, sempre. Mai sotto 120 px per 1080
  del **lato corto**. Due deroghe dichiarate, e solo queste [IT2]: prima che il totale
  esista con `prezziSoloSulConto` (SPEC §2.2.1), e nello stato `receipt`, dove domina il
  QR (SPEC §2.7).
- **Gli importi sono interi in centesimi.** Mai `number` decimale, mai `parseFloat` su
  una stringa della cassa. Formattazione solo in output.
- **Il totale arriva dalla cassa, non si somma lato monitor.** Se la somma delle righe
  non torna, vince la cassa e si logga la discrepanza. La cassa è la fonte fiscale.
- **Non esiste uno stato "pagamento riuscito".** La cassa non lo comunica in modo
  affidabile. Niente spunte, niente countdown, niente messaggi di successo: lo stato
  `payment` finisce solo con `pay.clear`, `sale.close` o un altro messaggio.
- **Il QR non si anima, non si deforma, non si copre.** Si anima il suo contenitore.
  Fondo bianco puro, quiet zone 4 moduli, `image-rendering: pixelated`, lato minimo
  1/3 dell'altezza schermo.
- **Si animano solo `transform` e `opacity`.** Mai `width`, `top`, `box-shadow`,
  `filter`, `background-position`.
- **`prefers-reduced-motion: reduce` forza il livello `eco`**, qualunque cosa dica il
  token del locale.
- **L'area messaggi spinge la lista, non la copre.**
- **Perdita di connessione = l'ultima schermata resta esattamente dov'era.** Nessuno
  svuotamento, nessuna schermata d'errore, nessun ritorno all'attesa. Solo una
  pillola grigia discreta in basso a sinistra, dopo 8 s di silenzio. Grigia, non rossa:
  il cliente non deve preoccuparsi.
- **Nessun `alert`, `confirm` o `prompt`.** In kiosk un dialogo blocca lo schermo e in
  sala nessuno sa cosa fare.
- **[IT2] L'attrazione cambia quanto si vende, mai dove stanno le cose.** Nessun livello
  sposta il totale, copre lo scontrino o tocca il footer. TROPPO su una macchina lenta
  mostra le stesse reclame, ferme (MOTION.md, "vince il più basso").
- **[IT2] Un solo pulsante vero** in tutto il display: la copia di carta, solo su schermi
  touch con una stampante assegnata. Una richiesta per vendita, nessuna conferma.
- **[IT2] L'eredità della configurazione la risolve l'Agent.** Il display riceve una
  `ScreenConfig` piatta e non sa da quale livello viene un valore.
- **[IT2] La scelta della vetrina è una funzione pura sola** (`ScegliVetrina`), usata
  dall'Agent per decidere e dal pannello per spiegare. Due copie divergono.
- **[IT2] L'accorpamento delle righe è solo vista.** `lines` resta come arriva dalla
  cassa; il totale resta quello della cassa.
- **[IT2] Il QR non si anima nemmeno nelle slide**: entra in sola opacità, per ultimo.

## Cosa non fare

- Non aggiungere Framer Motion, GSAP, react-spring o simili. Tutte le animazioni sono
  CSS: il PC cassa peggiore della base installata non regge un runtime JS che anima.
- Non usare `vw`/`vh` per la tipografia. Le dimensioni vengono da una tabella discreta
  per formato (`src/tokens/tokens.css`). Con `vw` il totale su 1024×768 diventa più
  piccolo che su 1280×800, che è l'opposto di quello che serve.
- Non virtualizzare la lista delle righe. Si mostra una finestra delle ultime N righe
  che entrano nello spazio disponibile. Una virtual list costa più di quanto risolva.
- Non usare l'indice come `key` delle righe: con l'indice l'animazione di ingresso
  scatta sulla riga sbagliata. La `key` è `line.id`.
- Non spostare il footer Bluetech dentro i layer di transizione.
- Non inventare campi del protocollo oltre a quelli in `src/types/protocol.ts` senza
  prima risolvere le domande aperte qui sotto.
- Non scalare il layout: ogni variante di formato ha la sua struttura. Vedi SPEC §9.

## Segnaposto da sostituire

Il design contiene tre segnaposto deliberati. Non reimplementarli, non disegnarli:
chiedi gli asset.

1. **Marchio Bluetech** — nel design è un glifo geometrico. Serve l'SVG ufficiale nelle
   due varianti, chiara e scura.
2. **Logo Satispay** — nel design è un riquadro tratteggiato con proporzioni e
   clearspace corrette. Va preso dal brand kit Satispay e usato secondo le loro linee
   guida. **Non ridisegnarlo.**
3. **Immagini pubblicitarie e foto articolo** — segnaposto disegnati con le proporzioni
   reali. Le carica il locale dal pannello.
4. **File dei font** — il pacchetto non li contiene. **[IT2] Quattordici** famiglie open
   source (SIL OFL), tutte incorporabili: Archivo, IBM Plex Sans, Oswald, Syne, Space
   Grotesk, Cormorant Garamond, Jost, Spectral, Karla, Newsreader, Alegreya Sans, Lora,
   Work Sans, Outfit. L'Agent le tiene tutte, così il tema si cambia senza rete. Vanno scaricati come woff2 e
   serviti da `public/fonts/` (task M0). Se la rete della macchina di sviluppo non li
   raggiunge, chiedili a Gianluca invece di sostituire le famiglie: i fallback di sistema
   nei token hanno metriche vicine ma la resa non è la stessa.

## [IT2] Domande aperte dell'iterazione 2 — prima di I5 e I6

1. **`receipt.show`**: chi impagina la copia di cortesia (proposta: l'Agent, righe a
   larghezza fissa) e con che QR (link a cosa, con che scadenza).
2. **Copia di carta**: il canale display → Agent esiste già per qualcos'altro, o
   `DisplayOut` è il primo messaggio in uscita? Se è il primo, va sullo stesso socket.
3. **Accorpamento**: se l'Agent fa già il diff delle ristampe, l'accorpamento può stare
   lì invece che nel display. Il risultato a schermo deve essere lo stesso.
4. ~~Colonna a 4:3~~ — **decisa**: si fa (solo testo). A Sobrio resta come oggi.
5. ~~Posizioni dello scontrino digitale~~ — **decisa**: ogni blocco in uno dei sei posti
   (SPEC §2.7). Niente coordinate libere.

## Domande aperte dell'iterazione 1 — verificare se il repo le ha già risolte

Il protocollo in `src/types/protocol.ts` è una **proposta ragionata, non una lettura
del comportamento reale della cassa**. Non scrivere il livello di trasporto prima di
avere queste quattro risposte:

1. Come la cassa espone i dati al monitor oggi: socket, HTTP locale, file su disco, o
   il protocollo del visore seriale incapsulato.
2. Se il flusso delle righe è un vero delta oppure una ristampa completa dello
   scontrino a ogni battuta. **Da questa dipende tutta la logica di animazione di
   ingresso**: con una ristampa completa serve un diff lato monitor per capire quale
   riga è nuova.
3. Se le foto articolo esistono già nell'anagrafica e con che chiave si risolvono.
4. Se esiste un evento affidabile di fine vendita distinto dall'arrivo del totale. Da
   questo dipende se lo stato Grazie si può mostrare sempre o solo quando la cassa lo
   dice.

Fino a quel momento lavora contro il mock in `src/transport/mock.ts` (da scrivere in
M1) e tieni il livello di trasporto dietro un'interfaccia sola, così sostituirlo costa
un file.
