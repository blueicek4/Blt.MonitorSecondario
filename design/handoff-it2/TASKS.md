# Ordine dei lavori — iterazione 2

Il prodotto gira. Queste milestone lo cambiano un pezzo alla volta, ognuna rilasciabile
da sola. Le milestone dell'iterazione 1 (M0–M6) sono in fondo, per riferimento.

Regola per tutte: **prima di cambiare un comportamento visibile, fai uno screenshot dello
stato attuale nei sei formati** e tienilo accanto. Ogni milestone si chiude con gli stessi
screenshot dopo.

## I1 — Temi e token

- Sostituisci le otto palette con `src/tokens/themes.ts` (dieci temi).
- Migra i valori salvati con `LEGACY_THEME`; logga ogni migrazione.
- `resolveTheme` per il modo `auto`; Notturno e Latte restano fissi.
- Aggiungi a `tokens.css` le sezioni [IT2]. Togli `--receipt-w`.
- **Fatto quando**: i dieci temi girano sul prototipo locale in tutti i formati, e un test
  ricalcola con `contrastRatio` tutte le coppie dei commenti e fallisce sotto 4,5.

## I2 — Griglia e card

- `colonneConto`, `colonneContoPagamento`, `colonnaAllineamento` con `flex-grow`.
- Card ad altezza contenuto, ancorata in basso.
- Fermi 9–17 / 8–14; massimo 16 a 1366 e 1280.
- **Fatto quando**: un conto da 3 righe non lascia card vuota in nessun formato.
- A 4:3 la colonna c'è, solo testo (deciso). A Sobrio non si monta.

## I3 — Attrazione e movimento effettivo

- `data-attr` e calcolo di `data-motion` come minimo (MOTION.md).
- A Sobrio la colonna non si monta.
- Rotazione della colonna con rinvio di 1,5 s durante le battute.
- **Fatto quando**: TROPPO + `eco` mostra le stesse reclame di TROPPO + `full`, ferme.

## I4 — Le sette righe e l'accorpamento

- `view: DisplayLine[]` derivata da `lines` con l'accorpamento (SPEC §3.2).
- Le sette coreografie; il collasso dello storno con FLIP, non con `height`.
- Prezzi solo sul conto con la cascata.
- **Fatto quando**: un test batte articolo, stesso articolo, variante, sconto,
  maggiorazione, storno di uno degli accorpati, e la vista ha le quantità giuste.
- **Da decidere prima**: domanda aperta 3 (display o Agent).

## I5 — Reclame

- Modelli A–D, punto di messa a fuoco, fasce sul modello B nei formati stretti.
- Transizioni per coppia; "un QR ogni N"; QR 15 s; video a durata nativa.
- Nastro con maschera ai bordi.
- Fondo a cinque strati; derivati del video generati dall'Agent al caricamento.

## I6 — Grazie e scontrino digitale

- Grazie con riepilogo in due righe, reclame dopo la celebrazione.
- Stato `receipt`, tre blocchi sui sei posti della griglia, 45 s; trasposizione su
  verticale e palmare; caduta reclame → carta quando manca spazio.
- Editor a sei posti nel pannello, con lo scambio e l'anteprima su ogni schermo abbinato.
- Pulsante copia di carta con i quattro stati, solo touch + stampante.
- **Bloccata su**: domande aperte 1 e 2.

## I7 — Configurazione v3

- Tipi di `config.ts`, migrazione v2 → v3 con `.bak` e log.
- Risoluzione dell'eredità nell'Agent → `ScreenConfig` al display.
- `ScegliVetrina` pura, con test sulla catena dei quattro passi e sui cinque stati.

## I8 — Back office

- Sei sezioni + Installazione per ruolo; anteprima a colonna fissa.
- Valori ereditati con "riporta al valore del locale".
- Controllo contrasto prima di salvare, con proposta di correzione.
- Primo avvio in cinque passi; layout a 390 px.
- Pagine del dispositivo (SPEC §21).

## Prima del rilascio dell'iterazione 2

