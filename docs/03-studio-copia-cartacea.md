# Studio: scontrino digitale a video e copia cartacea su richiesta

> Funzione **opzionale**, spenta di default, pensata per un **palmare** o uno **schermo touch**. Quando Passepartout
> manda il QR dello scontrino digitale (`QRCODESD`), lo schermo mostra lo scontrino con il dettaglio, il QR per
> scaricarlo e un pulsante "Vuoi la copia cartacea? Tocca qui". Il tocco stampa una copia su una stampante non
> fiscale: intestazione, righe, sconti, pagamenti e in fondo il QR, piccolo, per scaricare il documento fiscale.
>
> **Nessuna richiesta a Passepartout Menu né al suo web service.** Tutti i dati sono già passati dal file del monitor:
> l'Agent compone la copia con quello che ha visto e la stampa da solo, in ESC/POS, su una stampante comande
> standard.
>
> Stato: **studio**, niente è implementato. Le parti segnate *da verificare* dipendono da cose che non abbiamo ancora
> visto: un `QRCODESD` vero e le stampanti dei locali.

## 1. Cosa vede il cliente

1. Paga. Sul monitor compare "Grazie" con le righe del conto (come deciso il 20/10: `design/ALLINEAMENTO.md` §9, nel
   ramo del design).
2. Passepartout manda il QR dello scontrino digitale. Lo schermo (palmare o touch) passa alla schermata **Il tuo
   scontrino**:
   - lo scontrino com'è sulla carta (§5): intestazione del locale, righe, sconti, totale, pagamenti e in fondo il QR
     piccolo;
   - accanto, il QR grande, con "Inquadra per scaricare lo scontrino";
   - sotto il QR grande il pulsante **"Preferisci la copia di carta? Tocca qui"**.
3. Tocca il pulsante. Il pulsante diventa "Sto stampando la tua copia…", poi "Ritira la copia alla cassa".
4. Dopo 45 s (configurabile), o alla prima battuta del cliente successivo, lo schermo torna alle slide.

Se la stampa non riesce: "Non riesco a stampare: chiedi la copia alla cassa". Niente rosso e niente allarmi: il
cliente non deve preoccuparsi.

## 2. Cosa sappiamo e cosa no

| | Stato |
|---|---|
| Dati dello scontrino | **Li abbiamo già**: l'ultima versione del conto, quella con `PAGATO`, ha intestazione, righe, sconti, totale, pagamento e resto. È la stessa che oggi diventa la schermata Grazie |
| `QRCODESD` | Lo riconosce il vecchio monitor 6.0 (`docs/01` §10) e l'Agent lo gestisce già (`receipt.qr`). **Non l'abbiamo mai registrato**: non sappiamo in che ordine arriva rispetto alla riga `PAGATO` (§5). Il contenuto non ci serve: il QR si stampa come immagine |
| Numero del documento | Nel file non c'è, e **non serve** |
| Stampa | La fa l'Agent, in ESC/POS su una stampante comande standard: **nessuna richiesta a Passepartout Menu né al web service** (§6) |
| Schermo touch | Il design attuale lo esclude: SPEC §13 "lo schermo non è touch e non ha controlli". La funzione è pensata per un palmare o uno schermo touch (§7) |
| Focus | Sul palmare e su uno schermo autonomo non c'entra. Su un secondo monitor touch del PC della cassa sì: il Player non prende mai il focus (`WS_EX_NOACTIVATE`) e un tocco **non deve cambiare questo** (§7.2) |

## 3. Architettura

```
Passepartout Menu (cassa)
  │ 1. Scontrino.txt: conto con PAGATO, poi QRCODESD      (come oggi: Passepartout non sa altro)
  ▼
Bluetech Display Agent ──2. receipt.digital { scontrino, QR, copia cartacea sì/no }──► Display (touch)
  │   ▲                                                                                  │
  │   │                         ◄──────────── 3. receipt.paper { id } ───────────────────┘
  │   │
  │   └── 5. receipt.paper.status { id, stato } a tutti i display collegati
  │
  └─ 4. la copia, composta dall'Agent (testo + QR come immagine)
        │  ESC/POS sulla stampante in rete (porta 9100), oppure nella coda di stampa di Windows
        ▼
  stampante non fiscale
```

