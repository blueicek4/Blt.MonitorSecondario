# Studio: scontrino digitale a video e copia cartacea su richiesta

> Funzione **opzionale**, spenta di default. Quando Passepartout manda il QR dello scontrino digitale (`QRCODESD`), il
> monitor cliente mostra lo scontrino con il dettaglio, il QR per scaricarlo e un pulsante "Vuoi la copia cartacea?
> Tocca qui". Il tocco chiede a Passepartout Menu, tramite il suo web service, di ristampare lo scontrino su una
> stampante non fiscale, con il QR.
>
> Stato: **studio**, niente è implementato. Le parti segnate *da verificare* dipendono da cose che non abbiamo ancora
> visto: un `QRCODESD` vero e il web service di ristampa.

## 1. Cosa vede il cliente

1. Paga. Sul monitor compare "Grazie" con le righe del conto (come deciso il 20/10: `design/ALLINEAMENTO.md` §9, nel
   ramo del design).
2. Passepartout manda il QR dello scontrino digitale. Il monitor passa alla schermata **Il tuo scontrino**:
   - a sinistra lo scontrino come su carta: intestazione del locale, righe, sconti, totale, pagamento e resto;
   - a destra il QR, grande, con "Inquadra per scaricare lo scontrino";
   - sotto il QR il pulsante **"Preferisci la copia di carta? Tocca qui"**.
3. Tocca il pulsante. Il pulsante diventa "Sto stampando la tua copia…", poi "Ritira la copia alla cassa".
4. Dopo 45 s (configurabile), o alla prima battuta del cliente successivo, il monitor torna alle slide.

Se il web service non risponde o la stampante ha un problema: "Non riesco a stampare: chiedi la copia alla cassa".
Niente rosso e niente allarmi: il cliente non deve preoccuparsi.

## 2. Cosa sappiamo e cosa no

