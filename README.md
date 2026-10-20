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
| [design/](design/ALLINEAMENTO.md) | Pacchetto di handoff di Claude Design (`design/handoff/`, tale e quale) e allineamento con la cassa reale |

## Componenti

| Cartella | Stato |
|---|---|
| [`player/`](player/README.md) | **Bluetech Display Player** — WebView2 a schermo intero sul monitor secondario, avvio automatico, configurazione protetta da codice. Pronto per il collaudo |
| [`tools/`](tools/README.md) | **Bluetech Recorder** — registra ogni versione di `Scontrino.txt` per la Fase 0. Pronto |
| `agent/` | Servizio Windows che legge `Scontrino.txt` e pubblica lo stato in tempo reale — da fare (Fase 1) |
| [`display/`](display/README.md) | **Interfaccia web del monitor** (Vite + React + TypeScript) — M0 e M1 fatti: token, temi, formati, cassa finta |
