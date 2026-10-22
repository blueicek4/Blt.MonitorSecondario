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
3. **Le password del back office**, diverse per ogni locale (non esiste una password uguale per tutti). Sono due: una
   per il **titolare** e una per il **tecnico** Bluetech. Senza quella del tecnico il titolare vede anche la parte
   tecnica, come prima. Due modi:
   - scrivile in `dati\nuova-password.txt` e `dati\nuova-password-tecnico.txt` accanto all'exe (o fattele generare:
     `BluetechDisplayAgent.exe --genera-password` e `--genera-password tecnico`, che le mettono in quei file e le
     scrivono a video: annotale nella scheda del cliente). Al primo avvio l'Agent ne tiene solo l'hash e toglie i file;
   - oppure avvia l'exe con un doppio clic e apri **sul PC dell'Agent** <http://localhost:5080/backoffice>: la prima
     volta chiede la password del titolare. Dalla rete non si può: nessuno si prende il back office prima di te.

   Il back office si apre solo dal PC dell'Agent, anche in desktop remoto. Per aprirlo anche dagli altri PC e dai
   telefoni del locale c'è "Back office anche dalla rete" (Installazione › Rete e HTTPS).
4. Dopo la prima password parte la **configurazione guidata**, cinque passi: le password (anche quella del tecnico),
   la cassa (il file che Passepartout scrive per il monitor, quello nella cartella del vecchio `MonitorCliente.exe`;
   la prima cassa c'è già, su `C:\MonitorSecondario\Scontrino.txt`), il primo schermo (su ogni schermo apri
   `http://<pc-dell-agent>:5080/auth`, compare un codice di sei cifre: lo scrivi lì e lo schermo passa da solo al
   conto), il locale (nome, logo, tema) e la prima vetrina (facoltativa). Il monitor funziona dal terzo passo. Il resto
   sta nell'**Installazione** (solo il tecnico): altre casse, "raggiungibile dalla rete" con il riavvio (serve una regola
   nel firewall), le stampanti della copia di carta, i marchi, la modalità demo.
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
| `/cliente` | gli schermi abbinati | Lo schermo del cliente per la cassa dello schermo: le vetrine nell'attesa, accanto al conto e nel Grazie, il conto con il totale a contachilometri, il QR di pagamento, lo scontrino digitale con il pulsante della copia di carta. Sceglie da sé uno dei sei formati del design dal rapporto dei lati. Si installa come app (sotto). Uno schermo non abbinato vede solo "Questo schermo non è abilitato", detto con calma e senza indicazioni su come entrare |
| `/auth` | uno schermo nuovo | Il codice di abbinamento, sei cifre grandi sul tema del locale: vale 8 minuti, poi ne compare un altro da solo. Scritto il codice nel back office, lo schermo va su `/cliente`; su uno schermo già abbinato `/auth` porta subito lì |
| `/backoffice` | chi ha la password | Adesso, Il locale, Vetrine, Contenuti, Schermi, Statistiche; per il tecnico anche l'Installazione (sotto) |

