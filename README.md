# Bluetech Display — monitor cliente per Passepartout Menu

Nuova generazione del "monitor secondario" della cassa: legge lo stesso file di scambio di Passepartout Menu
dell'attuale `MonitorCliente.exe`, ma mostra scontrino, pubblicità e QR Satispay in un'interfaccia web moderna,
personalizzabile con il brand del locale e sempre firmata "powered by Bluetech".

| Documento | Contenuto |
|---|---|
| [docs/01-analisi-monitorcliente.md](docs/01-analisi-monitorcliente.md) | Reverse engineering completo del monitor attuale: protocollo del file, regole di parsing, configurazione, bug |
| [docs/02-piano-azione.md](docs/02-piano-azione.md) | Architettura, fasi, rischi, decisioni aperte |
| [docs/claude-design-brief.md](docs/claude-design-brief.md) | **Il brief da dare a Claude Design** (file da allegare o incollare) |
| [docs/claude-design-brief-2.md](docs/claude-design-brief-2.md) | **Il secondo brief**: tutti gli stati e i formati dello schermo, il back office, la struttura della configurazione, le richieste dei locali |
| [docs/03-prompt-claude-design.md](docs/03-prompt-claude-design.md) | Cosa allegare al brief e come gestire le iterazioni |
| [docs/reference/](docs/reference/) | Configurazione e modelli XML del monitor (a 30 colonne, quello originale; a 48, in uso dal 20/10) |
| [samples/](samples/) | File reali scritti dalla cassa, usati nei test |
| [design/](design/ALLINEAMENTO.md) | Pacchetto di handoff di Claude Design (`design/handoff/`, tale e quale) e allineamento con la cassa reale |

## Componenti

| Cartella | Stato |
|---|---|
| [`player/`](player/README.md) | **Bluetech Display Player** — WebView2 a schermo intero sul monitor secondario, avvio automatico, configurazione protetta da codice. Pronto per il collaudo |
| [`tools/`](tools/README.md) | **Bluetech Recorder** — registra ogni versione di `Scontrino.txt` per la Fase 0. Pronto |
| [`agent/`](agent/README.md) | **Bluetech Display Agent** — servizio Windows che legge il file di Passepartout di una o più casse, lo toglie appena letto e manda agli schermi solo quello che cambia, via WebSocket. Configurazione v3 del design (installazione, marchi, casse, schermi), back office con due ruoli (titolare e tecnico), anteprima fissa e primo avvio guidato: il locale con dieci temi, vetrine con regole riusabili, contenuti, schermi abbinati con un codice, statistiche, installazione. Modalità demo per provare lo scontrino digitale e la copia di carta senza la cassa. HTTPS opzionale, replay delle registrazioni. Pronto per la prova in cassa |
| `/cliente` (nell'Agent) | **Lo schermo del cliente** dell'iterazione 2 del design: sei formati, quattro livelli di attrazione, le sette entrate delle righe, il totale a contachilometri, le vetrine nei tre posti, lo scontrino digitale a sei posti. Il codice React del pacchetto del design è il riferimento; la pagina è HTML e JavaScript senza dipendenze, dentro l'exe |
| [`display/`](display/README.md) | **Interfaccia web del monitor** (Vite + React + TypeScript) — M0 e M1 dell'iterazione 1 (token, temi, formati, cassa finta). L'iterazione 2 del design è già nello schermo `/cliente` dell'Agent; il suo pacchetto è in [`design/handoff-it2`](design/handoff-it2/README.md) |
