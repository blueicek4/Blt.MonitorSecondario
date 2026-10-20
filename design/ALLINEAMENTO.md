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
| 2 | Delta o ristampa completa | **Ristampa completa a ogni battuta.** Serve il diff lato monitor: è già scritto e testato (`ReceiptDiff` in `tools/`) e mette gli articoli ripetuti in fondo, così l'ingresso scatta sulla riga giusta. Passepartout **elimina e ricrea** il file ogni volta, a volte con lo stesso identico contenuto (7 volte su 42) | Tuo riscontro in cassa, `samples/`, [registrazione del 20/10](../samples/registrazioni/2026-10-20_002138/README.md) |
| 3 | Foto articolo e chiave | **Nel file non c'è un codice articolo**, solo la descrizione, per di più **troncata**: a 22 caratteri col modello a 30 colonne (`Hamburger Vecchia Malg`), a 40 con quello a 48. Anche i comandi del modello XML (`StampaRigaArticolo`: descrizione, quantità, prezzo, reparto, IVA) non portano un codice. Una foto si può agganciare solo alla descrizione, quindi è fragile: il default del design `showLastItemPhoto: false` è giusto | `samples/scontrino-ristorante-3-righe.txt`, modello XML |
| 4 | Evento affidabile di fine vendita | **In parte.** Lo scontrino finale porta `PAGATO CONTANTI:` o `PAGATO C.CREDITO:` e `Resto:` dopo il totale: 4 dei 5 conti pagati durante la registrazione (il sesto è rimasto aperto). Il terzo invece non ha scritto nessuna riga di pagamento (da capire come è stato pagato). Il vecchio monitor usa solo il tempo: 20 s senza aggiornamenti → "ARRIVEDERCI E GRAZIE" | [registrazione del 20/10](../samples/registrazioni/2026-10-20_002138/README.md), `docs/01` §3.4 |

Lo stato **Grazie** si apre alla riga `PAGATO …`; se non arriva, per inattività come oggi. La versione dopo un `PAGATO` è
un conto nuovo. Senza `PAGATO` non basta "nessuna riga in comune": il 20/10 un conto nuovo è partito con un articolo
uguale a uno del conto precedente.

## 2. Dal file al protocollo del design

L'interfaccia non legge il file: riceve i messaggi `CashIn` di `handoff/src/types/protocol.ts`, che l'Agent ricava
confrontando ogni versione del file con quella precedente.

| Nel file | Messaggio `CashIn` | Note |
|---|---|---|
| Prima versione `SNF` di un conto | `sale.open` + una `sale.line` per riga | |
| Riga nuova (dal diff) | `sale.line` `item` con **id nuovo** | Gli id li assegna l'Agent e restano stabili tra versioni grazie al diff (stessa riga = stesso id) |
| Riga sparita (dal diff) | `sale.line` `void` con `voidsId` | Il design la tiene barrata 6 s. Lo storno, il 20/10, **toglie la riga** (nessuna riga negativa; da confermare con chi era in cassa) |
| Righe uguali che diventano una (in "dettagli conto") | **Non è uno storno**: aggiornamento della quantità della riga che resta | `Cestino 3,00` due volte → `2 X 3,00 Euro` + `Cestino 6,00`. Il diff vede due righe tolte e due aggiunte: l'Agent deve riconoscere l'unione (stessa descrizione, somma degli importi), altrimenti il cliente vedrebbe due righe barrate |
| `SCONTO  -5,00` tra `SUBTOT.` e `TOT. DA PAGARE` | `discount` sul conto | Sullo scontrino finale lo stesso sconto arriva ripartito sotto ogni riga (`SCONTO -1,25` allineato a destra) e il subtotale è già netto: da decidere se mostrare il ripartito o tenere lo sconto unico, mai tutti e due |
| Intestazione (righe centrate, `-----`, `EURO`) | nessuno | Compare solo in "dettagli conto" e sullo scontrino. La ragione sociale era `Da inserire`, cioè non compilata |
| `PAGATO …:` e `Resto:` | fine vendita → stato **Grazie** | Metodo e resto non hanno un messaggio nel protocollo: proposta `sale.paid { method, amount, change }` |
| Stesso contenuto rimandato | nessuno | Si scarta per hash |
| File vuoto appena creato | nessuno | Passepartout lo crea vuoto e lo riempie subito dopo: si aspetta il contenuto |
| `TOT. DA PAGARE  35,50` | `sale.total` `3550` | **Centesimi**, parsing della stringa senza `parseFloat` (gestire anche `1.234,50`). Il totale viene dalla cassa, come vuole il design |
| `SUBTOT.` | ignorato | Come il vecchio monitor |
| `   2 X 3,00 Euro` o `   2 pz X 12,00 Euro` | `qty` e prezzo unitario della riga **successiva**, che porta l'importo totale | Il primo in "dettagli conto", il secondo cambiando la quantità mentre si batte. Non filtrare per ` x `: ci sono prodotti con ` x ` nel nome (`Tagliere Emilia x 2`), che il vecchio monitor non mostra per questo |
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