- Passepartout continua solo a scrivere il file, come oggi. Non riceve niente da noi.
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
| `receipt.digital` | `id` (generato dall'Agent, opaco), `png` (QR, un pixel per modulo), `caption?`, `receipt` (le righe della copia, §5), `paperCopy` (il pulsante va mostrato) |
| `receipt.paper.status` | `id`, `state`: `printing`, `requested` (la copia è arrivata alla stampante o alla coda di Windows), `failed`, `already` (già chiesta), `message?` |

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

Lo stato `requested` non vuol dire "stampato": vuol dire che la copia è partita verso la stampante. Il testo a video
è "Ritira la copia alla cassa", non "Stampata". È la stessa prudenza dell'invariante del design sul pagamento:
mostriamo solo quello che sappiamo davvero.

## 5. La copia

L'Agent la compone dall'ultimo conto con `PAGATO` (la versione che chiude la vendita), nell'ordine di uno scontrino:

1. **intestazione** del locale (ragione sociale, indirizzo, partita IVA): le righe prima dei trattini del conto pagato;
2. **righe**, con le quantità;
3. **sconti** e maggiorazioni, dove sono nel conto (sotto la riga o sul conto);
4. **totale**, in grande;
5. **pagamenti** (metodo e importo; più righe se il pagamento è misto) e **resto**;
6. data e ora: quella in cui l'Agent ha letto il conto pagato. Non è l'ora fiscale, ma la differenza è di qualche
   secondo;
7. **il QR, piccolo**, per scaricare il documento fiscale: l'immagine del `QRCODESD` stampata così com'è (l'Agent ne
   ha già la matrice dei moduli);
8. sotto il QR la dicitura, da validare (§11): per esempio "Inquadra il QR per scaricare il documento commerciale.
   Copia di cortesia".

Su un rotolo da 80 mm (42 colonne):

```
            LOCALE 1
      VIA DELLE PROVE NUM 1
  00000 COMUNE DI PROVA      XX
        P.I. 00000000000
------------------------------------------
                                      EURO
Caffe'                                1,40
                    SCONTO           -0,20
------------------------------------------
TOTALE                                1,20

Pagato C.CREDITO                      1,20
Resto                                 0,00

20/10/2026 08:41

               ▄▄▄▄▄▄▄▄▄
               █ QR    █
               █ 25 mm █
               ▀▀▀▀▀▀▀▀▀
 Inquadra il QR per scaricare il documento
   commerciale. Copia di cortesia.
```

- **Il QR piccolo**: 4 punti per modulo su una stampante da 203 dpi danno circa 25 mm di lato per i 49 moduli del
  QR di Satispay e di Passepartout, più il margine bianco. Si legge bene con un telefono e consuma poca carta. La
  misura si regola (`QrPuntiPerModulo`, da 3 a 6).
- **A video** lo scontrino è l'anteprima di questa copia, con il QR piccolo in fondo come sulla carta. Il QR grande
  accanto resta quello da inquadrare dallo schermo.
- **Cosa non c'è**, perché il file del monitor non lo porta: numero e data del documento commerciale, matricola del
  registratore telematico, ripartizione dell'IVA. Le descrizioni sono tagliate come nel file (22 caratteri con il
  modello a 30 colonne, 40 con quello a 48). Il documento valido resta quello del QR.
- **Se la verifica fiscale (§11) dicesse che serve un duplicato vero**, con numero e IVA, questa copia non basta: un
  duplicato può farlo solo Passepartout, e la scelta di non chiedergli niente andrebbe rivista. Va chiarito prima di
  sviluppare.

**Abbinare il QR al conto giusto.** Il `QRCODESD` non dice di quale conto è. L'Agent lo abbina all'ultimo conto
pagato se arriva entro 30 s dalla riga `PAGATO` e nel frattempo non è partita un'altra vendita; altrimenti mostra il
QR senza scontrino e senza pulsante. Se nelle registrazioni il QR arriva *prima* del `PAGATO`, la regola si
rovescia: è il motivo principale per registrare qualche pagamento con scontrino digitale.

## 6. Come stampa l'Agent

L'Agent gira sul PC della cassa e stampa in **ESC/POS su una stampante comande standard**: testo, grassetto e
caratteri doppi per il totale, il QR come immagine, il taglio della carta. Due condizioni sulla stampante:
- **termica**. Le stampanti comande di cucina sono spesso ad aghi (come la Epson TM-U220), perché la carta termica
  annerisce vicino al calore: su quelle il QR non si stampa in modo leggibile;
