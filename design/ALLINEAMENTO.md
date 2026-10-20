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
| Righe uguali che diventano una (in "dettagli conto") | **Non è uno storno**: `sync` con le righe nuove | `Cestino 3,00` due volte → `2 X 3,00 Euro` + `Cestino 6,00`. Senza riconoscere l'unione il cliente vedrebbe due righe barrate. Lo stesso per gli sconti spostati sotto le righe e per i cambi d'ordine (§8) |
| `SCONTO  -5,00` tra `SUBTOT.` e `TOT. DA PAGARE` | `discount` sul conto | Sullo scontrino finale lo stesso sconto arriva ripartito sotto ogni riga (`SCONTO -1,25` allineato a destra) e il subtotale è già netto: da decidere se mostrare il ripartito o tenere lo sconto unico, mai tutti e due |
| Intestazione (righe centrate, `-----`, `EURO`) | nessuno | Compare solo in "dettagli conto" e sullo scontrino. La ragione sociale era `Da inserire`, cioè non compilata |
| `PAGATO …:` e `Resto:` | `sale.close` con `paid: { method, amount, change }` → stato **Grazie** | Estensione (§8). `method` è il testo della cassa (`CONTANTI`, `C.CREDITO`) |
| Stesso contenuto rimandato | nessuno | Si scarta per hash |
| File vuoto appena creato | nessuno | Passepartout lo crea vuoto e lo riempie subito dopo: si aspetta il contenuto |
| `TOT. DA PAGARE  35,50` | `sale.total` `3550` | **Centesimi**, parsing della stringa senza `parseFloat` (gestire anche `1.234,50`). Il totale viene dalla cassa, come vuole il design |
| `SUBTOT.` | ignorato | Come il vecchio monitor |
| `   2 X 3,00 Euro` o `   2 pz X 12,00 Euro` | `qty` e prezzo unitario della riga **successiva**, che porta l'importo totale | Il primo in "dettagli conto", il secondo cambiando la quantità mentre si batte. Non filtrare per ` x `: ci sono prodotti con ` x ` nel nome (`Tagliere Emilia x 2`), che il vecchio monitor non mostra per questo |
| Messaggio del visore (file senza `SNF`) | `display.msg` | Il design estrae etichetta e importo con una regex: i testi reali sono `{riga1} {riga2}` del visore (16 colonne) |
| `SNF` + `QRCODE` + riga Base64 | `pay.qr` `png: data:image/png;base64,…`, `amount` = ultimo totale | Visto il 20/10 ([registrazione](../samples/registrazioni/2026-10-20_075632/README.md)): PNG 197×197 in bianco e nero, QR versione 8, 49 moduli da 4 pixel, **senza quiet zone**. L'Agent lo riduce a un pixel per modulo; `QrStage` lo ingrandisce di un multiplo intero e aggiunge la quiet zone. Arriva dopo "dettagli conto" |
| Il conto rimandato dopo il QR | `pay.clear` | La cassa rimanda il conto prima della riga `PAGATO` |
| Fattura | come uno scontrino | Al monitor non arrivano i dati del cliente |
| Promozione | `discount` sotto le righe | Compare solo sullo scontrino finale, ripartita riga per riga |
| Qualunque file dopo un QR | `pay.clear` | È il comportamento del vecchio monitor |
| Conto nuovo | `sale.close` + `sale.open` | Dopo un `PAGATO`. Senza `PAGATO`: un conto senza nessuna riga in comune con quello a video, oppure 5 minuti senza file. Un conto nuovo che parte con un articolo uguale a quello di un conto non pagato si vede come storni del vecchio: da rivedere con altre registrazioni |
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
| QR della cassa finta | Era un SVG con la quiet zone già dentro | PNG vero, un pixel per modulo, senza quiet zone, 49 moduli come quello di Satispay visto il 20/10: `QrStage` lo ingrandisce di un multiplo intero aggiungendo la quiet zone |

**Aperto per M2**: con `mode: 'auto'` il passaggio giorno/notte oggi è secco, la SPEC §6 vuole una dissolvenza di 900 ms.
Va fatta in sola opacità (due livelli, mai una transizione sui colori), sotto il footer e mai durante il pagamento:
il QR non si copre, quindi il cambio aspetta la fine del pagamento.

