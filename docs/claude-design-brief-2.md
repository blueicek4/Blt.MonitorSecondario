# Brief 2: Bluetech Display, seconda iterazione del design

Evoluzione grafica e di esperienza di tutto il prodotto: lo **schermo del cliente** in tutti gli stati e formati, il
**back office del locale** (mai progettato finora) e le **pagine dei dispositivi**. Questo brief segue il primo
([claude-design-brief.md](claude-design-brief.md)) e il pacchetto che ne è uscito (`design/handoff/`: SPEC.md,
MOTION.md, token, 23 artboard). Quel lavoro resta la base: qui ci sono quello che abbiamo imparato dalla cassa vera,
le funzioni nuove e le richieste dei locali.

## 0. Come leggere questo brief

- Le sezioni 1–4 dicono cosa esiste e con che vincoli. Le sezioni 5–7 dicono cosa progettare, compresa la
  **struttura della configurazione** (§6.3), da rivedere anche quella. La 8 è il contratto dei dati. La 9 dice cosa
  consegnare e la 10 dove pensiamo che si possa fare meglio.
- "Oggi" descrive l'implementazione attuale: funziona, ma è stata disegnata da uno sviluppatore. Va presa come
  inventario delle funzioni, non come riferimento estetico.
- Tutto è in italiano: testi, importi `€ 18,90` (euro davanti, virgola decimale), date `gg/mm/aaaa`.

## 1. Cosa è cambiato dal primo giro