- **dove il cliente ritira la copia**: alla cassa o al banco, non in cucina. Il testo "Ritira la copia alla cassa" si
  può cambiare (per esempio "al banco").

| Collegamento | Come | Note |
|---|---|---|
| **Stampante comande in rete** (il caso normale) | ESC/POS sulla porta 9100 | Il più semplice. Se in quel momento Passepartout sta stampando una comanda, la stampante accetta un lavoro per volta: l'Agent riprova per qualche secondo |
| **Coda di stampa di Windows** (stampante USB o condivisa) | ESC/POS in modalità RAW nella stessa coda che usa Passepartout | Windows mette in fila i lavori: niente conflitti con Passepartout. La stampante va installata per tutti gli utenti, perché l'Agent come servizio non vede quelle installate per un solo utente |
| Stampante che non capisce ESC/POS | La copia disegnata come immagine e stampata con il driver di Windows | Solo se serve: più lenta e dipende dalle impostazioni del driver (carta, margini, taglio) |

Dettagli da curare:
- **accenti ed euro**: si sceglie la tabella caratteri della stampante (per le Epson, quella Windows-1252 o PC858).
  Dove non c'è, l'Agent scrive senza accenti, come fa oggi la cassa nel file (`Maesta'`);
- **larghezza**: 58 mm (32 colonne) o 80 mm (42 o 48 colonne), in configurazione;
- **QR come immagine** (comando raster), non con il comando QR della stampante: così non serve leggerne il contenuto
  e si stampa identico su qualunque modello;
- **stato della stampante**: dove il modello lo permette (le Epson sì), l'Agent chiede se c'è carta prima di
  stampare e, se manca, risponde subito `failed`;
- **modelli**: si parte dalle Epson TM (lo standard ESC/POS); Custom e Star vanno provati sui modelli dei locali,
  perché non tutti usano gli stessi comandi.

**Cosa mi serve**: modello della stampante comande del locale pilota (termica), dove si trova, come è collegata
(indirizzo IP o USB, e se Passepartout la usa tramite la coda di Windows), larghezza del rotolo. E **due o tre registrazioni** col registratore
di un pagamento con scontrino digitale (contanti, carta, Satispay), per vedere il `QRCODESD` vero e l'ordine dei file.

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
  cassa, un palmare mostrerebbe lo scontrino di un altro tavolo. Va capito come Passepartout scrive il file del
  monitor per i documenti dei palmari (lo si vede col registratore, senza chiedergli niente):
  - un file per palmare: l'Agent ne segue diversi, uno per canale (`/ws?canale=…`), con 1-2 giorni di lavoro in più;
  - un solo file per tutti: la funzione va bene solo con un palmare per cassa.
- **Standby.** Un palmare si spegne e si ricollega spesso. Lo scontrino digitale corrente deve restare nello stato
  completo (`sync`) per tutto il tempo in cui è a video: chi si ricollega lo rivede. Oggi `receipt.qr` è solo un
  evento, e un palmare che si riaccende un attimo dopo lo perderebbe.
- **Schermo piccolo.** Serve un formato in più per il design, "palmare" (verticale, 360-430 px di larghezza): QR in
  alto, largo quanto basta per essere inquadrato da un altro telefono, il pulsante subito sotto, poi lo scontrino che
  scorre. Gli altri formati del design partono dai monitor da 10".
- **Stampante.** La copia esce sulla stampante configurata nell'Agent, di solito quella non fiscale della cassa. La
  stampante integrata di un palmare non si raggiunge da qui.
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
| 16:9 orizzontale | Lo scontrino a sinistra, come sulla carta con il QR piccolo in fondo (circa 40% della larghezza, fondo carta); a destra il QR grande e sotto il pulsante |
| `narrow`, 10" | Il QR grande e il pulsante, lo scontrino ridotto a totale e numero di articoli |
| `portrait` | Il QR grande in alto, lo scontrino al centro, il pulsante in basso |
| palmare (nuovo, 360-430 px) | Il QR grande in alto, il pulsante subito sotto, lo scontrino che scorre (§7.1) |

