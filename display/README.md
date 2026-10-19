# Bluetech Display — interfaccia del monitor cliente

App web (Vite + React + TypeScript) che il Player mostra a schermo intero sul monitor della cassa.
Il design è in [`../design/handoff/`](../design/handoff/README.md) (fonte autoritativa: `SPEC.md`, `MOTION.md`,
`src/tokens/*`); come si collega alla cassa reale è in [`../design/ALLINEAMENTO.md`](../design/ALLINEAMENTO.md).

## Stato

| Milestone | Contenuto                                                                                      | Stato                                  |
| --------- | ---------------------------------------------------------------------------------------------- | -------------------------------------- |
| M0        | Scaffolding, font locali, token del pacchetto                                                  | fatto                                  |
| M1        | `ThemeProvider`, `useFormat`, validazione del contrasto, cassa finta (`src/transport/mock.ts`) | fatto                                  |
| M2–M4     | I sei stati, motore dello scontrino, QR                                                        | da fare                                |
| M5        | Trasporto reale (WebSocket dall'Agent)                                                         | dopo il giro in cassa col registratore |

Per ora l'app mostra il **banco di prova di M1**: cambia tema, modo, animazioni e QR, comanda la cassa finta e
mostra i messaggi `CashIn` che arrivano. Da M2 lascia il posto alle schermate vere.

## Comandi

```bash
npm ci
npm run dev            # http://localhost:5173
npm test               # vitest
npm run lint           # eslint (vieta alert/confirm/prompt)
npm run typecheck
npm run build          # dist/
npm run check:bundle   # dopo la build: niente indirizzi esterni, font presenti, niente alert
```

Node 22.12 o successivo.

## Regole del pacchetto che il codice già fa rispettare

- Importi **interi in centesimi**, formattati solo in uscita (`src/money/money.ts`): `€ 18,90`, meno tipografico `−`.
- Tema applicato **solo con variabili CSS** sul nodo radice: cambiarlo non tocca il layout.
- Contrasto **≥ 4,5:1** su testo/fondo e testo/primario, verificato all'avvio: sotto soglia si ricade su
  `bluetech-notte` e lo si scrive nel log.
- Variante di layout dal **rapporto d'aspetto** (`wide` / `narrow` / `portrait`), tipografia da tabella per formato:
  niente `vw`/`vh` (c'è un test).
- `prefers-reduced-motion` forza il livello `eco` (in `tokens.css`).
- **Font serviti in locale** da `public/fonts/` (OFL, vedi `public/fonts/licenze/`): zero richieste esterne,
  verificato da `check:bundle` e in Chromium.

## Note tecniche

- `.npmrc` ha `legacy-peer-deps=true`: npm 10 va in errore ("edgesOut") risolvendo le dipendenze peer opzionali di
  vitest 5 (`@vitest/browser-*`, che non usiamo). Le peer necessarie sono esplicite in `devDependencies`.
- TypeScript 6.0: typescript-eslint non supporta ancora TypeScript 7.
- Build per Chromium 109+ (l'ultimo per Windows 7/8.1): il Player usa WebView2, che è più recente.
- I file copiati dal pacchetto (`src/types/protocol.ts`, `src/tokens/themes.ts`, `src/tokens/tokens.css`) non vanno
  modificati: sono esclusi da Prettier per restare identici all'originale.