- **Il prodotto esiste e gira in cassa.** Un servizio Windows (l'**Agent**) legge il file che il gestionale
  Passepartout Menu scrive per il monitor, lo trasforma nei messaggi del protocollo del design e li manda via
  WebSocket ai display della rete locale. Ha un back office web con password.
- **Le quattro domande aperte del primo pacchetto hanno una risposta** (sezione 4): la cassa scrive un file; ogni
  battuta è una ristampa completa (il diff lo fa l'Agent, con id stabili per riga); niente codici articolo e niente
  foto; la fine vendita si riconosce dalla riga `PAGATO` quando c'è.
- **Funzioni nuove**: gallerie di foto, video e promozioni con QR; set di reclame per l'attesa, accanto al conto e nel
  Grazie, con orari, casse e regole di rotazione; Grazie con la promozione sotto; scontrino digitale a video con la
  copia di carta su richiesta; display su palmari e tablet come app (PWA); disposizione forzata per dispositivo.
- **Lo schermo che si vede oggi** è una pagina provvisoria dell'Agent (`/cliente`) con le misure, i temi e le
  animazioni del pacchetto, in attesa del display React. Le schermate allegate vengono da lì.

## 2. Chi usa cosa

| Chi | Dove e come | Cosa gli serve |
|---|---|---|
| **Il cliente al banco** | Guarda lo schermo da 0,5–2 m per pochi secondi, spesso con il telefono in mano per pagare | Cosa paga, quanto, come. Poi, se ha un attimo, una promozione o un QR che valga la pena inquadrare |
| **Il cassiere** | Non tocca lo schermo, lo vede con la coda dell'occhio | Che lo schermo segua la cassa senza ritardi e senza fare cose strane mentre lavora |
| **Il titolare o il gestore** | Configura dal back office, sul PC della cassa o (se abilitato) dal telefono; non è un grafico | Che il locale abbia il suo aspetto e le sue promozioni senza poter fare brutte figure: contrasti sbagliati, QR illeggibili, foto tagliate |
| **Il tecnico Bluetech** | Installa: casse, dispositivi abbinati con un codice, stampanti, rete, diagnosi | Vedere subito cosa è collegato, cosa va in onda, cosa non va |
| **Bluetech** | Il fornitore | "powered by Bluetech" sempre visibile, in ogni stato e tema |

## 3. Dove gira: dispositivi e formati

| Dispositivo | Come | Formati tipici |
|---|---|---|
| Secondo monitor del PC della cassa | Player Windows (WebView2, a schermo intero, non ruba il focus alla cassa) | 1920×1080, 1366×768, 1280×800, 1024×768 (15″ e 10″) |
| Tablet sul banco | App installata dal browser (PWA, schermo intero) | iPad 1180×820, **iPad Pro 1376×1032 (4:3 ma grande)**, Android 1280×800 |
| Palmare del cameriere girato verso il cliente | PWA | 360–430 × 740–930 |
| Totem verticale | Player o PWA | 1080×1920 |

Le varianti di layout (SPEC §9) si scelgono dal rapporto d'aspetto: **larga** da 1,45:1 in su (la SPEC dice 1,5;
abbiamo messo un margine perché un 1200×800 resti largo anche se il browser riporta un pixel in più), **stretta** sotto
(conto a tutta larghezza, niente colonna), **verticale** sotto 1:1, **palmare** se verticale e largo meno di 700 px.
Dal back office si può **forzare la variante per un singolo dispositivo**: l'iPad Pro è 4:3, ma la colonna accanto al
conto ci sta e il locale la vuole. Il formato si adatta, non si limita a scalare: ogni formato ha la sua tabella
tipografica (SPEC §7) e una tela fissa che poi si ingrandisce sullo schermo vero.

## 4. Vincoli che il design deve rispettare

### 4.1 Cosa arriva davvero dalla cassa

- **Descrizioni troncate**: 22 caratteri con il modello a 30 colonne (`Gelato alla crema con`), 40 con quello a 48.
  Maiuscole e minuscole come in anagrafica. Accenti resi con l'apostrofo (`Maesta'`): il file è ASCII.
- **Nessun codice articolo, nessuna foto**: `showLastItemPhoto` resta spento, non progettarci sopra.
- Quantità con decimali (unità di misura), prezzo unitario quando la cassa lo scrive (`2 X 3,00`).
- Gli sconti dello scontrino finale arrivano ripartiti sotto le righe; le promozioni della cassa sono righe di sconto.
- I coperti si ricavano da una riga "Coperto", se c'è.
- **Il totale lo dà la cassa**, sempre. Il metodo di pagamento arriva come testo della cassa (`CONTANTI`,
  `C.CREDITO`, `BANCOMAT`, `SATISPAY`, `BUONO`) e diventa "Contanti", "Carta", "Bancomat", "Satispay", "Buono pasto",
  con il resto se c'è.
- **Il QR di Satispay** arriva come immagine (49×49 moduli, senza quiet zone). **Nessun esito del pagamento.**
- I **messaggi del visore** (`TOTALE € 23,50`, `RESTO € 1,50`) sono due righe da 16 caratteri.
- Il ritmo: con il file tolto appena letto, le battute arrivano anche 3–4 al secondo.

### 4.2 Dove gira

- PC della cassa con grafica integrata, spesso vecchi: **solo CSS, si animano solo `transform` e `opacity`**, tre
  livelli di animazione (piena, leggera di default, ridotta), "riduci le animazioni" del sistema forza la ridotta.
- **Rete locale, spesso senza internet**: font, immagini e video stanno sul PC dell'Agent. Niente CDN, niente font
  remoti, niente librerie esterne.
- Browser: WebView2/Chromium su Windows, Safari su iPad e iPhone, Chrome su Android, anche vecchi WebView sui palmari.
- Nessun `alert`, `confirm` o `prompt`, nessuna schermata d'errore al cliente.

### 4.3 Invarianti che restano (CLAUDE.md del pacchetto)

Footer Bluetech sempre a schermo; il totale è l'elemento più grande; il QR non si anima, non si deforma, non si copre
(fondo bianco, quiet zone 4 moduli, pixel netti, lato minimo 1/3 dell'altezza); niente stato "pagamento riuscito";
l'area messaggi spinge la lista; connessione persa = l'ultima schermata resta, con una pillola grigia. Se una
proposta deve romperne uno (come lo scontrino digitale, §5.6), va scritto esplicitamente con il perché.

## 5. Lo schermo del cliente: cosa progettare

Per ogni stato: cosa fa oggi, cosa chiediamo.

### 5.1 Attesa

**Oggi**: la playlist del set "attesa" a tutto schermo. Gli elementi possono essere foto (secondi scelti), video
(quanto durano, muti) e **promozioni composte**: titolo, testo, immagine facoltativa, QR facoltativo con la sua
scritta. Una promozione senza immagine ha un fondo scuro con le luci del tema; con l'immagine, l'immagine velata.
Il QR sta a destra, grande. Sopra: il logo del locale, i puntini della playlist (solo se corta), il testo scorrevole.
Senza nessun set: logo, nome e benvenuto.

**Da progettare**:
- I modelli della slide promozione: solo testo, con immagine, con QR, con immagine e QR, in tutti i formati. Con il
  QR la slide deve funzionare a 1–2 m: misura, posizione, scritta, gerarchia tra titolo e QR.
- Come restano leggibili logo, testo scorrevole e footer sopra foto molto chiare o molto scure.
- Le transizioni tra foto, video e promozioni (oggi dissolvenza; Ken Burns sulle foto solo con le animazioni piene).
- Indicazioni per il locale sulle immagini: le foto con scritte già dentro si scontrano con il titolo della promozione
  (lo abbiamo visto con le prime prove).

### 5.2 Conto in corso

**Oggi**: card dello scontrino con le righe allineate in basso, le sei varianti di riga (SPEC §3), il totale a
contachilometri (SPEC §4), il conteggio di articoli e coperti. Le righe entrano una alla volta. Nella colonna laterale
(solo variante larga e verticale):
- se c'è un set "accanto al conto", i suoi elementi **a rotazione con i loro tempi**: foto o video 16:9, oppure la
  scheda della promozione (immagine in alto, titolo, testo, QR con la scritta);
- altrimenti logo e nome del locale;
- senza nemmeno quelli niente colonna, e il conto al centro.

**Opzione "prezzi solo sul conto"** (come il vecchio monitor): durante la battuta solo le descrizioni, niente importi
né totale; quando la cassa mostra il conto (preconto, scontrino) importi e totale entrano in dissolvenza.

**Da progettare**:
- La scheda laterale nelle sue quattro varianti (foto, video, promozione con e senza QR) e il passaggio da una
  all'altra.
- La colonna laterale vuota: oggi è logo e nome, ma si può fare meglio (e senza logo né nome il conto resta solo al
  centro di uno schermo largo).
- **Il conto senza prezzi**: con le righe allineate in basso e senza il totale, la card resta vuota in alto per metà
  (si vede nelle schermate). Serve una soluzione.
- La densità sui 10″ (`compactRows`, ancora aperto dal primo giro).

### 5.3 Messaggio dalla cassa

Come da SPEC §2.3: banda che spinge la lista, 6 s, etichetta e importo quando il testo li contiene. Nessuna richiesta
nuova, se non verificarla con i testi reali del visore (16 caratteri per riga).

### 5.4 Pagamento Satispay

Come da SPEC §2.4: accanto al conto (lo scontrino si stringe) o a tutto schermo, a scelta del locale; sempre a tutto
schermo sul 4:3, in verticale e sul palmare. Il QR arriva dalla cassa. Nessuna richiesta nuova sul QR. Da verificare:
il passaggio dal conto al pagamento con le animazioni leggere (oggi una dissolvenza).

### 5.5 Grazie

**Oggi**: in alto "Grazie e arrivederci" (testo del locale) e l'importo pagato con il metodo e il resto; **sotto, un
elemento del set "grazie"**, uno per cliente, a rotazione da un cliente all'altro (mai due QR nello stesso Grazie). Con
una promozione con QR il Grazie resta fino al conto dopo, al massimo i secondi scelti dal locale (30 di default),
altrimenti 8 s. Se la cassa manda il QR dello **scontrino digitale**, al posto della reclame c'è quello. Sulla
variante larga il Grazie sta nella colonna accanto allo scontrino ristretto; sul 4:3, in verticale e sul palmare,
con il QR, la foto della promozione resta fuori.

**Da progettare**: il Grazie con la reclame sotto in tutti i formati e le varianti di reclame (promozione con QR,
con immagine, con entrambi, foto, video), la celebrazione, il ritorno all'attesa. È **il momento in cui il QR rende
di più**: il cliente ha appena pagato, spesso con il telefono in mano. Il caso più importante è "Diventa cliente:
inquadra e registrati".

### 5.6 Scontrino digitale (stato `receipt`, opzionale)

**Oggi**: dopo il Grazie, se il locale l'ha attivato e la cassa manda il QR dello scontrino digitale: lo scontrino
come uscirà sulla carta (intestazione, righe, totale, pagamento, data, QR piccolo, dicitura), il QR grande da
inquadrare, e un pulsante "Preferisci la copia di carta? Tocca qui" con i suoi stati (sto stampando; ritira la copia
alla cassa; non riesco a stampare, chiedila alla cassa; già chiesta). Resta 45 s. Pensato per palmari e schermi touch.

**Da progettare**: tutto lo stato, nei formati palmare, verticale e largo. Deroghe da dichiarare: un pulsante vero (la
SPEC esclude l'interazione), il QR come protagonista al posto del totale, un QR piccolo sullo scontrino.

### 5.7 Connessione assente

Come da SPEC §2.6. Nessuna richiesta nuova.

### 5.8 Le pagine del dispositivo

Le prime cose che vedono il tecnico e il locale, oggi senza un disegno:
- **Abbinamento** (`/auth`): il dispositivo nuovo mostra un codice di 6 cifre valido 10 minuti, da scrivere nel back
  office; poi passa da solo allo schermo del cliente.
- **Dispositivo non autorizzato**: senza spiegare come entrare (sicurezza).
- **Invito a installare l'app** (PWA): una banda in basso, "Installa il monitor come app", con "Installa" e "Non ora".
- **Mi ricollego…**: la pagina quando l'Agent non risponde.

## 6. Il back office del locale: cosa progettare

Non è mai stato progettato. Oggi è una pagina web con schede, funzionale ma con l'aspetto di uno strumento tecnico.
Lo usano il titolare (non tecnico) e il tecnico Bluetech, quasi sempre sul PC della cassa (spesso 1366×768),
qualche volta dal telefono. Di default si apre solo dal PC dove gira l'Agent; dalla rete solo se il tecnico lo
abilita. Password diversa per ogni locale.

### 6.1 Le schede di oggi

| Scheda | Cosa c'è |
|---|---|
| Stato | Casse con lo stato del loro file, display collegati, ultime stampe, avvisi |
| Casse | Una per punto cassa, con il percorso del file di Passepartout |
| Dispositivi | Abbinamento con il codice; elenco con cassa mostrata, stampante della copia, **disposizione** (automatica, larga, stretta, verticale, palmare), collegato, ultimo accesso, copie; modifica e revoca |
| Stampanti | Le stampanti della copia di carta (rete o coda di Windows), larghezza, stampa di prova |
| Aspetto | Il locale (nome, sottotitolo, logo per i fondi scuri e per quelli chiari); il tema (4 temi, giorno, notte o automatico con le ore); i colori del locale con il controllo del contrasto (sotto 4,5:1 non salva e propone la correzione più vicina); caratteri, angoli, vetro, firma chiara o scura; sfondo (tinta, sfumato, luci, immagine, video); animazioni; QR del pagamento accanto o a tutto schermo; righe più grandi sul 4:3; prezzi solo sul conto; le scritte (benvenuto, titolo del conto, saluto, testo scorrevole, durata del Grazie con il QR) |
| Promozioni | Set di reclame, galleria delle promozioni, foto, video (sotto) |
| Opzioni | Chiusura del conto per inattività, scontrino digitale e copia di carta, rete e HTTPS, back office anche dalla rete |
| Statistiche | Per giorno e per cassa: vendite, pagate, incasso, QR di pagamento, scontrini digitali, copie |
| Log | Il registro dell'Agent, filtrabile |

Accanto ad Aspetto e Promozioni c'è l'**anteprima dal vivo** dello schermo del cliente: formato (16:9, 4:3,
verticale, palmare), fase (giro completo, attesa, conto, pagamento, Grazie), un conto di prova o una cassa vera, e
"cosa provare" (quello in onda adesso, un set qualunque anche spento o fuori orario, il set o la promozione che si sta
modificando). Sotto, gli errori che impedirebbero il salvataggio, in parole.

### 6.2 Promozioni e set: come funzionano

- **Tre gallerie.** Foto (fino a 15 MB) e video (fino a 200 MB, muti) caricati dal locale; promozioni con titolo
  (60 caratteri), testo (200), immagine dalla galleria, QR (un indirizzo: registrazione cliente, fidelity, recensioni,
  menu digitale) con la sua scritta (120), date di validità. Almeno una delle quattro cose.
- **Un set** mette in fila elementi delle gallerie per uno o più **posti** (attesa, accanto al conto, Grazie), con un
  **ordine** (come nell'elenco, a caso, per nome), i **secondi** di ogni elemento (un video dura quanto dura; per un QR
  il back office propone 15 s) e la regola **"una promozione con il QR dopo ogni N elementi"** (a caso con 2: due
  slide, un QR, due slide, un altro QR). Vale per alcune casse, giorni della settimana, ore (anche a cavallo della
  mezzanotte) e date.
- **Per ogni posto va in onda il primo set dell'elenco** che vale in quel momento e ha qualcosa da mostrare: in alto
  quelli con l'orario più stretto (l'aperitivo, il venerdì sera), in fondo quello di sempre. Il back office dice cosa
  è in onda adesso su ogni cassa e com'è ogni set: in onda, coperto (vale, ma vince uno più in alto), fuori orario,
  vuoto, spento.
- Limiti: 30 set, 100 elementi per set, 200 promozioni, 500 file, 3 GB.

### 6.3 La struttura della configurazione oggi, da rivedere

Due file sul PC dell'Agent, niente database: si copiano per fare una copia di sicurezza o per spostare l'installazione.

- `appsettings.json`, accanto all'exe: solo l'installazione (cartelle dei dati, del log e del display, giorni di log).
  Non si tocca dal back office e quasi mai a mano.
- `dati\impostazioni.json`: **tutto quello che si cambia dal back office**. Accanto ci sono `dati\media\` (foto e
  video, con l'impronta del contenuto come nome), `dati\statistiche.json` e il certificato HTTPS generato.
- `dati\nuova-password.txt`, solo quando serve: la password scritta dall'installazione o dal tecnico che la deve
  reimpostare. All'avvio l'Agent ne salva l'hash in `impostazioni.json` e cancella il file.

L'albero di `impostazioni.json`, con il livello a cui vale ogni parte:

```
impostazioni.json
├─ Password (solo l'hash)                         installazione
├─ BackOfficeDallaRete                            installazione        vale subito
├─ Rete { Porta, ApertoInRete, Https }            installazione        al riavvio dell'Agent
├─ ChiusuraVenditaDopoSecondi (60)                tutte le casse
├─ ScontrinoDigitale { MostraScontrino,           tutte le casse
│    CopiaCartacea, SecondiAVideo, Dicitura }
├─ Casse[]        { Id, Nome, FileCassa }         per cassa
├─ Stampanti[]    { Id, Nome, Collegamento, Indirizzo, LarghezzaMm, Colonne,
│                   TabellaCaratteri, QrPuntiPerModulo, Taglio, TestoRitiro, TimeoutSecondi }
├─ Dispositivi[]  { Id, Nome, Token (hash),       per dispositivo      cassa e stampante: il display si ricollega;
│                   Cassa, Stampante, Disposizione, AutorizzatoIl }    disposizione: subito, senza ricollegarsi
└─ Aspetto                                        tutto l'Agent (tutte le casse e tutti gli schermi)
   ├─ il locale    NomeLocale, Sottotitolo, Logo, LogoChiaro
   ├─ il tema      Tema, Modo, GiornoDalle, GiornoAlle, Colori (8), FontTitoli, FontTesto, Raggio, Vetro, Footer
   ├─ lo schermo   Fondo, FondoMedia, Animazioni, QrPagamento, RigheCompatte, PrezziSoloSulConto
   ├─ le scritte   Benvenuto, Scorrevole, TitoloConto, Grazie, SecondiGrazie
   ├─ Promozioni[] { Id, Nome, Titolo, Testo, Immagine, Qr, QrDidascalia, DalGiorno, AlGiorno }
   ├─ Set[]        { Id, Nome, Attivo, Dove[], Ordine, QrDopo, Secondi, Elementi[{ Id, Secondi }],
   │                 Casse[], Giorni[], Dalle, Alle, DalGiorno, AlGiorno }
   └─ Media[]      { Id, Nome, Tipo, Byte, CaricatoIl }
```

Come si comporta:
- **Tutto vale subito**, sui display collegati e senza ricaricare, tranne la rete (al riavvio, che il back office
  chiede e fa da sé).
- L'aspetto si salva con un pulsante, dopo averlo visto nell'anteprima; set e promozioni si salvano a ogni modifica;
  le altre schede a ogni modulo.
- Ogni salvataggio è controllato e i problemi si dicono in italiano. Si rifiuta solo una modifica che aggiunge un
  problema: chi ha un errore vecchio non resta bloccato. Il file precedente resta come `.bak`; un file scritto a mano
  male si ripara all'avvio e il log dice cosa è cambiato.
- Gli elenchi si legano per id: un dispositivo punta a una cassa e a una stampante, un set a foto, video e
  promozioni. Una cassa eliminata spegne i set che erano solo suoi; un file usato non si elimina.

Dove la struttura mostra i suoi limiti (da rivedere, non solo da ridisegnare):
- **L'aspetto è uno per tutto l'Agent.** Un locale con due marchi sulla stessa installazione (bar e ristorante) non
  può avere due aspetti; le casse filtrano solo i set.
- **Niente ereditarietà.** Non c'è un "default del locale, cambiato per questa cassa o questo schermo": un palmare
  non può avere le animazioni ridotte se il totem le ha piene, una cassa non può avere un altro benvenuto.
- **La divisione tra le schede segue il codice, non la testa del titolare**: lo scontrino digitale e la chiusura del
  conto sono in Opzioni anche se cambiano lo schermo del cliente; la durata del Grazie con il QR è in Aspetto, quella
  dello scontrino digitale in Opzioni.
- **Un solo ruolo**: la stessa password per il titolare e per il tecnico, e le stesse schede per tutti e due.
- Le stampanti sono entità a parte, usate solo dalla copia di carta, e si scelgono dispositivo per dispositivo.

Proponete una struttura rivista: livelli (installazione, locale o marchio, cassa, schermo), cosa eredita da cosa,
quali impostazioni spostare, cosa vede il titolare e cosa il tecnico. Vincoli: resta un file JSON che il tecnico può
leggere e copiare; le impostazioni di oggi si devono poter convertire da sole al primo avvio (lo facciamo già per le
promozioni della prima versione).

### 6.4 Da progettare

- **L'architettura**: oggi nove schede allo stesso livello. Il titolare usa Aspetto e Promozioni; il tecnico tutto
  il resto. Probabilmente servono due livelli (il locale, l'installazione).
- **Il primo avvio guidato**: password, la cassa, il primo schermo abbinato, l'aspetto, il primo set. Oggi si
  arriva a tutto da soli.
- **I set nel tempo**: la regola "vince il primo che vale" è potente ma astratta. Serve una vista che la renda
  evidente (una giornata o una settimana con cosa va in onda quando, per posto e per cassa), e un modo semplice di
  dire "il venerdì sera al posto di quello di sempre".
- **L'editor della promozione** con l'anteprima vera nei tre contesti (slide, accanto al conto, Grazie) e
  l'avviso quando una combinazione non rende (foto con scritte dentro, QR con una scritta troppo lunga).
- **Le gallerie**: caricamento trascinando i file, selezione multipla, dove è usato ogni file, filtri.
- **Aspetto**: le schede dei temi, l'editor dei colori con il contrasto, i due loghi, lo sfondo.
- **Dispositivi**: l'abbinamento con il codice, lo stato dal vivo, la disposizione forzata.
- **Stati vuoti, errori, salvataggi** (set e promozioni si salvano da soli; l'aspetto con un pulsante: oggi la
  differenza non si vede abbastanza).
- **Tutto anche a 390 px di larghezza**, per il telefono del titolare.
- I messaggi d'errore arrivano dall'Agent già in italiano e chiari ("Il set Aperitivo: gli orari sono come 18:00 e
  20:00"): vanno mostrati bene, non riscritti.
- La struttura della configurazione rivista (§6.3), riflessa nell'architettura del back office.

## 7. Le richieste dei locali finora

In ordine di arrivo, tutte già fatte salvo dove indicato:

1. **Prezzi solo sul conto**, come il vecchio monitor (`ShowPricesDuringOrder=false`).
2. **Un logo per i fondi chiari**: un logo chiaro su un tema di giorno non si vede.
3. **Promozioni con il QR**: "faccio vedere due promozioni e poi lo invito a registrarsi come cliente con il QR".
4. **Modi di rotazione**: in ordine, a caso, alfabetico; a caso, il QR ogni tot elementi.
5. **Promozioni anche accanto al conto e nel Grazie**, non solo nell'attesa.
6. **Gallerie di foto, video e promozioni, e set** per l'attesa, il conto e il Grazie, per cassa, giorni e orari.
7. **Nel Grazie la promozione sotto il saluto e il totale**, spostati in alto per farle spazio.
8. **Niente scheda grigia vuota** accanto al conto.
9. **L'iPad Pro e gli schermi 1200×800 devono usare la disposizione larga**, con la colonna; da qui lo switch per
   dispositivo.
10. **Le animazioni si devono vedere**: righe che entrano, totale che scorre.
11. **Buoni sconto** (da fare dopo): un codice diverso per ogni cliente nel QR, da usare alla cassa. Serve un servizio
    su internet: proponete come si vedrebbe, sullo schermo e nel back office.

## 8. I dati a disposizione

L'Agent manda al display il messaggio `cfg` (aspetto, già scelto per la cassa, il giorno e l'ora) e i messaggi del
conto. Il dettaglio è in `design/ALLINEAMENTO.md` §8; qui l'essenziale per il design.

| Campo di `cfg` | Contenuto |
|---|---|
| `theme`, `preset`, `dayHours` | I token del tema (SPEC §6), il tema di partenza, le ore del giorno per l'automatico |
| `venue` | `name` (40), `tagline` (60), `logo`, `logoLight` |
| `texts` | `welcome` (80), `receipt` (30, "Il tuo conto"), `thanks` (60, "Grazie e arrivederci") |
| `ticker` | Il testo scorrevole (500) |
| `playlists` | `idle`, `sale`, `thanks`: elementi `{ id, kind: image \| video \| promo, src?, durationMs? }`, già in ordine |
| `promoCards` | Per ogni promozione: `title`, `text`, `image?`, `qr?: { png, caption }` |
| `thanksSeconds`, `pricesOnlyOnBill` | Durata massima del Grazie con un QR; prezzi solo sul conto |

Messaggi del conto: apertura, righe (`item`, `discount`, `surcharge`, `void`, `note`, con id stabili), totale (con
`bill` vero quando la cassa mostra il conto e i coperti), chiusura con il pagamento (metodo, importo, resto), QR di
pagamento e la sua rimozione, QR dello scontrino digitale, stato `receipt` con la copia di carta, messaggio del
visore, disposizione forzata (`layout`), stato completo alla riconnessione, battito ogni 3 s.

## 9. Cosa consegnare

1. **Il sistema di design aggiornato**: token e componenti, compresi i nuovi (scheda promozione nelle sue varianti e
   nei tre contesti, cornici per foto e video, Grazie con la reclame, stato `receipt`, pagine del dispositivo) e
   quelli del back office (navigazione, elenchi, chip, editor, anteprima, badge di stato, stati vuoti).
2. **Lo schermo del cliente**: tutti gli stati nei formati 1920×1080, 1366×768, 1280×800, 1024×768, 1080×1920 e
   palmare 390×844, in almeno due temi di giorno e di notte. Priorità: Grazie con la promozione, Attesa con le
   promozioni, Conto con la colonna a rotazione e senza prezzi, Scontrino digitale.
3. **La struttura della configurazione rivista** (§6.3): l'albero nuovo con i livelli e le eredità, cosa si sposta
   rispetto a oggi e perché, chi vede cosa.
4. **Il back office**: architettura e tutte le schede a 1366×768 e a 390 px, più questi percorsi completi:
   - primo avvio;
   - creare "Diventa cliente" con il QR e metterla nel set del Grazie;
   - un set "Aperitivo" il venerdì 18–20 che sostituisce quello di sempre;
   - abbinare un iPad Pro e forzarne la disposizione larga;
   - cambiare i colori con un contrasto sbagliato e la correzione proposta.
5. **Il motion** aggiornato (MOTION.md):
   - ingresso della slide promozione;
   - rotazione della colonna laterale;
   - Grazie con la reclame;
   - importi che compaiono quando la cassa mostra il conto;
   - stato `receipt`;
   - per ognuno, i tre livelli.
6. **SPEC aggiornata** con misure esplicite, le deroghe agli invarianti dichiarate e i testi (microcopy) in italiano.
7. **Un prototipo** con i comandi del primo giro più: cambia set, promozione con QR, Grazie con QR, scontrino
   digitale, prezzi solo sul conto, disposizione forzata.

## 10. Dove pensiamo che si possa fare meglio

Domande aperte, su cui aspettiamo una proposta più che un'esecuzione:

- **Il QR nell'attesa rende poco**: lo schermo è lontano e nessuno ha il telefono in mano. Il posto buono è il Grazie,
  e un po' la colonna durante il pagamento. Proponete come guidare il locale verso i posti giusti senza vietargli
  niente.
- **Misurare**: il locale non sa quanti inquadrano un QR. Oggi suggeriamo un `utm` nell'indirizzo; con il servizio
  dei buoni sconto si potrebbero contare le scansioni. Che cosa mostrereste nelle statistiche?
- **La colonna laterale senza set** e **il conto senza prezzi** (§5.2).
- **Un aspetto per cassa o per marchio** (§6.3): come cambierebbe il back office, e come si spiega al titolare che
  una cosa vale "per tutto il locale salvo questa cassa".
- **Le foto del locale**: quasi mai sono pensate per uno schermo con un titolo sopra. Si può aiutare il titolare
  (ritaglio, zone sicure, avvisi) senza farlo diventare un grafico?
- **L'etichetta "Promozione"** sopra il titolo: per "Diventa cliente" o "Lasciaci una recensione" suona sbagliata.
  Serve un'etichetta scelta dal locale, o nessuna?

## Allegati da dare insieme a questo brief

- Le schermate dello stato attuale (schermo del cliente nei quattro formati, back office).
- Il pacchetto del primo giro: `design/handoff/` (SPEC.md, MOTION.md, `src/tokens/*`,
  `src/types/protocol.ts`) e `design/ALLINEAMENTO.md`, se Claude Design può leggere i file. Il canvas del primo
  giro è <https://claude.ai/artifact/1Vqr5fioAwP73svEAoiMrD>.
- Gli asset ancora mancanti dal primo giro: logo Bluetech ufficiale (chiaro e scuro), logo Satispay dal brand kit.
- 3–4 foto vere di locali clienti e un logo di un locale vero (come segnaposto, senza dati reali da pubblicare).
