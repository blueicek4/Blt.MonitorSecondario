# Studio: scontrino digitale a video e copia cartacea su richiesta

> Funzione **opzionale**, spenta di default, pensata per un **palmare** o uno **schermo touch**. Quando Passepartout
> manda il QR dello scontrino digitale (`QRCODESD`), lo schermo mostra lo scontrino con il dettaglio, il QR per
> scaricarlo e un pulsante "Vuoi la copia cartacea? Tocca qui". Il tocco stampa una copia su una stampante non
> fiscale, con il QR.
>
> **Non serve recuperare lo scontrino da Passepartout**: tutti i dati sono già passati dal file del monitor. L'Agent
> compone la copia con quello che ha visto, e la fa stampare.
>
> Stato: **studio**, niente è implementato. Le parti segnate *da verificare* dipendono da cose che non abbiamo ancora
> visto: un `QRCODESD` vero e il modo di stampare (§6).

## 1. Cosa vede il cliente

1. Paga. Sul monitor compare "Grazie" con le righe del conto (come deciso il 20/10: `design/ALLINEAMENTO.md` §9, nel
   ramo del design).
2. Passepartout manda il QR dello scontrino digitale. Lo schermo (palmare o touch) passa alla schermata **Il tuo
   scontrino**:
   - lo scontrino come su carta: intestazione del locale, righe, sconti, totale, pagamento e resto;
   - il QR, grande, con "Inquadra per scaricare lo scontrino";
   - sotto il QR il pulsante **"Preferisci la copia di carta? Tocca qui"**.
3. Tocca il pulsante. Il pulsante diventa "Sto stampando la tua copia…", poi "Ritira la copia alla cassa".
4. Dopo 45 s (configurabile), o alla prima battuta del cliente successivo, lo schermo torna alle slide.

Se la stampa non riesce: "Non riesco a stampare: chiedi la copia alla cassa". Niente rosso e niente allarmi: il
cliente non deve preoccuparsi.

## 2. Cosa sappiamo e cosa no