Testi (da far approvare, anche in inglese per i turisti):
- titolo: "Il tuo scontrino digitale" / "Your digital receipt";
- sotto il QR grande: la didascalia della cassa se c'è, altrimenti "Inquadra per scaricarlo" / "Scan to download";
- pulsante: "Preferisci la copia di carta? Tocca qui" / "Prefer a paper copy? Tap here";
- dopo il tocco: "Sto stampando la tua copia…", poi "Ritira la copia alla cassa"; in caso di problemi "Non riesco a
  stampare: chiedi la copia alla cassa".

Regole:
- il pulsante è un `<button>` vero, alto almeno 88 px, con il contrasto del tema (SPEC §10);
- si tocca una volta: dopo il tocco diventa un messaggio, non un pulsante;
- il QR grande segue le invarianti: fondo bianco, quiet zone, mai coperto né animato. Il QR piccolo dentro lo
  scontrino è l'anteprima della carta: **deroga da decidere** all'invariante "lato minimo 1/3 dell'altezza dello
  schermo", che vale per il QR da inquadrare;
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
    "Collegamento": "rete",
    "Stampante": "192.168.1.50:9100",
    "LarghezzaMm": 80,
    "Colonne": 42,
    "TabellaCaratteri": "auto",
    "QrPuntiPerModulo": 4,
    "Taglio": true,
    "Dicitura": "Inquadra il QR per scaricare il documento commerciale. Copia di cortesia.",
    "TestoRitiro": "Ritira la copia alla cassa",
    "TimeoutSecondi": 8
  }
}
```

- `MostraScontrino` senza `CopiaCartacea`: scontrino e QR a video, senza pulsante (va bene anche sui monitor non
  touch).
- `CopiaCartacea` richiede `MostraScontrino`, una stampante configurata e un palmare o uno schermo touch. Se la
  stampante non risponde, l'Agent non manda il pulsante e lo segnala in `/api/stato`.
- `Collegamento`: `rete` (`Stampante` è indirizzo e porta), `windows` (`Stampante` è il nome della coda di stampa) o
  `prova`.
- `"Collegamento": "prova"`: modalità dimostrativa, senza stampare. Il pulsante funziona, l'Agent salva la copia
  come immagine accanto al log e risponde `requested`. Serve per lo showroom e per provare la schermata.

## 10. Robustezza e sicurezza

- Timeout di 8 s sulla stampa, poi `failed`. Il cliente non resta mai davanti a "Sto stampando…".
- Una copia per scontrino, anche con più display collegati o tocchi ripetuti.
- Stampante occupata da Passepartout: in rete l'Agent riprova fino al timeout; con la coda di Windows ci pensa il
  sistema.
- Ogni richiesta finisce nel log e in `/api/stato`: ora, display, esito, tempo di stampa.
- L'Agent tiene in memoria solo l'ultimo conto pagato, per il tempo della schermata: nessun archivio di scontrini sul
  PC della cassa.
- `/diagnostica` mostra la schermata con il pulsante: si prova tutta la catena (cassa → Agent → stampante) prima che
  il display vero sia pronto.

## 11. Aspetti fiscali (da verificare, non sono un parere)

- Che cos'è legalmente la copia su stampante non fiscale composta con i dati del monitor: una copia di cortesia dello
  scontrino digitale, che resta il documento valido? Che diciture deve avere o non deve avere?
- Basta una copia senza numero del documento, matricola e IVA (§5), o il cliente che chiede la carta ha diritto a un
  duplicato vero del documento commerciale? Nel secondo caso questa funzione non basta (§5).
- Se il cliente che chiede la carta va considerato come uno che non accetta la consegna digitale, e se questo cambia
  cosa la cassa deve emettere.
- Va chiarito con un consulente fiscale **prima** di sviluppare: il testo del pulsante e della dicitura dipendono
  dalla risposta.

## 12. Piano e stime

Stime indicative, da rivedere quando avremo visto le stampanti.

| Fase | Cosa | Dipende da | Stima |
|---|---|---|---|
| 0 | Registrare 2-3 pagamenti con scontrino digitale; modello e collegamento della stampante del locale pilota; decidere §11 | Tu | — |
| 1 | Solo per il secondo monitor touch del PC della cassa: prototipo del tocco senza focus (§7.2) | Un monitor touch | ½ giorno, +1-2 giorni se serve la modalità visuale |
| 2 | Agent: ultimo conto pagato e abbinamento al QR, scontrino corrente anche nello stato completo, comando dal display, composizione della copia, stampa ESC/POS in rete e nella coda di Windows, stampante finta per i test, abbinamento dei dispositivi con chiave | Fase 0 | 4-5 giorni |
| 2b | Agent: stampa con il driver di Windows, per le stampanti che non capiscono ESC/POS | Solo se serve | 1-2 giorni |
| 2c | Agent: più file, uno per palmare (§7.1), se Passepartout li scrive così | Le registrazioni | 1-2 giorni |
| 3 | Diagnostica: schermata con il pulsante, prova completa in cassa | Fase 2 | ½ giorno |
| 4 | Display: lo stato `receipt` nei formati, compreso il nuovo formato "palmare" | Display M2-M5 | 3-4 giorni |
| 5 | Pannello: i due interruttori | Fase 4 del piano | ½ giorno |
| 6 | Pilota in un locale con palmari o schermo touch | Tutto il resto | 1 settimana |

Le fasi 2 e 3 si possono fare **subito**, senza aspettare il display: la diagnostica si apre anche dal browser del
palmare e basta per provare tutta la catena in cassa. Non dipendono da nessuno: servono solo le registrazioni e una
stampante.

## 13. Rischi

| Rischio | Effetto | Contromisura |
|---|---|---|
| La copia non basta ai fini fiscali | Serve un duplicato, che può fare solo Passepartout | §11 prima di sviluppare |
| Il `QRCODESD` arriva in un ordine diverso da quello previsto | Il QR abbinato al conto sbagliato | Registrazioni prima di sviluppare; nel dubbio, QR senza scontrino e senza pulsante (§5) |
| Stampanti diverse da Epson, comandi diversi | Copia stampata male o non stampata | Prova sui modelli dei locali pilota; stampa con il driver di Windows (2b) |
| Stampante comande ad aghi | QR illeggibile | Solo stampanti termiche; l'Agent lo dice nella configurazione |
| Stampante comande lontana dalla cassa (in cucina) | Il cliente non sa dove ritirare la copia | Una stampante al banco o alla cassa; testo di ritiro configurabile |
| Stampante condivisa con Passepartout | Lavori che si accavallano | Coda di Windows, o ritentativi in rete |
| Stampante installata solo per l'utente della cassa | L'Agent (servizio) non la vede | Installarla per tutti gli utenti; l'Agent lo segnala in `/api/stato` |
| Accenti ed euro | Caratteri sbagliati sulla carta | Tabella caratteri per modello; senza, testo senza accenti |
| Palmare sulla Wi-Fi degli ospiti | Scontrini visibili a estranei, stampe fatte da altri | Rete del personale, abbinamento con chiave (§7.1) |
| Più palmari, documenti tutti dalla stessa cassa | Il cliente vede lo scontrino di un altro tavolo | Un file per palmare (§7.1); altrimenti un palmare per cassa |
| Palmare in standby quando arriva lo scontrino | Il cliente non vede il QR | Lo scontrino corrente nello stato completo (§7.1) |
| Secondo monitor touch sul PC della cassa: WebView2 prende il focus | Battute della cassa perse | Prototipo (§7.2), modalità visuale, o niente pulsante |
| Stampa lenta, senza carta o ferma | Cliente in attesa | Controllo della carta prima di stampare, timeout di 8 s e "chiedi alla cassa" |
| Tocchi ripetuti o display diversi | Copie doppie | Una copia per scontrino |

## 14. Decisioni da prendere

1. Cambiare lo scopo del design: lo schermo diventa interattivo, ma solo in questa schermata e solo se attivato; e
   il display va anche sui palmari, con un formato in più.
2. Le deroghe del design: il totale come elemento più grande e la misura minima del QR, per il QR piccolo
   dell'anteprima (§8).
3. La dicitura sulla copia e se basta la copia composta, dopo §11.
4. La misura del QR piccolo sulla carta (25 mm proposti).
5. Testi del pulsante, anche in inglese.
6. Tempi: quanto resta a video la schermata (45 s proposti).
7. Il locale pilota e i dispositivi: quali palmari (modello, browser) o quali schermi touch, e quale stampante
   comande (termica, vicino alla cassa o al banco).
8. Come Passepartout scrive il file del monitor per i documenti emessi dai palmari (§7.1), da vedere col registratore:
   decide se un palmare può mostrare con certezza lo scontrino del suo tavolo.
