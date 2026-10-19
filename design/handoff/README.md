# Bluetech Display — pacchetto di handoff

Monitor rivolto al cliente collegato alla cassa, per i locali che usano Passepartout Menu.
Design completo, pronto da implementare. Stack deciso: repo nuovo, Vite + React +
TypeScript.

## Come usarlo

1. Crea il repo, copia questi file dentro mantenendo la struttura.
2. Apri una sessione Claude Code nel repo. Leggerà `CLAUDE.md` da sé.
3. Primo prompt: *"Leggi CLAUDE.md, SPEC.md e TASKS.md, poi esegui M0 e M1. Fermati
   prima di M2 e mostrami cosa hai fatto."*

## Indice

| File | Cosa contiene |
|---|---|
| `CLAUDE.md` | Istruzioni per l'agente: invarianti non negoziabili, cosa non fare, segnaposto da sostituire, **le quattro domande aperte** |
| `SPEC.md` | Specifica funzionale: i sei stati, le righe dello scontrino, il totale, il QR, i token, la tipografia, le varianti di layout, accessibilità, kiosk |
| `MOTION.md` | Curve, durate e coreografie; i tre livelli di animazione; igiene prestazionale |
| `TASKS.md` | Sette milestone con criteri di accettazione verificabili |
| `src/types/protocol.ts` | Tipi del protocollo cassa → monitor, stato dello schermo, token del tema. **Proposta, non protocollo reale** |
| `src/tokens/themes.ts` | Le otto palette (4 temi × giorno/notte) con i rapporti di contrasto calcolati |
| `src/tokens/tokens.css` | Curve, durate, scala tipografica per formato, keyframes, livelli di animazione |
| `src/components/README.md` | Albero dei componenti con le firme delle props e gli hook |
| `design-reference/` | I sorgenti dei 23 artboard + il loro `README.md`. Ogni misura, colore e durata scritti letteralmente. Da leggere, non da eseguire |

## Riferimento visivo

Sta tutto in `design-reference/`, offline, insieme alle istruzioni per leggerlo. Non serve
accesso a internet né al canvas.

Contiene: pagina del sistema di design, specifica motion, note di sviluppo, prototipo
interattivo con la macchina a stati completa in JS, i sei stati a 1920×1080 in notte e
giorno, le due modalità del QR, i sei stati a 1024×768, le verifiche a 1366×768 e
1280×800, due schermate verticali 1080×1920.

Lo stesso canvas navigabile, se serve guardarlo invece di leggerlo:
https://claude.ai/artifact/1Vqr5fioAwP73svEAoiMrD

Quando un numero nei sorgenti e un numero in questo pacchetto non coincidono, **vince il
pacchetto**: `src/tokens/*` e `SPEC.md` sono la fonte autoritativa.

## Cosa NON c'è dentro, di proposito

- **Nessun componente implementato.** Firme delle props sì, file stub no.
- **Nessun file di font.** Otto famiglie open source da scaricare in M0, elencate in
  `CLAUDE.md`.
- **Nessuna immagine.** Le slide pubblicitarie e le foto articolo le carica il locale.
- **Nessun `node_modules`.** Il progetto si crea con `npm create vite`.

## Le tre cose da non dimenticare

1. Il protocollo è una proposta. Le quattro domande in `CLAUDE.md` vanno risolte prima di
   scrivere il livello di trasporto (milestone M5).
2. Logo Bluetech e logo Satispay sono segnaposto deliberati. Servono gli asset ufficiali.
3. Due decisioni di prodotto restano aperte: `compactRows` per i monitor da 10″ e
   `showLastItemPhoto` come default. Entrambe documentate in `SPEC.md`.