| | Stato |
|---|---|
| Dati dello scontrino | **Li abbiamo già**: l'ultima versione del conto, quella con `PAGATO`, ha intestazione, righe, sconti, totale, pagamento e resto. È la stessa che oggi diventa la schermata Grazie |
| `QRCODESD` | Lo riconosce il vecchio monitor 6.0 (`docs/01` §10) e l'Agent lo gestisce già (`receipt.qr`). **Non l'abbiamo mai registrato**: non sappiamo cosa contiene il QR (verosimilmente l'indirizzo dello scontrino digitale), né in che ordine arriva rispetto alla riga `PAGATO` |
| Numero del documento | Nel file non c'è, ma **non serve**: la copia si fa con i dati che abbiamo (§5) |
| Stampa | Due strade: una stampa non fiscale tramite il web service di Passepartout, o l'Agent che stampa da solo (§6) |
| Schermo touch | Il design attuale lo esclude: SPEC §13 "lo schermo non è touch e non ha controlli". La funzione è pensata per un palmare o uno schermo touch (§7) |
| Focus | Sul palmare e su uno schermo autonomo non c'entra. Su un secondo monitor touch del PC della cassa sì: il Player non prende mai il focus (`WS_EX_NOACTIVATE`) e un tocco **non deve cambiare questo** (§7.2) |

## 3. Architettura

```
Passepartout Menu (cassa)
  │ 1. Scontrino.txt: conto con PAGATO, poi QRCODESD
  ▼
Bluetech Display Agent ──2. receipt.digital { conto, QR, copia cartacea sì/no }──► Display (touch)
  │   ▲                                                                              │
  │   │                       ◄──────────── 3. receipt.paper { id } ─────────────────┘
  │   │
  │   └── 5. receipt.paper.status { id, stato } a tutti i display collegati
  │
  └─ 4. la copia (testo + QR), composta dall'Agent:
        A. alla stampa non fiscale del web service di Passepartout, oppure
        B. direttamente alla stampante termica
                                   │
                                   ▼
                     stampante non fiscale: copia con il QR
```

- L'Agent conserva l'ultimo conto pagato e lo abbina al `QRCODESD` che arriva subito dopo (§5).
- Il display non conosce né lo scontrino fiscale né la stampante: manda solo "il cliente vuole la copia di
  *questo* scontrino digitale", con un `id` che l'Agent ha generato e che vale solo per quello.
- Il file di interscambio si toglie sempre appena letto, come oggi.

## 4. Protocollo

Il collegamento con il display oggi va in una sola direzione: l'Agent ignora tutto quello che il display manda. Qui
servono messaggi nei due sensi.

**Agent → display**

| Messaggio | Contenuto |
|---|---|
| `receipt.digital` | `id` (generato dall'Agent, opaco), `png` (QR, un pixel per modulo), `caption?`, `receipt` (intestazione, righe, totale, pagamento, resto: dall'ultimo conto con `PAGATO`), `paperCopy` (il pulsante va mostrato) |
| `receipt.paper.status` | `id`, `state`: `printing`, `requested` (la stampa è partita), `failed`, `already` (già chiesta), `message?` |

Con la funzione spenta l'Agent manda il `receipt.qr` di oggi e il display lo mostra in Grazie, come deciso.

**Display → Agent**

| Messaggio | Contenuto |
|---|---|
| `receipt.paper` | `id` del `receipt.digital` |

Regole nell'Agent:
- vale solo l'`id` dello scontrino digitale corrente;
- **una copia per scontrino**: il secondo tocco riceve `already`;
- messaggi oltre 1 KB, sconosciuti o troppo frequenti (più di uno ogni 2 s per collegamento) si ignorano e si
  scrivono nel log;
- accetta il comando dal Player di questo PC e dai dispositivi abbinati con una chiave (§7.1), da nessun altro.

Lo stato `requested` non vuol dire "stampato": vuol dire che la stampante o Passepartout hanno preso il lavoro. Il
testo a video è "Ritira la copia alla cassa", non "Stampata". È la stessa prudenza dell'invariante del design sul
pagamento: mostriamo solo quello che sappiamo davvero.

## 5. La copia: cosa c'è e da dove viene

L'Agent la compone dall'ultimo conto con `PAGATO` (la versione che chiude la vendita), nelle colonne della stampante
(32 o 42 per i rotoli da 58 e 80 mm, oppure 48).

| Sulla copia | Da dove |
|---|---|
| Intestazione del locale (ragione sociale, indirizzo, partita IVA) | Le righe prima dei trattini del conto pagato |
| Righe, quantità, sconti e maggiorazioni | Il conto pagato, come a video |
| Totale, metodo di pagamento, resto | `TOT. DA PAGARE`, `PAGATO …:`, `Resto:` |
| Data e ora | L'ora in cui l'Agent ha letto il conto pagato: non è l'ora fiscale, ma la differenza è di qualche secondo |
| QR dello scontrino digitale | L'immagine del `QRCODESD`, stampata così com'è: l'Agent ne ha già la matrice dei moduli, non serve leggerne il contenuto |
| Dicitura | Per esempio "Copia di cortesia. Il documento commerciale è quello digitale: inquadra il QR" (testo da validare, §11) |

**Cosa non c'è**, perché il file del monitor non lo porta: numero e data del documento commerciale, matricola del
registratore telematico, ripartizione dell'IVA. In più le descrizioni sono tagliate come nel file (22 caratteri con
il modello a 30 colonne, 40 con quello a 48). Il documento valido resta quello del QR.

**Abbinare il QR al conto giusto.** Il `QRCODESD` non dice di quale conto è. L'Agent lo abbina all'ultimo conto
pagato se arriva entro 30 s dalla riga `PAGATO` e nel frattempo non è partita un'altra vendita; altrimenti mostra il
QR senza dettaglio e senza pulsante. Se nelle registrazioni il QR arriva *prima* del `PAGATO`, la regola si rovescia:
è il motivo principale per registrare qualche pagamento con scontrino digitale (§6).

**Vantaggi rispetto a "ristampa il documento X"**: non serve il numero del documento, che nel file non c'è; non c'è
il rischio di ristampare lo scontrino di un altro cliente; sulla copia c'è esattamente quello che il cliente ha visto
a video; funziona con qualunque versione di Passepartout che scrive il `QRCODESD`.

**Quando non basta**: se la consulenza fiscale (§11) dice che la copia deve essere un duplicato vero del documento
commerciale, con numero e IVA. Allora si torna alla ristampa del documento da parte di Passepartout, e serve
l'identificativo: dal contenuto del QR (leggendolo con ZXing.Net, licenza Apache 2.0) o scritto da Passepartout nel
file. Mai "l'ultimo documento della cassa": al bar può essere già quello del cliente successivo.

