# Bluetech Display — monitor cliente per Passepartout Menu

Nuova generazione del "monitor secondario" della cassa: legge lo stesso file di scambio di Passepartout Menu
dell'attuale `MonitorCliente.exe`, ma mostra scontrino, pubblicità e QR Satispay in un'interfaccia web moderna,
personalizzabile con il brand del locale e sempre firmata "powered by Bluetech".

| Documento | Contenuto |
|---|---|
| [docs/01-analisi-monitorcliente.md](docs/01-analisi-monitorcliente.md) | Reverse engineering completo del monitor attuale: protocollo del file, regole di parsing, configurazione, bug |
| [docs/02-piano-azione.md](docs/02-piano-azione.md) | Architettura, fasi, rischi, decisioni aperte |
| [docs/claude-design-brief.md](docs/claude-design-brief.md) | **Il brief da dare a Claude Design** (file da allegare o incollare) |
| [docs/03-prompt-claude-design.md](docs/03-prompt-claude-design.md) | Cosa allegare al brief e come gestire le iterazioni |
| [docs/reference/](docs/reference/) | Configurazione e modelli XML del monitor (a 30 colonne, quello originale; a 48, in uso dal 20/10) |
| [samples/](samples/) | File reali scritti dalla cassa, usati nei test |

## Componenti

| Cartella | Stato |
|---|---|
| [`player/`](player/README.md) | **Bluetech Display Player** — WebView2 a schermo intero sul monitor secondario, avvio automatico, configurazione protetta da codice. Pronto per il collaudo |
| [`tools/`](tools/README.md) | **Bluetech Recorder** — registra ogni versione di `Scontrino.txt` per la Fase 0. Pronto |
| [`agent/`](agent/README.md) | **Bluetech Display Agent** — servizio Windows che legge il file di Passepartout di una o più casse, lo toglie appena letto e manda ai display solo quello che cambia, via WebSocket. Back office con password (impostazioni in JSON, statistiche, log), aspetto e promozioni con l'anteprima, dispositivi autorizzati con un codice, HTTPS opzionale, replay delle registrazioni. Lo schermo del cliente a tema è in `/cliente`. Pronto per la prova in cassa |
| `display/` | Interfaccia web del monitor (il display del design) — da fare (Fase 3, M2–M5). Il pannello del locale è nel back office dell'Agent |