- **Descrizioni troncate dalla cassa**: a 22 caratteri col modello a 30 colonne (`Gelato alla crema con`), a 40 con
  quello a 48 provato il 20/10 (`Tortellini in crema di Sua Maesta' il Ne`). La regola "a capo su due righe, poi
  ellissi" serve poco finché la cassa taglia. 48 colonne vanno bene per il nuovo monitor; il vecchio, con righe più
  lunghe, cambia aspetto.
- **Accenti resi con l'apostrofo** (`Maesta'`): il file è ASCII, e il modello XML ammette solo `!/:-=€` oltre a
  lettere e cifre (`CaratteriAmmessi`). Prima di ricostruire gli accenti nel display (funziona quasi sempre, ma sbaglia
  `po'`) conviene provare ad aggiungere `àèéìòù` al modello e vedere col registratore cosa arriva.
- **Ritmo**: col file tolto appena letto le battute arrivano col ritmo di chi batte: nella prova A/B/A 7 versioni in
  2 secondi (0,23–0,47 s l'una dall'altra). Il motore dello scontrino (M3) deve reggere più righe nuove nello stesso
  secondo, scaglionando gli ingressi.
- **Maiuscole e minuscole** arrivano come sono scritte in anagrafica (`Tagliata Al Rosmarino`): coerente con "mai
  maiuscolo forzato".
- **Coperti**: `sale.total.cover` non ha ancora una fonte. Se il conto ha una riga "Coperto", l'Agent li ricava da lì.
- **L'Agent toglie sempre il file appena letto** (decisione del 20/10). Lo dice la [prova A/B/A](../samples/registrazioni/2026-10-20_064854/README.md):
  se il file c'è ancora Passepartout aspetta circa 3 s, lo elimina da sé e scrive (oggi, col vecchio monitor, ogni
  battuta arriva 3 s dopo e alcuni stati intermedi si perdono); se è già stato tolto scrive subito. Con
  `SovrascriviSeEsistente=false` (il modello del 19/10) senza toglierlo gli aggiornamenti si perdono del tutto. Lo
  toglie solo se contiene ancora la versione letta, come il registratore. Non convive col vecchio monitor sullo stesso
  file. Nelle nostre installazioni anche `SovrascriviSeEsistente=true`, come rete di sicurezza se l'Agent si ferma.

## 5. Asset da recuperare (dal pacchetto)

- Logo Bluetech ufficiale in SVG, variante chiara e scura.
- Logo Satispay dal brand kit ufficiale, da usare secondo le loro linee guida.

## 6. Prossimi passi

1. **Subito, contro il mock**: M0 e M1 del [TASKS.md](handoff/TASKS.md) in `display/` (scaffolding, font locali,
   token, tema, formato, trasporto finto). Poi M2–M4 (i sei stati, motore dello scontrino, QR).