## 6. Chi stampa

| Strada | Come | Pro | Contro |
|---|---|---|---|
| **A. Web service di Passepartout** | L'Agent manda la copia (righe di testo e QR) a una chiamata di stampa non fiscale: "stampa questo sulla stampante X" | Passepartout conosce le stampanti, anche quella integrata nel palmare, e il loro stato (carta, coperchio) | Serve che il web service abbia una stampa non fiscale **generica**, di testo e QR, non solo la ristampa di un documento: *da verificare* |
| **B. L'Agent stampa da solo** | Direttamente sulla stampante termica in rete (comandi ESC/POS sulla porta 9100), o con il driver di Windows | Nessuna dipendenza da Passepartout: si può fare subito | La stampante va configurata nell'Agent e i modelli vanno provati (Epson, Custom, RCH…); una stampante USB collegata alla cassa può essere occupata da Passepartout; la stampante del palmare non si raggiunge |

Raccomandazione: **A** se il web service ha una stampa non fiscale generica, perché la stampa resta dove sta oggi. **B**
come alternativa, e come modo per avere la funzione dove il web service non c'è. Il QR si stampa come immagine in
tutte e due le strade, quindi non serve leggerne il contenuto; serve solo se la stampa di Passepartout accetta il QR
come testo.

**Cosa mi serve per la strada A**
- La documentazione della chiamata di stampa non fiscale: indirizzo, metodo, formato del testo (righe, larghezza,
  grassetto e allineamenti se ci sono), come si passa il QR (immagine o testo), esempi di risposta, codici d'errore.
- Come si sceglie la stampante: per nome, per terminale o secondo la configurazione della cassa. E se si può usare
  quella del palmare.
- Autenticazione: chiave, utente e password, token. L'Agent la salva cifrata con DPAPI di Windows e non la scrive mai
  nel log.
- Dove gira il web service: sullo stesso PC, sul server del locale o in cloud. Se è HTTPS con un certificato
  self-signed, si fissa l'impronta del certificato invece di disattivare il controllo.
- Se risponde a stampa avviata (sincrona) o con un identificativo da controllare (asincrona); cosa risponde senza
  carta o con la stampante spenta.
- Da quale versione di Passepartout Menu esiste la chiamata, e un ambiente di prova con una stampante non fiscale.

**Cosa mi serve per la strada B**: modello della stampante, collegamento (indirizzo IP o USB) e larghezza del rotolo.

**In tutti e due i casi**: **due o tre registrazioni** col registratore di un pagamento con scontrino digitale
(contanti, carta, Satispay), per vedere il `QRCODESD` vero e l'ordine dei file.

## 7. Dove si tocca: palmare, schermo touch autonomo, secondo monitor della cassa

La funzione è pensata per un **palmare** (la pagina del display aperta nel browser) o per uno **schermo touch**.

| Dispositivo | Focus della cassa | Cosa serve |
|---|---|---|
| Palmare | Nessun problema: è un altro dispositivo | Rete, abbinamento, scontrino giusto, schermo piccolo (§7.1) |
| Schermo touch autonomo (tablet o pannello con il suo browser) | Nessun problema | Rete e abbinamento come il palmare |
| Secondo monitor touch collegato al **PC della cassa** | **Da provare** (§7.2) | Il tocco arriva allo stesso Windows dove gira Passepartout |

### 7.1 Il palmare

- **Rete.** Il palmare si collega all'Agent della cassa via Wi-Fi: `ApertoInRete=true` e una regola nel firewall
  per la porta 5080. I dati passano in chiaro, quindi il palmare va sulla **rete del personale, mai su quella degli
  ospiti**. Sulla Wi-Fi degli ospiti chiunque potrebbe aprire la pagina, vedere gli scontrini e toccare il
  pulsante.
- **Abbinamento.** Oggi l'Agent accetta qualunque pagina servita da lui. Con dispositivi in rete serve una chiave:
  il palmare si abbina una volta (`http://cassa:5080/?chiave=…`, poi la ricorda) e l'Agent accetta il collegamento
  e il comando di stampa solo da chi ha una chiave valida. Le chiavi sono una per dispositivo e si possono revocare.
  Il Player sullo stesso PC non ne ha bisogno.