## 8. Aggiunte al protocollo fatte nell'Agent (da implementare in M5)

L'[Agent](../agent/README.md) manda i messaggi di `protocol.ts` con queste aggiunte. In `display/src/types/protocol.ts`
vanno aggiunte con il trasporto WebSocket (M5). Le ultime quattro vengono dal vecchio monitor 6.0 di Passepartout
(`docs/01` §10).

| Messaggio | Perché | Cosa fa il display |
|---|---|---|
| `sale.line` con un `id` già ricevuto | La quantità cambia mentre si batte (`2 pz X 12,00 Euro`) o in "dettagli conto" | Sostituisce la riga con quell'id, senza animazione d'ingresso: cambia la cifra |
| `{ t: 'hb' }` ogni 3 s | Con la cassa a file il silenzio è normale: senza battito dopo 8 s comparirebbe "connessione assente" | Conta come messaggio per `link`, nient'altro |
| `sale.close` con `paid?: { method: string; amount: Cents; change?: Cents }` | La cassa scrive metodo e resto | Grazie mostra l'importo pagato, il metodo (`C.CREDITO` → "Carta") e il resto solo se > 0 |
| `sync` durante la vendita, non solo alla riconnessione | Righe unite, sconti spostati sotto le righe, ordine cambiato: con i soli messaggi incrementali il cliente vedrebbe storni finti | Sostituisce le righe confrontando gli id: anima solo quelle davvero nuove, nessuna uscita di massa |
| `receipt.qr { png, caption? }` | `QRCODESD`: il QR per scaricare lo **scontrino digitale**, a pagamento fatto | Nello stato Grazie, al posto del QR di ringraziamento del pannello, con la didascalia (default "Scarica il tuo scontrino digitale"). Grazie dura 30 s. Arrivato a conto aperto, aspetta il `PAGATO` |
| `pay.qr.caption?` | La 6.0 mostra sopra il QR la riga che la cassa scrive dopo l'immagine. Finora quella riga è vuota | Se c'è, al posto del testo del design |
| `sale.total.bill` (e `bill` in `sync`) | `true` quando la cassa mostra il conto (intestazione: dettagli conto, preconto, scontrino), `false` durante la battuta | Con l'opzione del pannello "prezzi solo sul conto" (`ShowPricesDuringOrder=false` della 6.0, per chi la usa già) le righe senza importi e niente totale finché è `false` |
| `pay.clear` seguito da `sale.close` | Un QR arrivato dopo la chiusura per inattività (nessun conto a video) scade da solo dopo 3 minuti | L'attesa, non una vendita vuota. In generale `pay.clear` torna alla vendita solo se ci sono righe |

`pay.qr.amount` è l'ultimo totale ricevuto: il file del QR non porta l'importo. Il PNG arriva già ridotto a un pixel
per modulo (49×49 per il QR Satispay), senza quiet zone.

Per M5 in sviluppo: l'Agent accetta su `/ws` solo le pagine servite da lui stesso. Il display servito da Vite
(`http://localhost:5173`) si collega se nell'`appsettings.json` dell'Agent c'è `"AltreOrigini": ["http://localhost:5173"]`.

**Il vecchio monitor sullo scontrino pagato** (foto del 20/10, [`samples/scontrino-bar-pagato-carta.txt`](../samples/scontrino-bar-pagato-carta.txt)):
mostra ragione sociale, indirizzo e partita IVA, `EURO`, le righe con `SCONTO`, `PAGATO C.CREDITO:` e `Resto:` nel
corpo, `TOTALE` in basso; nasconde `SNF`, i trattini, `SUBTOT.` e `TOT. DA PAGARE` (che diventa il totale in basso).
Il conto resta a video fino al file successivo. Nel nostro display l'intestazione la fa il tema (logo del locale, non
la partita IVA).

## 9. Grazie: decisioni del 20/10