- I sei formati × almeno un tema chiaro e uno scuro × i quattro livelli di attrazione,
  con screenshot. Il prototipo `iterazione-2/Prototipo2.dc.html` ha tutte le leve.
- Un conto da 30 righe a 1024×768 su un 10" vero, letto da 70 cm.
- Un video bianco come fondo: il conto deve restare sopra 4,5:1.
- Una cassa lenta in `eco` per un'ora: nessun calo sotto 45 fps negli stati con testo.

---

# Appendice — milestone dell'iterazione 1


Sette milestone. Ognuna ha criteri di accettazione verificabili: non passare alla
successiva finché quelli della precedente non sono tutti verdi.

**M5 è bloccata** dalle quattro domande aperte in `CLAUDE.md`. Tutto il resto si può
fare adesso, contro il mock.

---

## M0 — Scaffolding

- `npm create vite@latest bluetech-display -- --template react-ts`
- Dipendenze: solo `react`, `react-dom`. Dev: `typescript`, `vite`,
  `@vitejs/plugin-react`, `eslint`, `prettier`, `vitest`.
  **Nessuna libreria di animazione, CSS framework o state management.**
- `tsconfig`: `strict: true`, `noUncheckedIndexedAccess: true`.
- Copia i file del pacchetto: `src/types/protocol.ts`, `src/tokens/themes.ts`,
  `src/tokens/tokens.css`. Importa `tokens.css` in `main.tsx` prima di tutto il resto.
- Scarica i font come woff2 in `public/fonts/` e scrivi le `@font-face` locali:
  Archivo, IBM Plex Sans, Newsreader, Alegreya Sans, Cormorant Garamond, Jost, Syne,
  Space Grotesk. **Niente Google Fonts a runtime**: il monitor deve partire identico col
  router staccato.

**Accettazione**: `npm run build` passa, la pagina bianca carica i font da locale
(verificabile in devtools → Network, zero richieste esterne).

---

## M1 — Token, tema, formato

- `ThemeProvider`: prende `ThemeTokens`, risolve `mode: 'auto'` sull'orario, applica
  `paletteToCssVars()` sul root più gli attributi `data-motion`, `data-format`,
  `data-layout`, `data-qr`, `data-compact-rows`.
- `useFormat()`: rapporto d'aspetto → `LayoutVariant` + `FormatKey`. Su `resize` e
  `orientationchange`.
- Validazione del tema all'avvio: contrasto di `cText` su `cBg` e `cOnPrimary` su
  `cPrimary`. Sotto 4,5:1 logga e ricade sul set `bluetech-notte`.
- `src/transport/mock.ts`: un `Transport` che emette una vendita realistica a comando.
  Riusa i comandi del prototipo nel canvas: aggiungi riga, sconto, storno, testo libero,
  messaggio, QR, fine vendita, offline.

**Accettazione**: cambiando `theme`/`mode` nel provider cambia tutto lo schermo e niente
layout. Test unitari: 8 palette × 2 coppie critiche, tutte ≥ 4,5:1. `useFormat` ritorna
`narrow` per 1024×768 e `portrait` per 1080×1920.

---

## M2 — I sei stati, statici, a 1920×1080

Tradurre gli artboard del canvas, senza animazioni e senza dati veri: stato finto
hard-coded.

Ordine: `IdleScreen`, `SaleScreen`, `MessageBand`, `PaymentScreen` (`fullscreen`),
`ThanksScreen`, `LinkIndicator`. `BluetechFooter` per primo, perché è l'unico che non
dipende dalla fase.

**Accettazione**: i sei stati a 1920×1080 combaciano con gli artboard. Il footer Bluetech
è a schermo in tutti e sei, nelle quattro palette e in entrambe le varianti. Il totale è
l'elemento più grande in tutti gli stati che ce l'hanno.

---

## M3 — Motore dello scontrino