- **Lo scontrino giusto (la domanda più importante).** Ogni Agent segue il file di una cassa e mostra gli scontrini
  digitali che *quella* cassa emette. Se più camerieri incassano dai palmari e i documenti escono tutti dalla stessa
  cassa, un palmare mostrerebbe lo scontrino di un altro tavolo. Va capito da Passepartout come scrive il file del
  monitor per i documenti dei palmari:
  - un file per palmare: l'Agent ne segue diversi, uno per canale (`/ws?canale=…`), con 1-2 giorni di lavoro in più;
  - un solo file ma con il terminale che ha emesso il documento: l'Agent manda a ogni palmare solo i suoi;
  - un solo file senza nessuna indicazione: la funzione va bene solo con un palmare per cassa.
- **Standby.** Un palmare si spegne e si ricollega spesso. Lo scontrino digitale corrente deve restare nello stato
  completo (`sync`) per tutto il tempo in cui è a video: chi si ricollega lo rivede. Oggi `receipt.qr` è solo un
  evento, e un palmare che si riaccende un attimo dopo lo perderebbe.
- **Schermo piccolo.** Serve un formato in più per il design, "palmare" (verticale, 360-430 px di larghezza): QR in
  alto, largo quanto basta per essere inquadrato da un altro telefono, il pulsante subito sotto, poi lo scontrino che
  scorre. Gli altri formati del design partono dai monitor da 10".
- **Stampante.** Alcuni palmari hanno la stampante termica: con la strada A, se il web service lo permette, la copia
  può uscire lì, al tavolo, invece che alla cassa.
- **Browser.** Il display è scritto per Chromium (WebView2): sui palmari Android con Chrome o WebView recenti va
  bene, ma va provato sul modello vero.

### 7.2 Secondo monitor touch sul PC della cassa

Qui il tocco arriva allo stesso Windows dove la cassiera batte. Se rende il Player la finestra attiva, la battuta
successiva e il lettore di codici a barre finiscono sul monitor cliente invece che in Passepartout.

Oggi il Player crea la finestra con `WS_EX_NOACTIVATE`: un clic non la attiva. Ma il contenuto è WebView2, un
browser in un processo separato, che al tocco può chiedere il focus per conto suo. **Non lo sappiamo finché non lo
proviamo.**

1. **Prototipo (mezza giornata)**: Player in schermo intero, pagina con un pulsante, monitor touch, Passepartout
   aperto con il cursore in un campo. Si tocca e si controlla quale finestra è in primo piano prima e dopo.
2. Se il focus resta alla cassa: fatto.
3. Se WebView2 lo prende, due strade:
   - **ripristino immediato**: il Player restituisce subito il primo piano all'ultima finestra attiva. È semplice, ma
     un tasto premuto in quell'istante andrebbe perso;
   - **WebView2 in modalità visuale** (`CoreWebView2CompositionController`): il Player riceve il tocco e lo inoltra
     al browser, che non ha più finestre proprie da attivare. È la soluzione pulita: 1-2 giorni sul Player.
4. Windows deve sapere che il touch è quello del monitor cliente ("Impostazioni Tablet PC" → "Configura"),
   altrimenti il tocco finisce sullo schermo della cassa. Va nelle istruzioni di installazione.

Senza touch la funzione resta utile **senza pulsante**: scontrino a video, QR e la scritta "Vuoi la copia di carta?
Chiedila alla cassa".

## 8. La schermata

Nuovo stato del display, **Il tuo scontrino** (`receipt`), dopo Grazie.

| Formato | Disposizione |
|---|---|
| 16:9 orizzontale | Scontrino a sinistra (circa 40% della larghezza, fondo carta), a destra il QR e sotto il pulsante |
| `narrow`, 10" | Il QR e il pulsante, lo scontrino ridotto a totale e numero di articoli |
| `portrait` | Il QR in alto, lo scontrino al centro, il pulsante in basso |
| palmare (nuovo, 360-430 px) | Il QR in alto, il pulsante subito sotto, lo scontrino che scorre (§7.1) |

