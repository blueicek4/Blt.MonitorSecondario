# Bluetech Display — istruzioni per l'agente

Stai implementando il monitor rivolto al cliente collegato alla cassa, per i locali che
usano Passepartout Menu. Il design è già fatto e validato: il tuo compito è tradurlo,
non riprogettarlo.

**Stack deciso**: repo nuovo, Vite + React + TypeScript. Nessun framework CSS, nessuna
libreria di animazione, nessuna libreria di state management. Vedi `TASKS.md` per
l'ordine di implementazione.

## Prima di scrivere codice

1. Leggi `SPEC.md` per intero. È la specifica funzionale: stati, layout, regole.
2. Leggi `MOTION.md` prima di toccare qualunque animazione.
3. Apri il canvas di design come riferimento visivo:
   https://claude.ai/artifact/1Vqr5fioAwP73svEAoiMrD
   Ogni stato ha il suo artboard a dimensione reale. Quando un numero nel codice e un
   numero sull'artboard non coincidono, **vince questo pacchetto**: i file
   `src/tokens/*` e `SPEC.md` sono la fonte autoritativa, gli artboard sono riferimento.
4. I file in `src/` sono già scritti e pronti: palette, token, tipi del protocollo.
   Copiali nel repo come sono. Non rigenerarli a mano.

## Invarianti — non negoziabili

Queste regole non sono preferenze estetiche: sono il prodotto. Se un refactoring le
rompe, il refactoring è sbagliato.

- **Il footer Bluetech è sempre a schermo**, in ogni stato e con ogni tema. Vive
  **fuori** da `StateLayer`, così nessuna transizione lo tocca e nessun tema può
  nasconderlo. Il tema del locale ne scegli solo la variante chiara o scura. Altezza
  minima del wordmark 15 px, contrasto minimo 4,5:1.
- **Il totale è l'elemento più grande dello schermo**, sempre. Mai sotto 120 px su
  un'altezza di 1080.
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

## Domande aperte — risolvere con Gianluca prima della milestone M5

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
