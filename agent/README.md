# Bluetech Display Agent

Il servizio che gira sul PC cassa:

1. **Legge** `Scontrino.txt` quando Passepartout ha finito di scriverlo.
2. **Lo toglie subito dopo la lettura**, così Passepartout scrive il successivo senza aspettare. Se il file resta lì,
   Passepartout aspetta circa 3 s prima di sostituirlo (con `SovrascriviSeEsistente=true`), oppure rinuncia (con
   `false`): [prova A/B/A](../samples/registrazioni/2026-10-20_064854/README.md).
3. **Manda al display** i messaggi di `protocol.ts` (`display/src/types/protocol.ts`, ramo `claude/design-handoff`) via
   WebSocket, solo con quello che cambia.

La lettura e la cancellazione sono quelle del [registratore](../tools/README.md), già provate in cassa: il file si
toglie solo se contiene ancora la versione letta. Si toglie **sempre**, anche con il QR a video: è il nostro modo di
lavorare (il vecchio monitor, anche nella versione 6.0, lascia il file dov'è).

## Installazione

Serve Windows 10 o 11 a 64 bit. L'exe contiene già .NET: non si installa niente.

1. Copia `BluetechDisplayAgent.exe` e `appsettings.json` in una cartella, per esempio `C:\Bluetech\Agent`.
2. **Chiudi il vecchio `MonitorCliente.exe` e toglilo dall'avvio automatico.** L'Agent toglie il file dopo averlo letto:
   il vecchio monitor resterebbe senza conti. Se lo trova aperto, l'Agent lo dice nel back office e nel log.
3. **La password del back office**, diversa per ogni locale (non esiste una password uguale per tutti). Due modi:
   - scrivila in `dati\nuova-password.txt` accanto all'exe (o fattene generare una: `BluetechDisplayAgent.exe
     --genera-password`, che la mette in quel file e la scrive a video: annotala nella scheda del cliente). Al primo
     avvio l'Agent ne tiene solo l'hash e toglie il file. È quello che farà l'installazione (Fase 5);
   - oppure avvia l'exe con un doppio clic e apri **sul PC dell'Agent** <http://localhost:5080/backoffice>: la prima
     volta chiede di scegliere la password. Dalla rete non si può: nessuno si prende il back office prima di te.

   Il back office si apre solo dal PC dell'Agent, anche in desktop remoto. Per aprirlo anche dagli altri PC e dai
   telefoni del locale c'è l'opzione "Back office anche dalla rete" (sotto).
4. Nel back office:
   - **Casse**: per ogni punto cassa il file che Passepartout scrive per il monitor (è quello nella cartella del vecchio
     `MonitorCliente.exe`). La prima cassa c'è già, su `C:\MonitorSecondario\Scontrino.txt`;
   - **Opzioni**: se i dispositivi sono in rete, "Raggiungibile dalla rete" (serve una regola nel firewall) e riavvio;
   - **Stampanti**, solo per la copia cartacea dello scontrino digitale;
   - **Dispositivi**: su ogni schermo apri `http://<pc-dell-agent>:5080/auth`, compare un codice di 6 cifre: lo scrivi
     qui scegliendo nome, cassa e stampante, e lo schermo passa da solo alla pagina del cliente.
5. Come servizio (prompt dei comandi da amministratore):

   ```bat
   sc.exe create BluetechDisplayAgent binPath= "C:\Bluetech\Agent\BluetechDisplayAgent.exe" start= delayed-auto DisplayName= "Bluetech Display Agent"
   sc.exe description BluetechDisplayAgent "Legge lo scontrino di Passepartout e lo manda al monitor cliente"
   sc.exe failure BluetechDisplayAgent reset= 86400 actions= restart/5000/restart/5000/restart/30000
   sc.exe start BluetechDisplayAgent
   ```

   Le azioni di ripristino (`sc failure`) servono anche al pulsante **Riavvia** del back office: da servizio l'Agent si
   chiude e Windows lo fa ripartire dopo 5 s.
6. Nel modello XML del monitor di Passepartout conviene `SovrascriviSeEsistente=true`: se l'Agent si ferma, il
   display riceve gli aggiornamenti in ritardo invece di perderli.

Il file di ogni cassa deve stare su un disco del PC dell'Agent: da servizio i dischi di rete collegati con una lettera
(`Z:`) non si vedono. Se un percorso non si raggiunge, l'Agent resta acceso, lo dice nel back office e riprova ogni
10 s.

Il **Player** del monitor cliente apre `http://localhost:5080/auth` (sullo stesso PC) o
`http://<pc-dell-agent>:5080/auth`: la prima volta mostra il codice, poi `/auth` porta subito alla pagina del cliente.

## Le tre pagine

| Indirizzo | Per chi | Cosa |
|---|---|---|
| `/cliente` | i dispositivi autorizzati | Lo schermo del cliente per la cassa del dispositivo, con l'aspetto scelto nel back office: i set di reclame nell'attesa, accanto al conto e nel Grazie (foto, video, promozioni con il QR), il QR di pagamento, lo scontrino digitale con il pulsante della copia. Si installa come app (sotto). A un dispositivo non autorizzato solo "Dispositivo non autorizzato", senza indicazioni su come entrare. Quando nella cartella `display` ci sarà il display del design (M5), `/cliente` servirà quello |
| `/auth` | uno schermo nuovo | Il codice di abbinamento: vale 10 minuti, poi ne compare un altro. Scritto il codice nel back office, lo schermo va su `/cliente`; su un dispositivo già autorizzato `/auth` porta subito lì |
| `/backoffice` | chi ha la password | Stato di casse e display, casse, dispositivi, stampanti (con la stampa di prova), aspetto e promozioni (con l'anteprima), opzioni, statistiche, log, riavvio |

Poi `/ws` (il WebSocket del protocollo: solo i dispositivi autorizzati, ognuno con la sua cassa, e l'anteprima del back
office), `/media/…` (le immagini e i video caricati nel back office: solo ai dispositivi autorizzati e al back office),
`/diagnostica?cassa=…` (una cassa dal vivo con gli ultimi messaggi, dal back office), `/cliente?anteprima=…` (lo
schermo del cliente per il back office: `demo` è un conto finto, `cassa1` una cassa dal vivo) e `/api/stato` (lo stato
completo in JSON, con la password). `/` e `/display` rimandano a `/cliente`. Pubblici e senza dati: il manifest
(`/manifest.webmanifest`), il service worker (`/sw.js`), i file in `/pwa/` (icone, script dell'invito, pagina di
attesa), i font del monitor in `/fonts/` e le palette dei temi in `/aspetto/tema.js`.

## Il monitor come app su telefoni e tablet (PWA)

`/cliente` si installa come app, così su Android e iOS si apre a schermo intero, senza le barre del browser, dalla
schermata Home. Sul dispositivo autorizzato, in attesa, compare in basso un invito:

| Dispositivo | Cosa succede |
|---|---|
| Android con Chrome, Edge o Samsung Internet, **con HTTPS** | "Installa": l'app finisce nella schermata Home e si apre a schermo intero |
| Android **senza HTTPS** | Il browser non installa app da indirizzi `http://` della rete (è una sua regola): l'invito lo dice e offre "Schermo intero", che però si perde se la pagina si ricarica |
| iPhone e iPad (Safari) | Le istruzioni: Condividi → «Aggiungi alla schermata Home». Funziona anche senza HTTPS |
| Player Windows | Niente invito: il Player è già a schermo intero |

- L'app parte da `/auth`: su un dispositivo autorizzato porta subito a `/cliente`, altrimenti mostra il codice.
  **Su iPhone e iPad l'app installata non vede i cookie di Safari**: alla prima apertura mostra un codice nuovo da
  autorizzare nel back office (poi il dispositivo "Safari" si può revocare).
- Con HTTPS anche: lo **schermo resta acceso** finché l'app è aperta, e se l'Agent non risponde (riavvio, Wi-Fi caduto)
  compare "Mi ricollego…" e la pagina torna da sola, al posto dell'errore del browser.
- "Non ora" o "Ok" sull'invito: su quel dispositivo non ricompare.
- Quando ci sarà il display del design (M5), l'Agent aggiunge le stesse cose alla sua pagina.

## Più casse con un solo Agent

Un Agent serve più punti cassa: ogni cassa ha il suo file, la sua vendita e i suoi display, e un display mostra solo la
cassa del suo dispositivo. Esempio con tre casse:

| Cassa | File, sul PC dell'Agent | Display | Stampante della copia |
|---|---|---|---|
| Sala | `C:\MonitorSecondario\Sala\Scontrino.txt` | Player sul secondo monitor | Banco sala |
| Bar | `C:\MonitorSecondario\Bar\Scontrino.txt` | Tablet sul banco | Bar |
| Asporto | `C:\MonitorSecondario\Asporto\Scontrino.txt` | Palmare | Asporto |

Due casse non possono avere lo stesso file. Se le casse sono su altri PC, ognuna scrive il suo file in una cartella
condivisa dal PC dell'Agent (`\\PC-AGENT\Bar`, che lì è `C:\MonitorSecondario\Bar`); con Passepartout su un server
terminal i file sono già tutti lì. Nel dispositivo monitor di ogni postazione di Passepartout il percorso del file deve
essere diverso: va controllato.

Aggiungere o togliere una cassa e cambiarne il file vale subito. Spostare un dispositivo su un'altra cassa, cambiargli
la stampante o revocarlo stacca subito il suo display, che si ricollega con le impostazioni nuove o mostra l'errore.

## Impostazioni (`dati\impostazioni.json`)

Tutto quello che si cambia dal back office sta in un file JSON nella cartella `dati` accanto all'exe, creato al primo
avvio:

| Sezione | |
|---|---|
| `Password` | L'hash della password del back office (PBKDF2): la password non c'è, né in chiaro né cifrata (una password cifrata si decifra, e la chiave starebbe nell'exe di ogni cliente). **Password dimenticata**: da amministratore sul PC dell'Agent scrivi quella nuova in `dati\nuova-password.txt`, o fattene generare una con `BluetechDisplayAgent.exe --genera-password`, e riavvia il servizio: l'Agent la prende e toglie il file, anche se non va bene (sotto i 6 caratteri resta quella di prima, e il log lo dice). Oppure togli la sezione `Password` e impostala dal back office aperto sul PC dell'Agent |
| `BackOfficeDallaRete` | `false`: il back office si apre solo dal PC dell'Agent, anche in desktop remoto. `true`: anche dagli altri PC e dai telefoni della rete del locale, che a volte è la stessa del Wi-Fi dei clienti. Vale subito; spegnendola, le sessioni aperte dalla rete finiscono |
| `Rete` | `Porta` (5080), `ApertoInRete` (`false`: risponde solo a questo PC; `true`: anche alla rete, serve una regola nel firewall), `Https`. Valgono dal riavvio; una porta già usata da un altro programma viene rifiutata. Se all'avvio la porta è occupata lo stesso, l'Agent usa la 5080 (o parte senza HTTPS) e lo dice nel back office |
| `ChiusuraVenditaDopoSecondi` | 60: conto aperto, senza `PAGATO`, e nessun file per un minuto: si chiude e il display torna alle slide. Con il QR Satispay a video almeno 3 minuti. All'avvio, un file più vecchio di così si toglie senza mostrarlo |
| `ScontrinoDigitale` | `MostraScontrino`, `CopiaCartacea`, `SecondiAVideo` (45), `Dicitura`: vedi sotto |
| `Casse` | `Id`, `Nome`, `FileCassa`: un `.txt`, fuori dalle cartelle dell'Agent. L'Agent lo cancella dopo averlo letto, quindi il back office rifiuta anche un file che c'è già e non sembra quello del monitor, e l'Agent non tocca mai un file di oltre 256 KB |
| `Stampanti` | `Id`, `Nome`, `Collegamento` (`rete`, `windows`, `prova`), `Indirizzo`, `LarghezzaMm`, `Colonne`, `TabellaCaratteri`, `QrPuntiPerModulo`, `Taglio`, `TestoRitiro`, `TimeoutSecondi` |
| `Dispositivi` | `Id`, `Nome`, `Cassa`, `Stampante`, `Disposizione` (vuota = automatica; `larga`, `stretta`, `verticale`, `palmare`: sotto), `AutorizzatoIl` e l'hash del token del dispositivo: il token vero lo ha solo il dispositivo, in un cookie |
| `Aspetto` | Come appare lo schermo del cliente: `NomeLocale`, `Sottotitolo`, `Logo`, `LogoChiaro`, `Tema`, `Modo`, `GiornoDalle`/`GiornoAlle`, `Colori`, `FontTitoli`, `FontTesto`, `Raggio`, `Vetro`, `Footer`, `Fondo`/`FondoMedia`, `Animazioni`, `QrPagamento`, `RigheCompatte`, `PrezziSoloSulConto`, le scritte (`Benvenuto`, `Scorrevole`, `TitoloConto`, `Grazie`), `SecondiGrazie`, `Promozioni` (la galleria), `Set` (i set di reclame) e `Media` (l'elenco delle foto e dei video in `dati\media`): vedi sotto |

Ogni modifica si salva subito e la versione prima resta in `impostazioni.json.bak`; se `impostazioni.json` sparisce,
all'avvio torna dalla copia. Il file si può copiare come copia di
sicurezza. Se lo modifichi a mano fallo ad Agent fermo: un file rotto viene messo da parte (`.errore-…`) e l'Agent
riparte dalla copia `.bak`; un errore dentro un file valido (una stampante senza indirizzo) si vede nel back office e non
impedisce di cambiare il resto.

Accanto: `statistiche.json` (i conteggi giorno per giorno, un anno, e l'ultimo accesso dei dispositivi), `https\` (i
certificati generati dall'Agent) e `media\` (immagini e video caricati dal back office: per una copia di sicurezza
completa si copia tutta la cartella `dati`).

`appsettings.json` (sezione `Agent`) dice solo dove stanno le cose: `CartellaDati` (`dati`), `CartellaLog` e `GiorniLog`
(`log`, 14), `CartellaDisplay` (`display`) e `AltreOrigini`, le pagine di altri indirizzi che possono collegarsi a
`/ws` (per il display in sviluppo: `["http://localhost:5173"]`). Dalla riga di comando: `--porta` (solo per
quell'avvio), `--dati`, `--replay`, `--velocita`, `--ripeti`.

## Back office

- **Stato**: per ogni cassa la fase (in attesa, vendita, QR di pagamento, scontrino digitale), il totale, l'ultimo file,
  i file letti, i display collegati e i problemi; gli indirizzi da aprire sui dispositivi; i display collegati; le
  ultime copie cartacee. Si aggiorna da solo. "Schermo del cliente" apre una cassa come la vede il cliente,
  "Diagnostica" la stessa cassa con i messaggi.
- **Aspetto** e **Promozioni**: vedi sotto.
- **Dispositivi**: l'autorizzazione con il codice (con chi sta mostrando un codice in quel momento, ma non il codice),
  nome, cassa e stampante di ogni dispositivo, se è collegato, ultimo accesso e indirizzo, copie stampate; modifica e
  revoca.
- **Stampanti**: "Prova" stampa uno scontrino finto con accenti, euro, sconto, resto e un QR di prova (porta a
  example.com), e mostra il testo della copia.
- **Statistiche**: per giorno e per cassa vendite, conti pagati, incasso visto (righe `PAGATO` meno il resto), conti
  chiusi senza `PAGATO`, QR di pagamento, scontrini digitali, copie cartacee (con la percentuale sugli scontrini
  digitali), copie non stampate, file letti. Sono quello che ha visto il monitor, non i dati fiscali.
- **Log**: il log del giorno scelto, con il filtro "avvisi ed errori" e la ricerca.
- Si apre solo dal PC dell'Agent (anche in desktop remoto), finché in Opzioni non si accende "Back office anche dalla
  rete": chi entra dal back office può cambiare quello che vedono i clienti, compreso l'indirizzo del QR dopo il
  pagamento. Dalla rete, senza l'opzione, la pagina lo spiega e la password non viene nemmeno provata.
- La sessione dura finché si usa (12 ore di inattività al massimo) e finisce al riavvio dell'Agent; 5 password
  sbagliate di fila fermano quel PC per un minuto. I cambi di password da file e l'apertura alla rete finiscono tra gli
  avvisi del log.
- Il cookie di un dispositivo dura 400 giorni (il massimo di Chrome) e si rinnova a ogni visita e ogni 6 ore a pagina
  aperta. Su HTTPS ha un altro nome: passare da HTTPS a HTTP, o il contrario, chiede solo un nuovo abbinamento.

## Aspetto e promozioni

Dal back office si sceglie come appare lo schermo del cliente; i display collegati cambiano subito, senza ricaricare.
L'Agent manda a ogni display il messaggio `cfg` del protocollo del design (`theme`, `ads`, `ticker`) con le aggiunte di
`design/ALLINEAMENTO.md` §8, già scelto per la sua cassa, il giorno e l'ora: il display non decide niente.

- **Il locale**: nome, sottotitolo e logo. Due loghi: quello per i fondi scuri (anche sopra le slide) e, facoltativo,
  quello per i fondi chiari, perché un logo chiaro su un tema di giorno non si vede. Se il logo ha già il nome, il nome
  si lascia vuoto.
- **Tema**: i quattro del design (Bluetech, Trattoria, Fine dining, Cocktail bar), ognuno di notte e di giorno.
  "Automatico" passa dal giorno alla notte con le ore scelte (7-19 se non si cambiano).
- **Colori**: con giorno o notte fissi si possono usare i colori del locale al posto di quelli del tema (con
  l'automatico valgono quelli del tema, come nel design). Il back office misura il contrasto delle coppie che il cliente
  deve leggere (testo sul fondo e sulla card, testo sul primario, cifra del totale): sotto 4,5:1 non salva e propone la
  correzione più vicina, la stessa tinta più chiara o più scura. Lo stesso controllo lo fa lo schermo: un tema
  illeggibile arrivato lo stesso (un file scritto a mano) diventa Bluetech di notte.
- **Caratteri e forma**: gli otto font del design (dentro l'exe, serviti a `/fonts`: niente internet), gli angoli, il
  vetro della card, la firma "powered by Bluetech" chiara o scura (c'è sempre).
- **Sfondo e animazioni**: tinta, sfumato, luci che si muovono piano, un'immagine o un video del locale (velati con il
  colore del fondo); animazioni piene, leggere (consigliate) o ridotte (MOTION.md del design: la riga che entra, il
  totale a contachilometri, le dissolvenze tra gli stati; con "riduci le animazioni" di Windows o del telefono valgono
  sempre le ridotte); il QR del pagamento accanto al conto o a tutto
  schermo; righe più grandi sui 4:3; importi e totale solo quando la cassa mostra il conto (preconto, scontrino), come
  `ShowPricesDuringOrder=false` del vecchio monitor.
- **Scritte**: il benvenuto (nell'attesa senza slide), il titolo del conto, il saluto dopo il pagamento, il testo che
  scorre in basso nell'attesa.
- **Tre gallerie** (scheda Promozioni): le **foto** e i **video** caricati (PNG, JPEG, WebP, GIF fino a 15 MB; MP4 H.264
  o WebM fino a 200 MB, senza audio; 3 GB in tutto) e le **promozioni**: un titolo, un testo, un'immagine e un QR
  (registrazione cliente, fidelity, recensioni, menu digitale: l'Agent fa il QR), tutti facoltativi ma almeno uno, con
  le date di validità. Il tipo dei file si riconosce dal contenuto, non dal nome (un SVG va esportato in PNG). Stanno in
  `dati\media` con l'impronta del contenuto come nome: lo stesso file caricato due volte è uno solo e i display lo tengono
  in cache. Un file usato non si elimina (il back office dice dove è usato); una promozione eliminata esce dai set.
- **Set di reclame**: foto, video e promozioni delle gallerie, in fila, per uno o più posti: le slide a tutto schermo
  dell'**attesa**, la scheda **accanto al conto** (non sui 4:3 né sul palmare, dove lo spazio è del conto), il **Grazie**
  sotto il saluto e il totale. L'ordine è quello dell'elenco, a caso o per nome; con "una promozione con il QR dopo ogni
  N elementi" le promozioni con il QR vanno a parte e tornano a intervalli fissi (a caso con 2: due slide, un QR, due
  slide, un altro QR). Ogni foto e promozione resta i secondi del set o i suoi (per un QR 12-15, il back office propone
  15), un video quanto dura. Ogni set vale per alcune casse, giorni della settimana, ore (anche a cavallo della
  mezzanotte: venerdì 22:00-02:00 vale fino alle 2 di sabato) e date. **Per ogni posto va in onda il primo set
  dell'elenco** acceso, che vale in quel momento e ha qualcosa da mostrare: in alto quelli con l'orario più stretto, in
  fondo quello di sempre. Il back office dice cosa è in onda adesso su ogni cassa e com'è ogni set (in onda, coperto da
  uno più in alto, fuori orario, vuoto, spento). L'Agent mette in fila la playlist (a caso con un seme fisso: lo stesso
  set dà sempre la stessa sequenza, che il display ripete) e la manda già pronta; la ricompone quando un set entra o
  esce dall'orario. Una cassa eliminata spegne i set che erano solo suoi. Set e promozioni si salvano subito; l'aspetto
  con il suo pulsante.
- **Il Grazie**: in alto il saluto e il totale pagato, sotto un elemento del set del Grazie, uno per cliente a
  rotazione. Con una promozione con il QR il Grazie resta finché arriva il conto dopo, al massimo `SecondiGrazie` (30);
  altrimenti 8 secondi. Se la cassa manda il QR dello scontrino digitale, al posto della reclame c'è quello. Senza set
  del Grazie, il saluto e il totale da soli.
- **Accanto al conto, senza set**: il logo e il nome del locale; senza nemmeno quelli il conto sta al centro.
- **Anteprima**: accanto alle due schede lo schermo del cliente con la bozza, prima di salvare: un conto di prova
  (attesa, conto, pagamento, Grazie, o il giro completo) o una cassa dal vivo, in 16:9, 4:3, verticale e palmare.
  Mostra quello in onda adesso, o un set scelto (anche spento o fuori orario); il set o la promozione che si sta
  modificando si vedono mentre si scrive.
- **Dalla prima versione**: le promozioni di prima (ognuna con i suoi posti) e il QR del Grazie diventano, al primo
  avvio, la galleria e i set Attesa, Accanto al conto e Grazie. Casse, giorni e orari di ogni promozione non si portano
  (ora sono del set): il log lo dice, e vanno rimessi sul set.

Lo schermo del cliente sceglie la disposizione dal rapporto d'aspetto (SPEC §9 del design): da 1,5:1 in su larga, con
la colonna laterale (anche 1200×800, con un margine per chi riporta un pixel in più o in meno), sotto stretta, senza
colonna, come sui monitor 4:3; verticale con la reclame sopra il conto, palmare con il conto a tutto schermo. Uno schermo
che sbaglia (un iPad Pro è 4:3, ma la colonna ci sta) si forza nel back office, **Dispositivi → Modifica →
Disposizione**: larga, stretta, verticale o palmare; cambia subito, senza ricaricare la pagina. Dove la colonna è
stretta (4:3, verticale, palmare), una promozione con la foto e il QR perde la foto: il QR e le scritte contano di più.
La tela del formato si adatta alla risoluzione vera dello schermo.

## HTTPS (opzionale)

In **Opzioni → Rete**: HTTPS su una seconda porta (5443), accanto a HTTP. Due modi:

- **certificato dell'Agent** (campo del certificato vuoto): l'Agent crea una piccola autorità
  (`dati\https\autorita.pfx`) e un certificato per `localhost`, il nome del PC e i suoi indirizzi IP, rifatto da solo
  all'avvio quando cambiano o sta per scadere. Sui dispositivi si installa una volta l'autorità
  (`bluetech-agent-ca.cer`, da scaricare in Opzioni): poi il browser si fida senza avvisi anche dei certificati rifatti.
  Senza installarla il browser chiede di accettare il rischio. L'autorità ha i **vincoli di nome**: vale solo per gli
  indirizzi delle reti private e per i nomi di questo PC (anche `.local`), così chi ne copiasse la chiave non potrebbe
  garantire nessun sito vero;
- **un certificato tuo**: un `.pfx` con la chiave privata e la sua password, per esempio di un dominio del cliente.

Il Player sullo stesso PC può restare su `http://localhost`. Con HTTPS la password del back office e i cookie dei
dispositivi non viaggiano in chiaro sulla rete.

## Scontrino digitale a video e copia cartacea (opzionale)

Quando Passepartout manda il QR dello scontrino digitale (`QRCODESD`), il palmare o lo schermo touch mostrano lo
scontrino com'è sulla carta, il QR grande per scaricarlo e il pulsante "Preferisci la copia di carta? Tocca qui". Il
tocco stampa una copia **sulla stampante del dispositivo**, in ESC/POS: intestazione, righe, sconti, totale, pagamenti,
data e in fondo il QR piccolo, che è la parte che vale ai fini fiscali (il PDF firmato del documento). **Nessuna
richiesta a Passepartout Menu né al web service**: i dati sono quelli già passati dal file del monitor. Studio completo
in [`docs/03`](../docs/03-studio-copia-cartacea.md).

- Si accende in **Opzioni** ("Mostra lo scontrino" e "Pulsante per la copia cartacea"); il pulsante compare solo sui
  dispositivi che hanno una stampante.
- **Stampanti**: `rete` (stampante comande in rete, indirizzo e porta, 9100 se manca), `windows` (nome della coda di
  stampa, installata per tutti gli utenti) o `prova` (niente stampante: la copia finisce in `log\copie` come testo e
  come comandi ESC/POS). La stampante deve essere **termica** (su quelle ad aghi il QR non si legge) e stare dove il
  cliente ritira la copia: il testo a video dopo il tocco ("Ritira la copia alla cassa") è per stampante.
- Tabella caratteri: `1252` per le Epson e compatibili, `858` se gli accenti escono sbagliati, senza accenti per
  scrivere come la cassa (`Caffe'`). La stampa di prova serve proprio a sceglierla.
- **Abbinamento del QR al conto.** Il QR non dice di quale conto è. L'Agent lo abbina all'ultimo conto con `PAGATO`
  della stessa cassa se arriva entro 30 s e non è partita un'altra vendita; altrimenti mostra e stampa il solo QR. Così
  non si stampa mai il QR di un cliente con le righe di un altro. La regola viene dal vecchio monitor 6.0: non abbiamo
  ancora visto un `QRCODESD` vero, va controllata con le prime transazioni.
- La schermata resta `SecondiAVideo`, o finché non parte la vendita successiva. Una copia per scontrino: il secondo
  tocco, anche da un altro dispositivo della stessa cassa, risponde "già chiesta"; dopo un errore si può riprovare.
- I palmari vanno sulla rete del personale, mai su quella degli ospiti.
- Per provare senza cassa: una stampante `prova` assegnata al dispositivo, poi si copia nel file della cassa uno
  scontrino pagato (`samples/scontrino-bar-pagato-carta.txt`) e subito dopo `samples/qr-scontrino-digitale-prova.txt`.

## Replay: senza cassa

```bat
BluetechDisplayAgent.exe --replay C:\Registrazioni\2026-10-20_075632 --velocita 2 --ripeti true
```

Rigioca una registrazione del registratore con i suoi tempi (le pause oltre 10 s si accorciano) nella prima cassa
delle impostazioni, al posto del suo file. Serve per le demo in showroom e per provare il display senza cassa: il
display va abbinato come sempre, con `/auth`.

## Come interpreta il file

Le regole vengono dalle [registrazioni](../samples/registrazioni) (30 e 48 colonne):

| Nel file | Al display |
|---|---|
| Riga articolo nuova in fondo | `sale.line` con un id nuovo |
| Quantità o importo cambiati (`2 pz X 12,00 Euro`) | `sale.line` con lo **stesso id** |
| Riga sparita, le altre uguali invariate | storno: `sale.line` `void` con `voidsId` |
| Righe uguali unite in "dettagli conto", sconti spostati sotto le righe, ordine cambiato | `sync`, lo stato completo: niente storni finti |
| `TOT. DA PAGARE` | `sale.total`, in centesimi (il display non somma mai). `bill: true` quando la cassa mostra il conto (c'è l'intestazione: dettagli conto, preconto, scontrino), `false` durante la battuta |
| `PAGATO …:` e `Resto:` | `sale.close` con `paid: { method, amount, change }`: Grazie con le righe, poi le slide. Pagamento misto (una riga per metodo): metodi uniti, `BUONI PASTO + CONTANTI`, importi sommati |
| Conto dopo un `PAGATO`, o senza nessuna riga in comune con quello a video (il coperto non conta: c'è su ogni tavolo) | `sale.open`: vendita nuova |
| `QRCODE` e il QR in Base64 | `pay.qr` con l'ultimo totale, ridotto a un pixel per modulo (il display lo ingrandisce di un multiplo intero e aggiunge la quiet zone), e la didascalia se la cassa la scrive dopo l'immagine. Lo toglie (`pay.clear`) un conto cambiato o `PAGATO`; lo stesso conto rimandato entro 20 s no (il vecchio monitor 6.0 ignora i file per 20 s dopo il QR); altrimenti scade dopo 3 minuti |
| `QRCODESD` e il QR in Base64 (scontrino digitale, vecchio monitor 6.0) | `receipt.qr` con la didascalia: il display lo mostra in Grazie |
| Intestazione del locale, `SUBTOT.`, la valuta dopo i trattini (`EURO`) | niente |
| Versione identica alla precedente | niente |
| Accenti | UTF-8 o Windows-1252, riconosciuti da soli |

**Aggiunte al protocollo**, da implementare nel display (M5):

- `sale.line` con un id già ricevuto **aggiorna** quella riga;
- `{ "t": "hb" }` ogni 3 s. Con una cassa a file il silenzio è normale: senza battito il display mostrerebbe
  "connessione assente" dopo 8 s;
- `sale.close` può portare `paid`, per mostrare metodo e resto nello stato "Grazie";
- `sale.total.bill` (e `bill` in `sync`): la cassa mostra il conto e non la battuta. Serve per l'opzione "prezzi solo
  sul conto" (`ShowPricesDuringOrder=false` del vecchio monitor 6.0);
- `pay.qr.caption`, se la cassa la scrive;
- `receipt.qr { png, caption }`: il QR dello scontrino digitale;
- con lo scontrino digitale attivo: `receipt.digital { id, png, caption?, notice?, receipt?, paperCopy, paper, issuedAt }`
  (lo stesso `receipt` anche in `sync`), `receipt.paper.status { id, state, message? }` e `receipt.clear { id }`; dal
  display all'Agent `receipt.paper { id }`, l'unico comando accettato (al massimo 1 KB, uno ogni 2 s).

## Cosa manca

- Il display del design non ha ancora il collegamento WebSocket (M5): oggi i dispositivi vedono `/cliente`, la pagina
  dell'Agent, con le misure e i temi del design ma non con tutte le sue animazioni.
- Scontrino digitale: l'abbinamento del QR al conto e la copia vanno controllati con le prime transazioni vere; il
  controllo della carta prima di stampare non c'è ancora.
- L'aspetto è per tutto l'Agent: un locale con due marchi (bar e ristorante con nomi diversi) avrebbe bisogno di un
  aspetto per cassa. I set di reclame invece si scelgono già per cassa.
- I buoni sconto (un codice per cliente nel QR, da usare alla cassa): da fare dopo. Oggi il QR di una promozione è un
  indirizzo fisso, uguale per tutti.
- Nessun conteggio delle slide e delle promozioni viste: lo schermo non lo dice all'Agent.
- La PWA è provata in Chrome (installabile, service worker, pagina di attesa) e con l'aspetto di un iPhone e di un
  Android: va provata su dispositivi veri, soprattutto iPhone e iPad.
- Annullo, reso e pagamento Satispay vero non sono nelle registrazioni: se Passepartout scrive righe che l'Agent non
  riconosce, arrivano al display come note e finiscono nel log.
- L'installer (Fase 5) e la firma digitale dell'exe.

## Sviluppo

```bash
dotnet test agent/Bluetech.Display.Agent.Tests
dotnet run --project agent/Bluetech.Display.Agent -- --dati /tmp/agent-dati --porta 5099 --replay samples/registrazioni/2026-10-20_075632 --velocita 4
dotnet publish agent/Bluetech.Display.Agent -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

I test rigiocano le registrazioni vere attraverso l'Agent e un display di prova che applica i messaggi: dopo ogni
versione il display deve mostrare esattamente il conto della cassa (righe, quantità, totale).