Testi (da far approvare, anche in inglese per i turisti):
- titolo: "Il tuo scontrino digitale" / "Your digital receipt";
- sotto il QR: la didascalia della cassa se c'è, altrimenti "Inquadra per scaricarlo" / "Scan to download";
- pulsante: "Preferisci la copia di carta? Tocca qui" / "Prefer a paper copy? Tap here";
- dopo il tocco: "Sto stampando la tua copia…", poi "Ritira la copia alla cassa"; in caso di problemi "Non riesco a
  stampare: chiedi la copia alla cassa".

Regole:
- il pulsante è un `<button>` vero, alto almeno 88 px, con il contrasto del tema (SPEC §10);
- si tocca una volta: dopo il tocco diventa un messaggio, non un pulsante;
- il QR segue le invarianti: fondo bianco, quiet zone, mai coperto né animato;
- il footer Bluetech resta a schermo;
- **deroga da decidere**: il design vuole il totale come elemento più grande in ogni stato che lo mostra. Qui il
  protagonista è il QR: il totale resta il numero più grande dentro lo scontrino, ma il QR è più grande;
- la prima battuta del conto successivo chiude la schermata: lo scontrino di un cliente non resta a video per il
  cliente dopo. Se la cassiera è veloce, il cliente perde il pulsante; la copia gliela ristampa lei da Passepartout.

## 9. Configurazione

Due interruttori, spenti di default, nel pannello del locale (Fase 4) e per cassa nell'`appsettings.json`
dell'Agent:

```json
"ScontrinoDigitale": {
  "MostraScontrino": false,
  "CopiaCartacea": false,
  "SecondiAVideo": 45,
  "Stampa": {
    "Modo": "passepartout",
    "Url": "",
    "Credenziali": "",
    "Stampante": "",
    "Colonne": 42,
    "Dicitura": "Copia di cortesia. Il documento commerciale è quello digitale: inquadra il QR",
    "TimeoutSecondi": 8
  }
}
```

- `MostraScontrino` senza `CopiaCartacea`: scontrino e QR a video, senza pulsante (va bene anche sui monitor non
  touch).
- `CopiaCartacea` richiede `MostraScontrino`, una stampa configurata e un palmare o uno schermo touch. Se la stampa
  non è configurata o non risponde, l'Agent non manda il pulsante e lo segnala in `/api/stato`.
- `Modo`: `passepartout` (strada A, con `Url`, `Credenziali` e `Stampante`), `diretta` (strada B, con l'indirizzo
  della stampante in `Stampante`, per esempio `192.168.1.50:9100`) o `prova`.
- `Credenziali` si scrive in chiaro una volta: al primo avvio l'Agent la cifra con DPAPI e la riscrive cifrata.
- `"Modo": "prova"`: modalità dimostrativa, senza stampare. Il pulsante funziona e l'Agent scrive la copia nel log,
  così com'è, e risponde `requested`. Serve per lo showroom e per provare la schermata.

## 10. Robustezza e sicurezza

- Timeout di 8 s sulla stampa, poi `failed`. Il cliente non resta mai davanti a "Sto stampando…".
- Una copia per scontrino, anche con più display collegati o tocchi ripetuti.
- Con la strada A, se il web service lo permette, una chiave di idempotenza per scontrino: se l'Agent ripete la
  chiamata dopo un timeout, Passepartout non deve stampare due volte (*da verificare*).
- Ogni richiesta finisce nel log e in `/api/stato`: ora, display, esito, tempo di risposta. Mai le credenziali.
- L'Agent tiene in memoria solo l'ultimo conto pagato, per il tempo della schermata: nessun archivio di scontrini sul
  PC della cassa.
- `/diagnostica` mostra la schermata con il pulsante: si prova tutta la catena (cassa → Agent → stampa) prima che il
  display vero sia pronto.

## 11. Aspetti fiscali (da verificare, non sono un parere)

- Che cos'è legalmente la copia su stampante non fiscale composta con i dati del monitor: una copia di cortesia dello
  scontrino digitale, che resta il documento valido? Che diciture deve avere o non deve avere?
- Basta una copia senza numero del documento, matricola e IVA (§5), o il cliente che chiede la carta ha diritto a un
  duplicato vero del documento commerciale?
- Se il cliente che chiede la carta va considerato come uno che non accetta la consegna digitale, e se questo cambia
  cosa la cassa deve emettere.
- Va chiarito con Passepartout e con un consulente fiscale **prima** del pilota: il testo del pulsante e della
  dicitura, e la scelta tra copia composta e duplicato (§5), dipendono dalla risposta.

