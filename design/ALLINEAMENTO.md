# Design e cassa reale: allineamento

Il pacchetto di Claude Design è in [`handoff/`](handoff/README.md), **copiato tale e quale**: `SPEC.md`, `MOTION.md`
e `src/tokens/*` restano la fonte autoritativa per l'interfaccia. Questo documento collega il design a quello che
sappiamo davvero della cassa (reverse engineering in [docs/01](../docs/01-analisi-monitorcliente.md), file reali in
[samples/](../samples/)) e dice cosa cambia nel [piano](../docs/02-piano-azione.md).

Riferimento visivo offline: i sorgenti dei 23 artboard in [`handoff/design-reference/`](handoff/design-reference/README.md)
(da leggere, non da aprire nel browser). Lo stesso canvas navigabile: <https://claude.ai/artifact/1Vqr5fioAwP73svEAoiMrD>

## 1. Le quattro domande aperte del `CLAUDE.md`

| # | Domanda | Risposta | Prova |
|---|---|---|---|
| 1 | Come la cassa espone i dati al monitor | **File su disco**: Passepartout scrive `Scontrino.txt` con il driver "stampa su file" e il modello XML `Monitor`. Nessun socket, nessun HTTP. Il trasporto verso il browser lo fa l'**Agent** (servizio Windows locale) | Reverse engineering del vecchio `MonitorCliente.exe`; modello XML in `docs/reference/` |
| 2 | Delta o ristampa completa | **Ristampa completa a ogni battuta.** Serve il diff lato monitor: è già scritto e testato (`ReceiptDiff` in `tools/`) e mette gli articoli ripetuti in fondo, così l'ingresso scatta sulla riga giusta | Tuo riscontro in cassa + `samples/` |
| 3 | Foto articolo e chiave | **Nel file non c'è un codice articolo**, solo la descrizione, per di più **troncata a 30 colonne** (`Hamburger Vecchia Malg`). Anche i comandi del modello XML (`StampaRigaArticolo`: descrizione, quantità, prezzo, reparto, IVA) non portano un codice. Una foto si può agganciare solo alla descrizione, quindi è fragile: il default del design `showLastItemPhoto: false` è giusto | `samples/scontrino-ristorante-3-righe.txt`, modello XML |
| 4 | Evento affidabile di fine vendita | **Non ancora noto.** Il vecchio monitor usa solo il tempo: 20 s senza aggiornamenti → "ARRIVEDERCI E GRAZIE". Da verificare col registratore (giro completo: pagamenti, Satispay, due conti di fila, minuto di pausa finale) | `docs/01` §3.4 |

Fino alla risposta 4 lo stato **Grazie** si apre per inattività (come oggi) e non per un evento della cassa.

## 2. Dal file al protocollo del design

L'interfaccia non legge il file: riceve i messaggi `CashIn` di `handoff/src/types/protocol.ts`, che l'Agent ricava
confrontando ogni versione del file con quella precedente.

| Nel file | Messaggio `CashIn` | Note |
|---|---|---|
| Prima versione `SNF` di un conto | `sale.open` + una `sale.line` per riga | |
| Riga nuova (dal diff) | `sale.line` `item` con **id nuovo** | Gli id li assegna l'Agent e restano stabili tra versioni grazie al diff (stessa riga = stesso id) |
| Riga sparita (dal diff) | `sale.line` `void` con `voidsId` | Il design la tiene barrata 6 s. **Da verificare**: lo storno in Passepartout toglie la riga o ne aggiunge una negativa? (giro completo, punto 3) |
| Riga con importo negativo | `discount` | Formato reale di sconti e maggiorazioni ancora da registrare (punto 4) |
| `TOT. DA PAGARE  35,50` | `sale.total` `3550` | **Centesimi**, parsing della stringa senza `parseFloat` (gestire anche `1.234,50`). Il totale viene dalla cassa, come vuole il design |
| `SUBTOT.` | ignorato | Come il vecchio monitor |
| Riga con ` x ` (dettaglio quantità) | `qty` della riga a cui si riferisce | Nei file visti finora gli articoli ripetuti sono righe separate: formato da confermare (punto 2) |
| Messaggio del visore (file senza `SNF`) | `display.msg` | Il design estrae etichetta e importo con una regex: i testi reali sono `{riga1} {riga2}` del visore (16 colonne) |
| `SNF` + `QRCODE` + riga Base64 | `pay.qr` `png: data:image/png;base64,…`, `amount` = ultimo totale | Formato dell'immagine da confermare (punto 9) |
| Qualunque file dopo un QR | `pay.clear` | È il comportamento del vecchio monitor |
| Conto nuovo | `sale.close` + `sale.open` | Criterio da definire coi dati (nessuna riga in comune? totale ripartito da zero?) |
| Agent riavviato o browser ricaricato | `sync` con lo stato completo | Il design lo prevede già per la riconnessione |