| | Stato |
|---|---|
| `QRCODESD` | Lo riconosce il vecchio monitor 6.0 (`docs/01` §10) e l'Agent lo gestisce già (`receipt.qr`). **Non l'abbiamo mai registrato**: non sappiamo cosa contiene il QR (verosimilmente l'indirizzo dello scontrino digitale), né in che ordine arriva rispetto alla riga `PAGATO` |
| Numero del documento | **Nel file non c'è.** Per chiedere la ristampa di *quel* documento serve un identificativo (§5) |
| Web service di ristampa | Da avere: indirizzo, autenticazione, chiamata, risposte (§6) |
| Monitor touch | Il design attuale lo esclude: SPEC §13 "lo schermo non è touch e non ha controlli". Molti monitor cliente dei POS doppio schermo non sono touch |
| Stampa | La SPEC §13 la lascia alla cassa. Qui la stampa resta di Passepartout: noi chiediamo, Passepartout stampa |
| Focus | Il Player non prende mai il focus (finestra `WS_EX_NOACTIVATE`): la tastiera e il lettore di codici restano alla cassa. Un tocco sul monitor cliente **non deve cambiare questo** (§7) |

## 3. Architettura

```
Passepartout Menu (cassa)
  │ 1. Scontrino.txt: conto con PAGATO, poi QRCODESD
  ▼
Bluetech Display Agent ──2. receipt.digital { conto, QR, copia cartacea sì/no }──► Display (touch)
  ▲   │                                                                              │
  │   │                       ◄──────────── 3. receipt.paper { id } ─────────────────┘
  │   │ 4. "ristampa il documento X sulla stampante non fiscale"
  │   ▼
  │  Web service di Passepartout Menu ──► stampante non fiscale: copia con il QR
  │
  └── 5. receipt.paper.status { id, stato } a tutti i display collegati
```

- La stampa la fa **Passepartout**: conosce le stampanti del locale e il modello della copia. L'Agent non parla con
  le stampanti.
- Il display non conosce il documento né il web service: manda solo "il cliente vuole la copia di *questo*
  scontrino digitale", con un `id` che l'Agent ha generato e che vale solo per quello.
- Il file di interscambio si toglie sempre appena letto, come oggi.

## 4. Protocollo

Il collegamento con il display oggi va in una sola direzione: l'Agent ignora tutto quello che il display manda. Qui
servono messaggi nei due sensi.

**Agent → display**

| Messaggio | Contenuto |
|---|---|
| `receipt.digital` | `id` (generato dall'Agent, opaco), `png` (QR, un pixel per modulo), `caption?`, `receipt` (intestazione, righe, totale, pagamento, resto: dall'ultimo conto con `PAGATO`), `paperCopy` (il pulsante va mostrato) |
| `receipt.paper.status` | `id`, `state`: `printing`, `requested` (il web service ha accettato), `failed`, `already` (già chiesta), `message?` |

Con la funzione spenta l'Agent manda il `receipt.qr` di oggi e il display lo mostra in Grazie, come deciso.

**Display → Agent**

| Messaggio | Contenuto |
|---|---|
| `receipt.paper` | `id` del `receipt.digital` |

Regole nell'Agent:
- vale solo l'`id` dello scontrino digitale corrente;
- **una copia per documento**: il secondo tocco riceve `already`;
- messaggi oltre 1 KB, sconosciuti o troppo frequenti (più di uno ogni 2 s per collegamento) si ignorano e si
  scrivono nel log;
- con `ApertoInRete=false` accetta il comando solo dai collegamenti da questo PC.

Lo stato `requested` non vuol dire "stampato": vuol dire che Passepartout ha preso in carico la richiesta. Il testo a
video è "Ritira la copia alla cassa", non "Stampata". È la stessa prudenza dell'invariante del design sul pagamento:
mostriamo solo quello che sappiamo davvero.

## 5. Come si identifica il documento da ristampare

Nel file della cassa non c'è il numero del documento. Tre strade, in ordine di preferenza:

| | Come | Pro | Contro |
|---|---|---|---|
| **A. Dal QR** | L'Agent legge il contenuto del QR `QRCODESD` (verosimilmente l'indirizzo dello scontrino digitale) e ne estrae l'identificativo che il web service accetta | Nessuna modifica a Passepartout; l'identificativo è esattamente quello dello scontrino a video | Serve un decodificatore QR nell'Agent (ZXing.Net, licenza Apache 2.0: il QR ridotto a un pixel per modulo lo rende semplice). Funziona solo se l'indirizzo contiene un identificativo che il web service accetta: *da verificare* con un `QRCODESD` vero |
| **B. Nel file** | Passepartout scrive l'identificativo nel file, per esempio nella riga dopo l'immagine (la didascalia della 6.0) o in una riga dedicata | Il più robusto | Richiede una modifica o una configurazione in Passepartout |
| **C. "Ultimo documento della cassa"** | Il web service ristampa l'ultimo documento emesso da quella cassa | Il più semplice | **Rischioso**: al bar il conto successivo può essere già stato emesso, e si stamperebbe lo scontrino di un altro. Accettabile solo se il web service restituisce anche ora e totale del documento, e l'Agent li confronta con quelli a video prima di stampare |

Raccomandazione: **A**, con **B** se il QR non basta. **C** solo con il controllo di ora e totale.

## 6. Il web service: cosa mi serve

- Documentazione della chiamata di ristampa: indirizzo, metodo, parametri, esempi di richiesta e di risposta,
  codici d'errore.
- Come si identifica il documento (§5) e come si sceglie la stampante non fiscale: per nome, per terminale o
  secondo la configurazione della cassa.
- Autenticazione: chiave, utente e password, token. L'Agent la salva cifrata con DPAPI di Windows e non la scrive mai
  nel log.
- Dove gira il web service: sullo stesso PC, sul server del locale o in cloud. Se è HTTPS con un certificato
  self-signed, si fissa l'impronta del certificato invece di disattivare il controllo.
- Tempi di risposta e comportamento senza carta, con stampante spenta o con un documento già ristampato.
- Se la richiesta è **sincrona** (risponde a stampa avviata) o **asincrona** (risponde con un identificativo da
  controllare).