- `ReceiptRow` con le sei varianti (`SPEC.md` §3). `key = line.id`.
- `useVisibleRows()` + finestra scorrevole con gradiente in testa e "altre N righe sopra".
- `Odometer` (`SPEC.md` §4): solo le cifre che cambiano si muovono.
- Animazione di ingresso riga, con i tre livelli.
- Storno: la riga resta 6 s poi esce dalla finestra.

**Accettazione**: con 40 righe il totale resta sempre visibile e la lista mostra le
ultime N. Da 17,40 a 18,90 si muovono solo unità e decimi (verificabile rallentando le
animazioni in devtools). Con `data-motion="eco"` nessun `transform` animato e la riga
entra solo in opacità. `prefers-reduced-motion` forza eco anche con
`data-motion="full"`.

---

## M4 — QR e le due modalità

- `QrStage`: fondo bianco, quiet zone 4 moduli, `image-rendering: pixelated`, lato
  multiplo intero dei moduli, mai ruotato né scalato in modo non intero.
- `WaitingHalo`: anelli **attorno** alla card, non sopra.
- `qrLayout: 'beside'` su `wide`: scontrino da 1180 → 760 px, tipografia ridotta, totale a
  92 px con etichetta "Da pagare", QR a 488 px.
- Su `narrow` e `portrait` il token è ignorato: sempre `fullscreen`.
- Transizione di stato con FLIP sul totale (`MOTION.md`).

**Accettazione**: in `beside` il QR misura 488 px e lo scontrino 760. In `narrow` passando
`qrLayout: 'beside'` si ottiene comunque il full screen. Il lato del QR è ≥ 1/3
dell'altezza schermo in tutti e cinque i formati. Nessun elemento, gradiente o blur si
sovrappone al QR (verificabile con un controllo sugli z-index in test).

---

## M5 — Trasporto reale e resilienza — BLOCCATA

Non iniziare prima delle quattro risposte in `CLAUDE.md`.

- Implementare `Transport` contro il meccanismo reale della cassa.
- Se il flusso è una ristampa completa invece di un delta: diff lato monitor per
  identificare la riga nuova, altrimenti l'animazione di ingresso scatta su tutte.
- `link`: `online` → `stale` dopo 8 s di silenzio → `lost` alla caduta del socket.
  Backoff 1-2-4-8-15 s. `requestSync()` alla ripresa.
- Messaggi fuori sequenza: `sale.line` senza `sale.open` apre implicitamente la vendita.
  Messaggio sconosciuto: logga e ignora.
- Anello degli ultimi 200 messaggi, interrogabile dalla cassa.

**Accettazione**: staccando il cavo di rete l'ultima schermata resta identica e immobile,
la pillola compare dopo 8 s, e alla riconnessione lo schermo riprende dallo stato
completo. Nessun `alert`/`confirm` in tutto il bundle (grep in CI).

---

## M6 — Formati, kiosk, degrado

- Varianti `narrow` e `portrait` complete (`SPEC.md` §9).
- Verifica su tutti e cinque i formati.
- `useFpsWatch`: sotto 45 fps per 3 s scala al livello inferiore e logga. **Non risale
  da sé.**
- Script di avvio Chromium `--kiosk --app` sul display secondario, con riavvio pagina se
  il socket non torna in 60 s.

**Accettazione**: i cinque formati non hanno overflow né testo tagliato. Su una VM senza
accelerazione il degrado scatta e resta. Il footer Bluetech è leggibile e al suo posto in
tutti e cinque i formati (centrato su `portrait`).

---

## Prima del rilascio

- [ ] Logo Bluetech ufficiale al posto del segnaposto, due varianti
- [ ] Logo Satispay dal brand kit, non ridisegnato
- [ ] Le quattro domande aperte risolte e il protocollo allineato alla realtà
- [ ] Decisione su `compactRows` per i monitor da 10″ (`SPEC.md` §7)
- [ ] Decisione su `showLastItemPhoto` come default
- [ ] Test di contrasto in CI sulle otto palette
- [ ] Grep in CI: nessun `alert|confirm|prompt`, nessun `vw`/`vh` nella tipografia,
      nessun `transition` su proprietà diverse da `transform`/`opacity`