## 3. Cosa il design cambia nel nostro piano

| Tema | Piano (`docs/02`) | Design | Decisione |
|---|---|---|---|
| Animazioni | Motion/GSAP, WebGL nel profilo "Piena" | **Solo CSS**, nessuna libreria; `transform` e `opacity` | Vale il design: più leggero sui PC cassa |
| Trasporto Agent → browser | SignalR | Nessuna dipendenza oltre React | **WebSocket nativo** con i messaggi `CashIn` in JSON: niente client SignalR nel bundle |
| Repo | Monorepo (`player/`, `tools/`, `agent/`, `display/`) | "Repo nuovo" | **Stesso repo, cartella `display/`**: Agent, Player e registratore condividono protocollo e campioni reali |
| Kiosk | Player WebView2 | Chromium `--kiosk --app` | **Player WebView2**: non ruba il focus alla cassa, non copre lo schermo principale, configurazione con codice. Il riavvio pagina se la connessione non torna in 60 s lo fa la pagina stessa |
| Profili prestazionali | Piena / Leggera / Eco automatici | `full` / `light` / `eco`, degrado sotto 45 fps che non risale da solo | Vale il design |

## 4. Cose che il design non poteva sapere

- **Descrizioni troncate dalla cassa a 30 colonne** (`Gelato alla crema con`). La regola "a capo su due righe, poi
  ellissi" non entra mai in gioco finché la cassa taglia. Proposta da provare col registratore: alzare
  `NumeroColonne` nel modello XML del monitor (per esempio 48). È il modello del monitor, non di una stampante: ma
  attenzione al vecchio monitor, che con righe più lunghe cambierebbe aspetto.
- **Maiuscole e minuscole** arrivano come sono scritte in anagrafica (`Tagliata Al Rosmarino`): coerente con "mai
  maiuscolo forzato".
- **Coperti**: `sale.total.cover` non ha ancora una fonte. Se il conto ha una riga "Coperto", l'Agent li ricava da lì.
- **Passepartout potrebbe non sovrascrivere il file** (`SovrascriviSeEsistente=false`): in quel caso l'Agent deve
  "consumarlo" dopo averlo letto, e non può convivere col vecchio monitor sullo stesso file. Primo giro in cassa:
  una sola versione registrata; in verifica con `/consuma`.

## 5. Asset da recuperare (dal pacchetto)

- Logo Bluetech ufficiale in SVG, variante chiara e scura.
- Logo Satispay dal brand kit ufficiale, da usare secondo le loro linee guida.

## 6. Prossimi passi

1. **Subito, contro il mock**: M0 e M1 del [TASKS.md](handoff/TASKS.md) in `display/` (scaffolding, font locali,
   token, tema, formato, trasporto finto). Poi M2–M4 (i sei stati, motore dello scontrino, QR).
2. **Dopo il giro completo col registratore**: protocollo reale nell'Agent (diff → `CashIn`) e M5 (trasporto
   WebSocket, resilienza).
3. Decisioni di prodotto ancora aperte nel pacchetto: `compactRows` per i monitor da 10″ e `showLastItemPhoto`.