- Un ambiente di prova: una cassa di prova con una stampante non fiscale, anche virtuale.
- Da quale versione di Passepartout Menu esiste la chiamata: nel parco installato ci sono versioni diverse.
- **Due o tre registrazioni** col registratore di un pagamento con scontrino digitale (contanti, carta, Satispay),
  per vedere il `QRCODESD` vero, il suo contenuto e l'ordine dei file.

## 7. Il tocco senza rubare il focus (rischio n. 1)

Sui POS doppio schermo il monitor cliente è un secondo schermo dello **stesso PC**. Se il tocco del cliente rende il
Player la finestra attiva, la battuta successiva della cassiera e il lettore di codici a barre finiscono sul monitor
cliente invece che in Passepartout. In sala è un guasto grave, peggio del pulsante che non funziona.

Oggi il Player crea la finestra con `WS_EX_NOACTIVATE`: un clic non la attiva. Ma il contenuto è WebView2, un
browser in un processo separato, che al tocco può chiedere il focus per conto suo. **Non lo sappiamo finché non lo
proviamo.** Il piano:

1. **Prototipo (mezza giornata)**: Player in schermo intero con `WS_EX_NOACTIVATE`, pagina con un pulsante, monitor
   touch. Si tocca e si controlla la finestra in primo piano prima e dopo (`GetForegroundWindow`), con Passepartout
   aperto e il cursore in un campo.
2. Se il focus resta alla cassa: fatto.
3. Se WebView2 lo prende, due strade:
   - **ripristino immediato**: il Player ricorda l'ultima finestra attiva non sua e, appena viene attivato, le
     restituisce il primo piano. È semplice, ma per un istante il focus si sposta: un tasto premuto in quel
     momento andrebbe perso;
   - **WebView2 in modalità visuale** (`CoreWebView2CompositionController`): il Player riceve lui il tocco e lo
     inoltra al browser, che non ha più finestre proprie da attivare. È la soluzione pulita, ma costa 1-2 giorni sul
     Player.
4. In ogni caso: Windows deve sapere che il touch è quello del monitor cliente ("Impostazioni Tablet PC" →
   "Configura"). Altrimenti il tocco finisce sullo schermo della cassa. Va nelle istruzioni di installazione.

Senza touch, o con il prototipo che fallisce, la funzione resta comunque utile **senza pulsante**: scontrino a video,
QR e la scritta "Vuoi la copia di carta? Chiedila alla cassa".

Alternativa hardware: un tablet come monitor cliente (`ApertoInRete=true`). È un altro dispositivo, quindi il
problema del focus non esiste.

## 8. La schermata

Nuovo stato del display, **Il tuo scontrino** (`receipt`), dopo Grazie.