## 12. Piano e stime

Stime indicative, da rivedere quando sapremo come si stampa.

| Fase | Cosa | Dipende da | Stima |
|---|---|---|---|
| 0 | Registrare 2-3 pagamenti con scontrino digitale; scegliere la strada di stampa e averne l'accesso di prova; decidere §11 | Tu, Passepartout | — |
| 1 | Solo per il secondo monitor touch del PC della cassa: prototipo del tocco senza focus (§7.2) | Un monitor touch | ½ giorno, +1-2 giorni se serve la modalità visuale |
| 2 | Agent: ultimo conto pagato e abbinamento al QR, scontrino corrente anche nello stato completo, comando dal display, composizione della copia, stampa con la strada A, configurazione cifrata, abbinamento dei dispositivi con chiave, test con una stampa finta | Fase 0 | 3-4 giorni |
| 2b | Agent: stampa diretta ESC/POS (strada B), provata sui modelli di stampante dei locali | Le stampanti | 1-2 giorni |
| 2c | Agent: più file, uno per palmare (§7.1), se Passepartout li scrive così | Risposta di Passepartout | 1-2 giorni |
| 3 | Diagnostica: schermata con il pulsante, prova completa in cassa | Fase 2 | ½ giorno |
| 4 | Display: lo stato `receipt` nei formati, compreso il nuovo formato "palmare" | Display M2-M5 | 3-4 giorni |
| 5 | Pannello: i due interruttori | Fase 4 del piano | ½ giorno |
| 6 | Pilota in un locale con palmari o schermo touch | Tutto il resto | 1 settimana |

Le fasi 2 e 3 si possono fare **subito**, senza aspettare il display: la diagnostica si apre anche dal browser del
palmare e basta per provare tutta la catena in cassa. Con la stampa diretta (2b) non serve nemmeno il web service.

## 13. Rischi

| Rischio | Effetto | Contromisura |
|---|---|---|
| La copia non basta ai fini fiscali | Va rifatta come duplicato del documento | §11 prima di sviluppare; la ristampa del documento resta un'alternativa (§5) |
| Il `QRCODESD` arriva in un ordine diverso da quello previsto | Il QR abbinato al conto sbagliato | Registrazioni prima di sviluppare; nel dubbio, QR senza dettaglio e senza pulsante (§5) |
| Palmare sulla Wi-Fi degli ospiti | Scontrini visibili a estranei, stampe fatte da altri | Rete del personale, abbinamento con chiave (§7.1) |
| Più palmari, documenti tutti dalla stessa cassa | Il cliente vede lo scontrino di un altro tavolo | Un file per palmare o il terminale nel file (§7.1); altrimenti un palmare per cassa |
| Palmare in standby quando arriva lo scontrino | Il cliente non vede il QR | Lo scontrino corrente nello stato completo (§7.1) |
| Secondo monitor touch sul PC della cassa: WebView2 prende il focus | Battute della cassa perse | Prototipo (§7.2), modalità visuale, o niente pulsante |
| Stampa lenta o ferma | Cliente in attesa | Timeout di 8 s e "chiedi alla cassa" |
| Stampa diretta: modelli di stampante diversi, stampante USB occupata | Copia illeggibile o non stampata | Prova sui modelli dei locali pilota; preferire la strada A |
| Tocchi ripetuti o display diversi | Copie doppie | Una copia per scontrino, idempotenza |

## 14. Decisioni da prendere

1. Cambiare lo scopo del design: lo schermo diventa interattivo, ma solo in questa schermata e solo se attivato; e
   il display va anche sui palmari, con un formato in più.
2. La deroga sul totale come elemento più grande (§8).
3. Chi stampa: il web service di Passepartout (strada A) o l'Agent direttamente (strada B) (§6).
4. Cosa c'è sulla copia e la dicitura, dopo §11; e se basta la copia composta o serve il duplicato (§5).
5. Testi del pulsante, anche in inglese.
6. Tempi: quanto resta a video la schermata (45 s proposti).
7. Il locale pilota e i dispositivi: quali palmari (modello, browser, stampante integrata) o quali schermi touch.
8. Come Passepartout scrive il file del monitor per i documenti emessi dai palmari (§7.1): decide se un palmare può
   mostrare con certezza lo scontrino del suo tavolo.