2. **Agent**: lettura del file e cancellazione subito dopo (il codice del registratore, già provato in cassa), poi
   protocollo reale (diff → `CashIn`, con unione delle righe e `PAGATO`) e M5 (trasporto WebSocket, resilienza).
   Le registrazioni del 20/10 sono già un banco di prova per il parser. Da registrare ancora: Satispay, messaggi del
   visore, annullo, reso, fattura.
3. Decisioni di prodotto ancora aperte nel pacchetto: `compactRows` per i monitor da 10″ e `showLastItemPhoto`.

## 7. Decisioni prese in M0 e M1 (da confermare)

| Punto del pacchetto | Problema | Decisione |
|---|---|---|
| Il `ThemeProvider` riceve `ThemeTokens` | I token non dicono da quale tema partono e hanno una sola serie di colori: con `mode: 'auto'` non si sa cosa mostrare di giorno | Il locale sceglie un **tema di partenza** (uno degli otto set) più i `ThemeTokens`. In `auto` si alternano le due varianti del tema e i colori dei token sono ignorati; con giorno o notte fissi i colori del locale vincono (`display/src/theme/theme.ts`) |
| Ore di `mode: 'auto'` | "Orari di apertura del locale", che il protocollo non porta | Giorno dalle 7 alle 19, configurabile (`DayHours`). Gli orari veri arriveranno dal pannello |
| Formato degli importi | SPEC §4 chiede `€ 18,90` ma indica `Intl.NumberFormat('it-IT', {style: 'currency'})`, che dà `18,90 €` | Vale l'esempio: Intl formatta il numero, `€` davanti con spazio fine (`display/src/money/money.ts`) |
| `qrLayout` di default | Non indicato nella SPEC | `beside`, come il prototipo |
| Rapporti d'aspetto 1–1,2 | La SPEC copre ≥1,5 / 1,2–1,5 / <1 | `narrow`, la variante più vicina |
| Font in `public/fonts/` | — | Come da TASKS M0, quindi il display va servito dalla radice del sito (`/`): l'Agent lo servirà così |
| Livello di animazione di default | Il prototipo parte da "piena", MOTION.md indica "leggera" come default consigliato | `light` (vale il pacchetto). "Piena" si sceglie dal pannello |
| `data-qr` sul nodo radice | TASKS M1 lo dà al `ThemeProvider`, che non sa in che fase è lo schermo; tokens.css restringe lo scontrino a 760 px quando `data-qr='beside'` | L'attributo c'è solo durante il pagamento: il `ThemeProvider` riceve la fase (oggi dal banco di prova, da M2 da `DisplayRoot`). Fuori dal pagamento lo scontrino resta a 1180 px |
| Colori del locale con giorno o notte fissi | I colori che il preset ricava dagli altri (blocco totale, bagliori, superficie opaca, filetti, testo tenue) restavano quelli del preset: il totale poteva diventare illeggibile | Si ricalcolano dai colori del locale. Il controllo di contrasto copre anche il testo sulla card e la cifra del totale; se non passa, `bluetech-notte` |
| Font e raggio con `mode: 'auto'` | Ignorati insieme ai colori | Restano quelli del locale: sono uguali nelle due varianti di ogni tema |
| QR della cassa finta | Era un SVG con la quiet zone già dentro | PNG vero, un pixel per modulo, senza quiet zone: come quello della cassa, che `QrStage` ingrandisce di un multiplo intero aggiungendo la quiet zone |

**Aperto per M2**: con `mode: 'auto'` il passaggio giorno/notte oggi è secco, la SPEC §6 vuole una dissolvenza di 900 ms.
Va fatta in sola opacità (due livelli, mai una transizione sui colori), sotto il footer e mai durante il pagamento:
il QR non si copre, quindi il cambio aspetta la fine del pagamento.