| Formato | Disposizione |
|---|---|
| 16:9 orizzontale | Scontrino a sinistra (circa 40% della larghezza, fondo carta), a destra il QR e sotto il pulsante |
| `narrow`, 10" | Il QR e il pulsante, lo scontrino ridotto a totale e numero di articoli |
| `portrait` | Il QR in alto, lo scontrino al centro, il pulsante in basso |

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
  "Ristampa": {
    "Url": "",
    "Credenziali": "",
    "Cassa": "",
    "Stampante": "",
    "TimeoutSecondi": 8
  }
}
```

- `MostraScontrino` senza `CopiaCartacea`: scontrino e QR a video, senza pulsante (va bene anche sui monitor non
  touch).
- `CopiaCartacea` richiede `MostraScontrino`, un web service configurato e un monitor touch. Se il web service non è
  configurato o non risponde, l'Agent non manda il pulsante e lo segnala in `/api/stato`.
- `Credenziali` si scrive in chiaro una volta: al primo avvio l'Agent la cifra con DPAPI e la riscrive cifrata.
- `"Url": "prova"`: modalità dimostrativa, senza web service. Il pulsante funziona, l'Agent scrive nel log la
  richiesta e risponde `requested`. Serve per lo showroom e per provare la schermata.

## 10. Robustezza e sicurezza

- Timeout di 8 s sulla chiamata, poi `failed`. Il cliente non resta mai davanti a "Sto stampando…".
- Una richiesta per documento, anche con più display collegati o tocchi ripetuti.
- Se possibile, una chiave di idempotenza per documento: se l'Agent ripete la chiamata dopo un timeout, Passepartout
  non deve stampare due volte (*da verificare* nel web service).
- Ogni richiesta finisce nel log e in `/api/stato`: ora, display, esito, tempo di risposta. Mai le credenziali, e
  l'identificativo del documento solo abbreviato.
- `/diagnostica` mostra la schermata con il pulsante: si prova tutta la catena (cassa → Agent → web service →
  stampante) prima che il display vero sia pronto.

## 11. Aspetti fiscali (da verificare, non sono un parere)

- Cosa è legalmente la copia su stampante non fiscale: una copia di cortesia dello scontrino digitale, che resta il
  documento valido? Che diciture deve avere o non deve avere?
- Se il cliente che chiede la carta va considerato come uno che non accetta la consegna digitale, e se questo cambia
  cosa la cassa deve emettere.
- Va chiarito con Passepartout e con un consulente fiscale **prima** del pilota: il testo del pulsante e della copia
  dipende dalla risposta.

## 12. Piano e stime

Stime indicative, da rivedere quando avremo il web service.

| Fase | Cosa | Dipende da | Stima |
|---|---|---|---|
| 0 | Registrare 2-3 pagamenti con scontrino digitale; documentazione e accesso di prova al web service; decidere §11 | Tu, Passepartout | — |
| 1 | Prototipo del tocco senza focus sul Player (§7) | Un monitor touch | ½ giorno, +1-2 giorni se serve la modalità visuale |
| 2 | Agent: lettura del QR, scontrino corrente, comando dal display, chiamata al web service, configurazione cifrata, test con un web service finto | Fase 0 | 2-3 giorni |
| 3 | Diagnostica: schermata con il pulsante, prova completa in cassa | Fase 2 | ½ giorno |
| 4 | Display: lo stato `receipt` nei formati | Display M2-M5 | 2-3 giorni |
| 5 | Pannello: i due interruttori | Fase 4 del piano | ½ giorno |
| 6 | Pilota in un locale con monitor touch | Tutto il resto | 1 settimana |

Le fasi 1, 2 e 3 si possono fare **subito**, senza aspettare il display: la diagnostica basta per provare tutta la
catena in cassa.

## 13. Rischi

| Rischio | Effetto | Contromisura |
|---|---|---|
| WebView2 prende il focus al tocco | Battute della cassa perse | Prototipo prima di tutto (§7), modalità visuale, o niente pulsante |
| Monitor cliente non touch, o touch associato allo schermo sbagliato | Pulsante inutile o tocchi sulla cassa | Funzione per cassa; calibrazione nelle istruzioni; versione senza pulsante |
| Identificativo del documento non disponibile | Si stamperebbe il documento sbagliato | Strade A o B; C solo con il controllo di ora e totale |
| Web service lento o fermo | Cliente in attesa | Timeout di 8 s e "chiedi alla cassa" |
| Tocchi ripetuti o display diversi | Copie doppie | Una richiesta per documento, idempotenza |
| Versioni di Passepartout Menu senza la chiamata | Funzione non disponibile | L'Agent controlla all'avvio e spegne il pulsante |
| Stato fiscale della copia | Testi sbagliati in cassa | §11 prima del pilota |

## 14. Decisioni da prendere

1. Cambiare lo scopo del design: il monitor diventa interattivo, ma solo in questa schermata e solo se attivato.
2. La deroga sul totale come elemento più grande (§8).
3. Strada per identificare il documento (§5), dopo aver visto un `QRCODESD` vero.
4. Testi del pulsante e della copia, anche in inglese, dopo §11.
5. Tempi: quanto resta a video la schermata (45 s proposti).
6. Il locale pilota, con monitor touch.