Poi `/ws` (il WebSocket del protocollo: solo gli schermi abbinati, ognuno con la sua cassa, e l'anteprima del back
office), `/media/…` (le immagini e i video caricati: solo agli schermi abbinati e al back office), `/diagnostica?cassa=…`
(una cassa dal vivo con gli ultimi messaggi, dal back office), `/cliente?anteprima=…` (lo schermo del cliente per il
back office: `demo` è un conto finto con la bozza del pannello, `cassa1` una cassa dal vivo), `/prova/scontrino/{id}`
(dove porta il QR finto della modalità demo) e `/api/stato` (lo stato completo in JSON, con la password). `/` e
`/display` rimandano a `/cliente`. Pubblici e senza dati: il manifest (`/manifest.webmanifest`), il service worker
(`/sw.js`), i file in `/pwa/` (icone, script dell'invito, pagina "Mi ricollego alla cassa"), i font in `/fonts/` e i
temi in `/aspetto/tema.js`. Una cartella `display` accanto all'exe, se c'è, sostituisce `/cliente` (per provare un
display diverso senza ricompilare).

## Il monitor come app su telefoni e tablet (PWA)

`/cliente` si installa come app, così su Android e iOS si apre a schermo intero, senza le barre del browser, dalla
schermata Home. Sullo schermo abbinato, in attesa, compare in basso un invito:

| Dispositivo | Cosa succede |
|---|---|
| Android con Chrome, Edge o Samsung Internet, **con HTTPS** | "Installa": l'app finisce nella schermata Home e si apre a schermo intero |
| Android **senza HTTPS** | Il browser non installa app da indirizzi `http://` della rete (è una sua regola): l'invito lo dice e offre "Schermo intero", che però si perde se la pagina si ricarica |
| iPhone e iPad (Safari) | Le istruzioni: Condividi → «Aggiungi alla schermata Home». Funziona anche senza HTTPS |
| Player Windows | Niente invito: il Player è già a schermo intero |

- L'app parte da `/auth`: su uno schermo abbinato porta subito a `/cliente`, altrimenti mostra il codice.
  **Su iPhone e iPad l'app installata non vede i cookie di Safari**: alla prima apertura mostra un codice nuovo da
  autorizzare nel back office (poi il dispositivo "Safari" si può revocare).
- Con HTTPS anche: lo **schermo resta acceso** finché l'app è aperta, e se l'Agent non risponde (riavvio, Wi-Fi caduto)
  compare "Mi ricollego…" e la pagina torna da sola, al posto dell'errore del browser.
- "Non ora" sull'invito: su quel dispositivo torna dopo 30 giorni; installata, non ricompare.

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

Ogni cassa appartiene a un marchio: il bar e il ristorante nello stesso edificio hanno ognuno il suo aspetto, i suoi
contenuti e le sue vetrine. Aggiungere o togliere una cassa e cambiarne il file vale subito. Spostare uno schermo su
un'altra cassa, cambiargli la stampante o revocarlo vale subito: lo schermo si ricollega con le impostazioni nuove o
mostra l'errore; le sue eccezioni (disposizione, attrazione…) arrivano senza nemmeno ricollegarsi.

## Impostazioni (`dati\impostazioni.json`)

Tutto quello che si cambia dal back office sta in un file JSON nella cartella `dati` accanto all'exe, creato al primo
avvio. È la **versione 3** del design (`config.ts`): quattro livelli, ognuno eredita da quello sopra e può cambiare
qualcosa (installazione → marchio → cassa → schermo). L'eredità la risolve l'Agent: a ogni schermo arriva un `cfg` già
piatto, scelto per la sua cassa, il giorno e l'ora.

| Sezione | |
|---|---|
| `Versione` | 3. Un file della versione 2 (o della prima) si porta alla 3 da solo all'avvio: quello di prima resta come `impostazioni.v2.bak` e il log dice riga per riga cosa è stato spostato (l'aspetto nel primo marchio, i dispositivi negli schermi con la disposizione come eccezione, casse, giorni e orari di ogni set in una regola con il nome del set, due regole uguali in una sola, i set nelle vetrine) |
| `Installazione` | `Rete` (`Porta` 5080, `ApertoInRete`, `Https`: valgono dal riavvio; una porta già usata viene rifiutata), `BackOfficeDallaRete`, `Accessi` (un hash PBKDF2 per ruolo, `titolare` e `tecnico`: le password non ci sono, né in chiaro né cifrate), `Demo`, `Loghi` e `Media` (i loghi dei kit ufficiali di Bluetech e di Satispay) |
| `Marchi` | Uno per locale: un'installazione può servire il bar e il ristorante dello stesso edificio. `Identita` (nome, sottotitolo, loghi), `Aspetto` (tema, giorno e notte, colori, caratteri, angoli, vetro, firma, sfondo), `Schermo` (animazioni, attrazione, larghezza del conto, colonna accanto, QR del pagamento, prezzi solo sul conto, righe compatte), `Scritte`, `Vendita` (chiusura del conto, scontrino digitale con i sei posti) e `Contenuti` (`Media`, `Promozioni`, `Regole`, `Vetrine`) |
| `Casse` | `Id`, `Nome`, `Marchio`, `FileCassa` (un `.txt`, fuori dalle cartelle dell'Agent: l'Agent lo cancella dopo averlo letto, quindi il back office rifiuta anche un file che c'è già e non sembra quello del monitor, e l'Agent non tocca mai un file di oltre 256 KB) e le scritte solo sue (`Override.Scritte`) |
| `Schermi` | `Id`, `Nome`, `Cassa`, `Stampante`, `AutorizzatoIl`, l'hash del token (il token vero lo ha solo lo schermo, in un cookie) e le eccezioni dello schermo (`Override`: disposizione `wide`, `narrow`, `portrait`, `palmare`; animazioni, attrazione, larghezza e allineamento del conto) |
| `Stampanti` | `Id`, `Nome`, `Collegamento` (`rete`, `windows`, `prova`), `Indirizzo`, `LarghezzaMm`, `Colonne`, `TabellaCaratteri`, `QrPuntiPerModulo`, `Taglio`, `TestoRitiro`, `TimeoutSecondi` |

