# Bluetech Display — pacchetto di handoff, iterazione 2

Monitor rivolto al cliente collegato alla cassa, per i locali che usano Passepartout Menu.
Questa è la **seconda iterazione**: parte dal prodotto in esercizio (81 schermate) e
descrive come cambiarlo. Non è un progetto da rifare da zero.

## Come usarlo

1. Copia questa cartella **dentro il repo esistente**, in `docs/handoff-it2/` (non nella
   radice: il repo ha già il suo `CLAUDE.md`, e i file in `src/` vanno confrontati, non
   sovrascritti).
2. Apri Claude Code nel repo. Primo prompt:
   *"Leggi docs/handoff-it2/CLAUDE.md e DELTA.md. Poi confronta src/ del pacchetto con il
   codice del repo e dimmi, file per file, cosa c'è già, cosa manca e cosa è in conflitto.
   Non modificare niente finché non ti rispondo."*
3. Poi una milestone alla volta da `TASKS.md` (I1 → I8), con gli screenshot prima/dopo.

## Indice

| File | Cosa contiene |
|---|---|
| `DELTA.md` | **Da leggere per primo.** Ogni cambio rispetto all'iterazione 1, con il motivo e il punto della specifica |
| `CLAUDE.md` | Istruzioni per l'agente: invarianti (con le aggiunte [IT2]), cosa non fare, segnaposto, domande aperte |
| `SPEC.md` | Specifica funzionale completa, 21 sezioni; le parti nuove marcate [IT2] |
| `MOTION.md` | Curve, durate, coreografie; le sette righe; attrazione × livelli |
| `TASKS.md` | Otto milestone I1–I8 con criteri di chiusura; in appendice M0–M6 dell'iterazione 1 |
| `src/tokens/themes.ts` | I dieci temi, contrasti calcolati da script, migrazione dai vecchi valori |
| `src/tokens/tokens.css` | Token non di colore: curve, durate, scala per formato (ora anche palmare), griglia, attrazione, scrim, receipt |
| `src/types/protocol.ts` | Protocollo Agent → display e stato dello schermo, esteso [IT2]. **Proposta** |
| `src/types/config.ts` | `impostazioni.json` v3 e la firma della funzione che sceglie la vetrina |
| `src/components/README.md` | Albero dei componenti dell'iterazione 1 (ancora valido; i componenti nuovi sono descritti in SPEC) |
| `design-reference/` | I sorgenti degli artboard: 23 dell'iterazione 1, 39 dell'iterazione 2 |
| `storico/iterazione-1/` | Il pacchetto del primo giro com'era, solo per tracciabilità: non è più la fonte |

I file `.ts` passano `tsc --strict --noEmit`.

## Canvas navigabili

- Iterazione 1: https://claude.ai/artifact/1Vqr5fioAwP73svEAoiMrD
- Iterazione 2: https://claude.ai/artifact/HQk2tRopxx1ehLxideLTHR

Sono privati finché non li condividi dal menu Condividi. Per l'agente non servono: ha i
sorgenti in `design-reference/`.

## Le cose da decidere prima di cominciare

Già decise da Gianluca: la colonna accanto al conto **c'è anche a 4:3** (solo testo), e
nello scontrino digitale **ogni blocco va dove si vuole** sui sei posti della griglia.

Ancora aperte:

1. **Scontrino digitale**: chi impagina la copia e dove punta il QR.
2. **Copia di carta**: primo messaggio display → Agent, o canale già esistente?
3. **Accorpamento delle righe**: nel display o nell'Agent.

Dettagli in `CLAUDE.md`, sezione "Domande aperte dell'iterazione 2".

## Cosa NON c'è dentro, di proposito

- Nessun componente implementato: il codice è nel repo.
- Nessun file di font: quattordici famiglie OFL, elencate in `CLAUDE.md`.
- Nessuna immagine: le carica il locale. Loghi Bluetech e Satispay restano segnaposto.
