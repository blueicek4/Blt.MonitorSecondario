# Ordine di implementazione

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