- **Grazie mostra anche le righe del conto**, non solo l'importo: lo scontrino resta dov'era (stretto come nel pagamento
  `beside`) e nella colonna accanto compaiono "Grazie e arrivederci", l'importo pagato, il metodo e il resto se > 0. Il
  display quindi a `sale.close` **non svuota** le righe: le tiene per tutto lo stato Grazie e le toglie al passaggio
  all'attesa o al primo messaggio del conto successivo. Una riga viene sempre battuta prima del pagamento, quindi le
  righe a video ci sono sempre.
- **Finito Grazie si va all'attesa, con le slide del locale, senza il conto vecchio** (immagini e video di `cfg.ads`, testo
  scorrevole). Nessuno scontrino resta a video quando il cliente se n'è andato:

  | Come finisce il conto | Cosa manda l'Agent | Cosa fa il display |
  |---|---|---|
  | Riga `PAGATO …` | `sale.close` con `paid` | Grazie con le righe per 8 s (30 s con il QR di ringraziamento), poi l'attesa |
  | Nessun `PAGATO` e nessun file per 60 s (conto visto e lasciato, tavolo parcheggiato); con il QR Satispay a video almeno 3 minuti | `sale.close` senza `paid` | Subito l'attesa: senza pagamento non si ringrazia |
  | Conto nuovo prima della fine di Grazie | `sale.open` | Il conto nuovo |

  I 60 s (`ChiusuraVenditaDopoSecondi` dell'Agent, configurabile) sono una tua scelta del 20/10. Nelle registrazioni tra
  l'ultimo file e la riga `PAGATO` sono passati 9–67 s con contanti e POS e 80 s con il QR Satispay a video: oltre i
  60 s (2 pagamenti su 8: contanti, uno dopo due tentativi falliti al POS) il display va alle slide e torna con Grazie e le righe quando arriva `PAGATO`. Il QR invece
  resta almeno 3 minuti, perché il cliente lo sta usando. Con i 20 s del vecchio monitor il totale sarebbe sparito mentre
  il cliente pagava in 6 conti su 9.
- **QR di ringraziamento impostabile dal pannello** (Fase 4): recensioni Google, registrazione cliente/fidelity, menu
  digitale o un indirizzo qualunque, con la didascalia. La SPEC §2.5 lo prevedeva già ("QR opzionale a destra"), ma
  nessun campo del protocollo lo portava. Proposta:
  - nel messaggio `cfg`: `thanks?: { png: string; caption: string; seconds?: number }`. Il pannello salva l'indirizzo,
    l'**Agent genera il PNG** (un pixel per modulo, senza quiet zone, come quello di Satispay): il display lo mostra con
    lo stesso `QrStage` e resta senza librerie QR;
  - **durata**: con il QR, 8 s non bastano per tirare fuori il telefono e inquadrare. Grazie resta finché non arriva il
    conto successivo, al massimo `seconds` (default 30 s); senza QR resta 8 s come da SPEC. Poi sempre l'attesa;
  - un solo QR alla volta, non due a rotazione: il cliente ha pochi secondi e una sola scelta funziona meglio;
  - per misurare se funziona conviene un indirizzo con `?utm_source=monitor-cassa` (dove la pagina lo permette; il
    link alle recensioni di Google no);
  - legare la registrazione allo scontrino (punti fidelity sull'acquisto appena fatto) oggi non si può: nel file non c'è
    il numero del documento.

## 10. Proposta: scontrino digitale a video e copia cartacea (da decidere)

Studio in [`docs/03`](../docs/03-studio-copia-cartacea.md). Funzione opzionale che **cambia lo scopo** della SPEC
§13 ("lo schermo non è touch e non ha controlli"; "la stampa resta sulla cassa"): un nuovo stato `receipt` dopo Grazie,
con lo scontrino, il QR dello scontrino digitale e un pulsante per chiedere la copia di carta, che stampa Passepartout.
Per il design: un `<button>` vero in un solo stato e solo se il locale lo attiva, e una deroga all'invariante del
totale come elemento più grande (in questo stato il protagonista è il QR). Prima di disegnarlo va fatto il prototipo
sul focus della cassa (§7 dello studio).