**Password dimenticata**: da amministratore sul PC dell'Agent scrivi quella nuova in `dati\nuova-password.txt` (o
`nuova-password-tecnico.txt`), o fattene generare una con `--genera-password [tecnico]`, e riavvia il servizio: l'Agent
la prende e toglie il file, anche se non va bene (sotto i 6 caratteri resta quella di prima, e il log lo dice). Il
tecnico può anche reimpostare quella del titolare dall'Installazione, senza conoscerla.

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

Sei sezioni per il titolare, più una che il titolare **non vede** (SPEC §20 del design):

- **Adesso**: per ogni cassa cosa sta vedendo il cliente (in attesa, conto, QR di pagamento, scontrino digitale, con
  il totale) e cosa va in onda in ogni posto, con le miniature; gli schermi collegati, le ultime copie di carta, gli
  schermi che chiedono il codice. Accanto, la cassa dal vivo.
- **Il locale**: nome e loghi, il tema fra i dieci, i colori con il controllo del contrasto, caratteri e forma, lo
  sfondo, quanto il monitor si fa notare, il conto, le scritte (del locale e di ogni cassa: un campo vuoto segue il
  locale, "riporta al valore del locale" toglie l'eccezione), la vendita e lo scontrino digitale. Si salva con il
  pulsante, dopo aver visto l'anteprima: la barra in basso dice quante modifiche ci sono e su quali schermi andranno.
- **Vetrine**: la settimana in onda (per posto e per cassa, calcolata con la stessa scelta che fanno gli schermi),
  l'ordine di priorità da trascinare, lo stato di ogni vetrina con il perché in chiaro ("In onda adesso su Sala
  nell'attesa…", "coperta da Aperitivo", "torna venerdì alle 18:00"), l'editor con le regole riusabili e quello che gira.
- **Contenuti**: foto, video e promozioni. Si trascinano i file; per ogni file dove è usato, il punto di messa a fuoco
  con i quattro ritagli, "ha già delle scritte dentro". I video arrivano con due fotogrammi, fermo e sfocato, preparati
  dal pannello nel browser (lo schermo non sfoca mai). Le promozioni nei modelli A-D, con l'anteprima nei tre posti.
- **Schermi**: l'abbinamento con il codice, lo stato dal vivo e le eccezioni di ogni schermo. Il titolare le vede e le
  cambia; il tecnico in più assegna la cassa e la stampante.
- **Statistiche**: per giorno e per cassa vendite, conti pagati, incasso visto (righe `PAGATO` meno il resto), conti
  chiusi senza `PAGATO`, QR di pagamento, scontrini digitali, copie di carta (con la percentuale), copie non stampate.
  Sono quello che ha visto il monitor, non i dati fiscali.
- **Installazione** (solo il tecnico): casse e file del gestionale, stampanti (con la stampa di prova: accenti, euro,
  sconto, resto e un QR che porta a example.com), marchi, modalità demo, rete e HTTPS, loghi dei kit, accessi, log,
  riavvio. Entrando da titolare non c'è: non grigia, assente, e le sue API rispondono 403.

L'**anteprima** è una colonna fissa a destra in Adesso, Il locale, Vetrine, Contenuti e Schermi: lo schermo del
cliente vero, in 16:9, 4:3, verticale e palmare, con un conto di prova (attesa, conto, prezzi dopo, pagamento, Grazie,
scontrino, o il giro completo) e la bozza del pannello prima di salvare. A **390 px** le sezioni vanno in basso e
l'anteprima si apre a schermo pieno: dal telefono si fa tutto. Il pannello si può rifare dal primo passo con
"Rifai la configurazione guidata" (menu del ruolo, in alto a destra).

- Si apre solo dal PC dell'Agent (anche in desktop remoto), finché nell'Installazione non si accende "Back office anche
  dalla rete": chi entra può cambiare quello che vedono i clienti, compresi gli indirizzi dei QR. Dalla rete, senza
  l'opzione, la pagina lo spiega e la password non viene nemmeno provata.
- La sessione dura finché si usa (12 ore di inattività al massimo) e finisce al riavvio dell'Agent; 5 password
  sbagliate di fila fermano quel PC per un minuto. Cambiare una password chiude le altre sessioni di quel ruolo.
- Il cookie di uno schermo dura 400 giorni (il massimo di Chrome) e si rinnova a ogni visita e ogni 6 ore a pagina
  aperta. Su HTTPS ha un altro nome: passare da HTTPS a HTTP, o il contrario, chiede solo un nuovo abbinamento.

## Il locale, le vetrine e i contenuti

I quattro piani del design (SPEC §18): **cosa** (foto, video, promozioni: esistono una volta e non sanno dove
finiranno), **dove** (l'attesa, accanto al conto, il Grazie: fissi), **quando** (la regola: casse, giorni, ore, date,
riusabile fra vetrine) e **come** (tema, modello, attrazione: dello schermo).

- **Dieci temi**, scelti dalla sala e non dal colore: Bluetech, Brace, Notturno, Riserva, Onda di sera; Giorno, Forno,
  Latte, Sale, Menta di giorno. Chi ha un gemello (Brace e Forno, Riserva e Sale, Onda e Menta, Bluetech e Giorno) può
  passare da solo dal giorno alla sera agli orari scelti. Quattordici font, dentro l'exe (niente internet).
- **Colori del locale** sopra il tema (solo con il tema fisso): il pannello misura il contrasto delle coppie che il
  cliente deve leggere (testo sul fondo e sulla card, testo sul primario, cifra del totale); sotto 4,5:1 non salva e
  propone la correzione più vicina, la stessa tinta più chiara o più scura. Lo stesso controllo lo fa lo schermo: un
  tema illeggibile arrivato lo stesso (un file scritto a mano) diventa Bluetech.
- **Quanto il monitor si fa notare**: Sobrio (il conto e basta, l'attesa con il locale e il testo che scorre), Vivo,
  Spettacolo, TROPPO. I QR delle promozioni seguono l'attrazione: nel Grazie da Vivo, anche nell'attesa da Spettacolo,
  accanto al conto solo a TROPPO e a vendita ferma. Il movimento vero è il più basso fra l'attrazione, le animazioni
  scelte, "riduci le animazioni" del sistema e il degrado automatico: sotto 45 fotogrammi al secondo per 3 secondi lo
  schermo scende di un livello da solo e lo ricorda.
- **Il conto** sulla griglia a 24 colonne: largo da 9 a 17 (16 di serie), 8-14 con il QR di pagamento accanto; la card
  è alta quanto le righe e sta in basso, vicino al totale. Righe ripetute riunite a video ("Cestino di tigelle ×2", la
  cassa non cambia), storni barrati che escono dopo 6 secondi, sconti e maggiorazioni con il loro ingresso. "Prezzi solo
  sul conto": mentre si ordina la vetrina in grande e la lista stretta senza importi, poi i prezzi a cascata e il totale.
- **Vetrine**: per ogni posto va in onda la prima vetrina dell'elenco che vale per la cassa, il giorno e l'ora e ha
  qualcosa da mostrare oggi; le altre valide sono coperte. Cinque stati, sempre visibili: in onda, coperta (da chi),
  fuori orario (quando torna), vuota, spenta. Una regola a cavallo della mezzanotte appartiene al giorno in cui comincia
  (venerdì 21:00-01:00 vale fino all'1 di sabato). L'ordine è quello dell'elenco, a caso (con un seme fisso) o per
  nome, con una promozione con il QR ogni N elementi; un QR resta almeno 15 secondi.
- **Contenuti**: PNG, JPEG, WebP, GIF fino a 15 MB; MP4 H.264 o WebM fino a 200 MB, muti; 3 GB in tutto. Il tipo si
  riconosce dal contenuto, non dal nome; lo stesso file caricato due volte è uno solo. Un file o una promozione usati non
  si eliminano: il pannello dice dove sono. **Promozioni** in quattro modelli: A solo testo, B foto parlante (una
  locandina: si mostra intera, il sistema non ci scrive sopra), C foto muta con le scritte del sistema, D con il QR.
- **Sei formati**: 1920×1080, 1366×768, 1280×800 (larghi), 1024×768 (stretto: la colonna accanto c'è, solo testo),
  1080×1920 (verticale) e 390×844 (palmare: niente colonna, una riga di promozione sopra il totale a vendita ferma).
  Uno schermo che sbaglia (un iPad Pro è 4:3, ma la colonna ci sta) si forza in **Schermi**, "Disposizione".

## HTTPS (opzionale)

In **Installazione › Rete e HTTPS**: HTTPS su una seconda porta (5443), accanto a HTTP. Due modi:

- **certificato dell'Agent** (campo del certificato vuoto): l'Agent crea una piccola autorità
  (`dati\https\autorita.pfx`) e un certificato per `localhost`, il nome del PC e i suoi indirizzi IP, rifatto da solo
  all'avvio quando cambiano o sta per scadere. Sui dispositivi si installa una volta l'autorità
  (`bluetech-agent-ca.cer`, da scaricare nello stesso riquadro): poi il browser si fida senza avvisi anche dei certificati rifatti.
  Senza installarla il browser chiede di accettare il rischio. L'autorità ha i **vincoli di nome**: vale solo per gli
  indirizzi delle reti private e per i nomi di questo PC (anche `.local`), così chi ne copiasse la chiave non potrebbe
  garantire nessun sito vero;
- **un certificato tuo**: un `.pfx` con la chiave privata e la sua password, per esempio di un dominio del cliente.

Il Player sullo stesso PC può restare su `http://localhost`. Con HTTPS la password del back office e i cookie dei
dispositivi non viaggiano in chiaro sulla rete.

## Scontrino digitale a video e copia cartacea (opzionale)

Quando Passepartout manda il QR dello scontrino digitale (`QRCODESD`), il palmare o lo schermo touch mostrano lo
scontrino com'è sulla carta, il QR grande per scaricarlo e il pulsante "Voglio la copia di carta". Il tocco stampa
una copia **sulla stampante dello schermo**, in ESC/POS: intestazione, righe, sconti, totale, pagamenti,
data e in fondo il QR piccolo, che è la parte che vale ai fini fiscali (il PDF firmato del documento). **Nessuna
richiesta a Passepartout Menu né al web service**: i dati sono quelli già passati dal file del monitor. Studio completo
in [`docs/03`](../docs/03-studio-copia-cartacea.md).

- Si accende in **Il locale › Vendita e scontrino digitale** ("Mostra lo scontrino digitale" e "Copia di carta"); il
  pulsante compare solo sugli schermi touch con una stampante (in Schermi), sugli altri "Preferisci la copia di carta?
  Chiedila alla cassa". Lo scontrino si compone su **sei posti** (tre colonne, due righe): la carta, il QR e la reclame
  del Grazie, dove si vuole; le colonne vuote spariscono, e dove non c'è spazio la carta va in due colonne, poi le righe
  minori si raggruppano, poi cade la reclame. Sul palmare in fila, il QR per primo.
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
- La schermata resta i secondi scelti (45), o finché non parte la vendita successiva. Una copia per scontrino: il secondo
  tocco, anche da un altro dispositivo della stessa cassa, risponde "già chiesta"; dopo un errore si può riprovare.
- I palmari vanno sulla rete del personale, mai su quella degli ospiti.
- Per provare senza cassa: una stampante `prova` assegnata allo schermo, poi si copia nel file della cassa uno
  scontrino pagato (`samples/scontrino-bar-pagato-carta.txt`) e subito dopo `samples/qr-scontrino-digitale-prova.txt`.

### Modalità demo

Per provare lo scontrino digitale e la copia di carta senza una cassa che mandi `QRCODESD` (in showroom, o prima che
Passepartout lo mandi davvero): **Installazione › Modalità demo**.

- Dopo ogni conto con `PAGATO`, se la cassa non manda il suo QR entro 4 secondi, l'Agent fa uno scontrino di prova:
  il conto appena pagato, con un **QR finto** che porta a `/prova/scontrino/{id}` dell'Agent (una pagina che dice
  "Scontrino di prova" e mostra la copia). Sullo schermo c'è la scritta "Prova · QR finto".
- Lo scontrino digitale e il pulsante della copia sono accesi anche se il locale li ha spenti; il pulsante c'è anche
  sugli schermi senza touch, per provarlo con il mouse.
- Gli schermi senza stampante stampano su una stampante finta (`prova`): la copia finisce in `log\copie`.
- Se la cassa manda il suo QR entro quei 4 secondi, vale il suo. Il back office mostra "Modalità demo" in alto finché è accesa: da
  spegnere prima di lasciare il locale.

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

**Aggiunte al protocollo** del design (`design/ALLINEAMENTO.md` §8, ramo `claude/design-handoff`):

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
  display all'Agent `receipt.paper { id }`, l'unico comando accettato (al massimo 1 KB, uno ogni 2 s). In modalità demo
  `receipt.digital` ha `demo: true`; `copy { lines, columns, large }` è la copia di cortesia già impaginata;
- `cfg` è la `ScreenConfig` dell'iterazione 2, già piatta per lo schermo: `cassa`, `screen` (tema, colori, attrazione,
  animazioni, conto, scritte, scontrino digitale, disposizione forzata), `showcase` (per posto la vetrina in onda, i suoi
  elementi e la `sequenza` già messa in fila dall'Agent), `marks` (i loghi dei kit) e `ticker`. Arriva per primo, prima
  di `sync`, e di nuovo quando cambia qualcosa (anche una vetrina che entra in orario). Il messaggio `layout` non c'è
  più: la disposizione forzata è in `cfg.screen.disposizione`.

## Cosa manca

- **Scansioni dei QR per vetrina** (Statistiche): non si possono contare, perché il QR di una promozione porta il
  telefono direttamente al suo indirizzo, senza passare dall'Agent. Si misurano dove arrivano, con un parametro
  nell'indirizzo (`?utm_source=monitor`). Il pannello lo dice.
- Le prove "prima del rilascio" del design (TASKS.md) che chiedono hardware vero: un conto da 30 righe su un 10" letto
  da 70 cm, un video bianco come fondo, una cassa lenta in `eco` per un'ora. In laboratorio: i sei formati, un tema
  chiaro e uno scuro, i quattro livelli di attrazione, il giro completo dalla prima riga allo scontrino di prova.
- Della reclame non ci sono la seconda foto verticale caricata a parte (il verticale ritaglia la stessa foto intorno al
  punto di messa a fuoco) né la parallasse delle slide.
- Scontrino digitale: l'abbinamento del QR al conto e la copia vanno controllati con le prime transazioni vere; il
  controllo della carta prima di stampare non c'è ancora.
- I buoni sconto (un codice per cliente nel QR, da usare alla cassa): da fare dopo. Oggi il QR di una promozione è un
  indirizzo fisso, uguale per tutti.
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
