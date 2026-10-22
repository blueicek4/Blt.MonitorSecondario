# Bluetech Display — specifica funzionale

> **Iterazione 2 — ottobre 2026.** Parte dalle 81 schermate del prodotto in esercizio, non
> da zero. Le parti nuove o cambiate sono marcate **[IT2]**; l'elenco completo, con il
> motivo di ogni cambio, è in `DELTA.md`. Dove l'iterazione 1 e la 2 dicono cose diverse,
> vale la 2. Dove il codice in esercizio fa già bene una cosa che qui non è descritta,
> vale il codice.

Monitor rivolto al cliente, collegato alla cassa di ristoranti, pizzerie e bar che usano
Passepartout Menu. Pagina web a schermo intero in un browser in modalità kiosk, sul
display secondario del PC cassa, alimentata dalla cassa in tempo reale.

Il cliente guarda lo schermo da 0,5–2 m, per pochi secondi, mentre paga. Deve capire tre
cose in quest'ordine: **cosa sta pagando**, **quanto paga**, **come paga**. Pubblicità,
brand ed effetti vengono dopo, e solo se non disturbano.

---

## 1. Architettura logica

Una SPA sola, servita dalla cassa. Nessun router: lo schermo è una macchina a stati.

```
idle ──► sale ──► payment ──► thanks ──► idle
                                 └──► receipt ──► idle        [IT2]
```

`message` e `offline` **non sono stati**: sono livelli sovrapposti a qualunque stato. Un
messaggio dalla cassa durante il pagamento non deve smontare il QR, e la perdita di
connessione non deve cambiare schermata. Nel modello sono due campi indipendenti, non
rami dello stesso enum. È il punto dove una macchina a stati scritta male rovina tutto.

Stato completo: `DisplayState` in `src/types/protocol.ts`.

---

## 2. Gli stati (sei + scontrino digitale [IT2])

### 2.1 Attesa (`idle`)

Nessun conto in corso. Deve sembrare digital signage di alto livello, non uno screensaver.

- Playlist pubblicitaria a tutto schermo: immagini **7 s**, video per la durata nativa.
- Logo del locale in alto a sinistra (centrato in verticale), con parallasse di 12 px.
- Indicatore di playlist: trattini, quello attivo più largo.
- Testo scorrevole opzionale in basso, sopra il footer.
- Scrim a gradiente sopra la slide: senza, logo e ticker diventano illeggibili su
  un'immagine chiara. Alto 26% in testa, 42% in coda.
- Due slide in memoria alla volta, mai tre: la terza si decodifica a transizione finita.
- **[IT2]** Cosa gira lo decide la vetrina in onda per il posto `attesa` (§18). Come è
  fatta ogni slide lo decide il suo modello A–D (§16). Quanto si muove, l'attrazione (§14).

### 2.2 Conto in corso (`sale`)

Lo scontrino si compone in tempo reale.

- **[IT2]** Card dello scontrino a sinistra, larga `colonneConto` su 24 (default 16, §17);
  colonna laterale a destra che prende il resto. La larghezza fissa di 1180 px non esiste più.
- **[IT2] La card è alta quanto il suo contenuto e ancorata in basso.** Con tre righe non
  resta mezza card vuota (patologia 02 delle schermate): lo spazio sopra è la scena, non un buco.
- Intestazione card: etichetta "Il tuo conto" + conteggio articoli e coperti.
- Lista righe allineata in basso (`justify-content: flex-end`): le righe recenti stanno
  sempre nello stesso punto, vicino al totale.
- Con 30+ righe la lista mostra solo le ultime che entrano, con un gradiente di
  dissolvenza in testa e la dicitura "altre N righe sopra". Il totale resta sempre
  visibile: non scorre mai via.
- Blocco totale in fondo alla card, dominante.
- Colonna laterale: promozione del locale; se per l'articolo appena battuto esiste una
  foto, la foto occupa la parte alta (420 px) e la promozione si comprime sotto.

> **Nota di progetto**: la foto dell'articolo appena battuto è bella in demo ma su un
> conto da 30 righe sposta l'occhio via dal totale. **Default consigliato: spenta**,
> token `showLastItemPhoto: false`.

- **[IT2]** La colonna mostra la vetrina in onda per il posto `conto`: una scheda che
  ruota (12 s a Vivo, 8 s a Spettacolo, 5 s a TROPPO). **Mai durante l'ingresso di una
  riga**: se la cassa sta battendo, il cambio si rimanda di 1,5 s. Niente QR in colonna,
  tranne a TROPPO e solo dopo 4 s senza battute. A **Sobrio la colonna non esiste** e il
  conto prende tutta la larghezza.

#### 2.2.1 [IT2] Prezzi solo sul conto

Token `prezziSoloSulConto`. Mentre il cassiere batte, le righe compaiono **senza importi**
e senza totale; al loro posto, in basso, la scritta "Il totale compare quando il conto è
pronto". I ruoli si invertono: la scena (vetrina del posto `conto`) va al centro, la lista
diventa una colonna stretta di controllo. Quando la cassa manda `sale.total`, gli importi
entrano a cascata (MOTION.md) e il totale arriva per ultimo.

È l'**unica deroga** all'invariante "il totale è l'elemento più grande": vale solo prima
che il totale esista. Da quando esiste, l'invariante torna intera.

### 2.3 Messaggio dalla cassa (`message`, livello)

La cassa manda brevi messaggi da "visore": `TOTALE € 23,50`, `RESTO € 1,50`, `BUONGIORNO`.

- Banda piena larghezza, 150 px su 1080, fondo `--c-primary`, testo `--c-on-primary`.
- Si inserisce **sopra** la lista spingendola verso il basso. Non la copre mai.
- Due forme: con etichetta + importo quando il testo contiene una cifra riconoscibile;
  testo semplice altrimenti. Il parsing è una regex sul testo ricevuto, documentata e
  testata: se non riconosce nulla, forma semplice.
- Resta 6 s (o `ttl` se il messaggio lo porta), poi esce.
- Un messaggio nuovo mentre uno è a schermo **non** fa uscire la banda: sostituisce il
  testo con una dissolvenza incrociata di 180 ms, così non lampeggia.

### 2.4 Pagamento Satispay (`payment`)

Il QR arriva dalla cassa come PNG. Due modalità, scelte dal locale col token `qrLayout`.

**`fullscreen`** — il QR è l'unico protagonista. Colonna sinistra con importo (150 px),
istruzione "Inquadra il QR con l'app Satispay", logo Satispay; QR a destra, card bianca
540×540 con padding 68 px, QR 404 px.

**`beside`** — il QR prende lo spazio delle promozioni e **lo scontrino si restringe a
`colonneContoPagamento` su 24** (default 10; era 760 px fissi), con la tipografia ridotta (riga 22 px, importi 24 px) e il totale
sempre a schermo a 92 px, etichettato "Da pagare". Il QR sale a 488 px dentro una card
640×640 con padding 76 px.

Regole comuni:
- Fondo bianco puro sotto il QR. Quiet zone **4 moduli**. Lato minimo **1/3
  dell'altezza schermo**. Mai ruotato, deformato, animato o coperto da vetro, gradienti
  o overlay.
- `image-rendering: pixelated` e lato multiplo intero del numero di moduli: un QR
  interpolato è un QR che il telefono non legge.
- Animazione d'attesa **attorno** al QR, non sopra: anelli che si espandono dal bordo
  della card verso fuori, sfalsati.
- **Niente countdown e niente esito.** Vedi invarianti in `CLAUDE.md`.

**[IT2]** Su `narrow` (1024×768) `beside` **ora è ammesso**: con il conto a 10/24 il QR
sta a 288 px, sopra il minimo di un terzo d'altezza (256). Su `portrait` e `palmare`
`qrLayout` resta ignorato e vale `fullscreen`.

### 2.5 Grazie (`thanks`)

- Celebrazione: tre anelli nei colori del locale, 900 ms, una volta sola.
- "Grazie e arrivederci" a 124 px, più l'importo pagato.
- QR opzionale a destra (340 px di card, 252 di codice) con didascalia: il locale scegli
  se punta a Google, alla fidelity o al menu digitale.
- Dopo **8 s** ritorno automatico all'attesa con dissolvenza di 900 ms.
- Nessun coriandolo, nessuna particella: su grafica integrata costano quanto tutto il
  resto insieme e invecchiano male.
- **[IT2]** Lo scontrino si **ritira**: al posto della card, due righe di riepilogo
  (importo, metodo, resto, articoli, data). Niente card mezza vuota.
- **[IT2]** La reclame del posto `grazie` entra **dopo** la celebrazione (420 ms dalla
  fine degli anelli), mai insieme. Un elemento solo, mai due QR. Con QR la permanenza
  sale a 30 s (`secondiGrazie`), perché è il momento in cui il telefono è già in mano.
  A Sobrio: 8 s, nessuna proposta.

### 2.6 Connessione assente (`offline`, livello)

- Compare dopo **8 s** senza messaggi dalla cassa (`link: 'stale'`).
- Pillola discreta in basso a sinistra, grigia, con un puntino che respira in opacità
  (0,4 → 1 ogni 2 s). È l'unica animazione infinita permessa fuori dall'attesa.
- **L'ultima schermata resta intatta e immobile.** Il fondo animato si ferma.
- Riconnessione con backoff 1-2-4-8 s fino a 15 s. Alla ripresa la cassa rimanda lo
  stato completo, non un delta.
- Alla riconnessione la pillola esce in 300 ms e lo schermo riprende da dove era.

### 2.7 [IT2] Scontrino digitale (`receipt`)

Arriva con `receipt.show` dopo il Grazie, se il marchio lo ha attivo. Resta 45 s, poi attesa.

- **Tre blocchi indipendenti**: `carta` (la copia di cortesia impaginata dall'Agent, font
  monospazio, larghezza della stampante), `qr` (per scaricarlo sul telefono), `reclame`
  (la vetrina del posto `grazie`). Default: **carta in colonna 1, QR in colonna 2**.
- **Ogni blocco dove vuoi, su una griglia di sei posti** (decisione di Gianluca, ottobre
  2026): tre colonne × due righe. Ogni blocco acceso sceglie una colonna e una riga fra
  `alto`, `basso`, `piena`. Niente coordinate libere: una posizione in pixel scelta su
  16:9 non ha significato su un palmare.
  - La carta è sempre `piena` (uno scontrino lungo non sta in mezza colonna).
  - Due blocchi nella stessa colonna solo come `alto` + `basso`. Trascinare su un posto
    occupato **scambia** i due blocchi.
  - Le colonne vuote spariscono: la composizione resta centrata.
  - Il QR `piena` ha titolo e card 440 px (codice 332); `alto`/`basso` card 300 senza
    titolo (codice ≥ 220).
  - **Formati**: `narrow` come `wide` in scala 0,62, ma con tre colonne cade la reclame;
    `portrait` trasposto (colonne → righe dall'alto, alto/basso → sinistra/destra);
    `palmare` tutto in fila nell'ordine colonna → alto, piena, basso, senza reclame.
  - Quando manca spazio cade prima la **reclame**, poi la **carta**. Il QR mai.
  - Il pannello mostra la composizione su **ogni schermo abbinato** al marchio e scrive
    cosa non si vede dove ("sul palmare la reclame non si vede").
  - Validazione: almeno un blocco acceso.
  - Riferimento: `design-reference/iterazione-2/E-Receipt-Posizioni.dc.html`.
- Larghezze in `tokens.css` (`--receipt-paper-w`, `--receipt-qr-w`, `--receipt-gap`).
- **Deroga dichiarata**: qui il QR, non il totale, è l'elemento dominante. Il totale
  sta dentro la carta.
- **Copia di carta.** Sul monitor fisso è una riga scritta: "Preferisci la copia di carta?
  Chiedila alla cassa". Sugli **schermi touch con una stampante assegnata** è l'unico
  pulsante vero del prodotto, con quattro stati nello stesso posto e alla stessa altezza:
  *Voglio la copia di carta* → *Sto stampando* → *Ritirala alla cassa* | *Chiedi la copia
  alla cassa* (guasto: nessun errore rosso, il dettaglio va nel log dell'Agent). Una sola
  richiesta per vendita; nessuna conferma; una nuova vendita azzera lo stato.
- Il display manda `receipt.print.request`; l'esito torna con `receipt.print`.


---

## 3. Righe dello scontrino — sei varianti

| Variante | Resa |
|---|---|
| `item` normale | descrizione a sinistra, importo a destra |
| `item` con quantità | la quantità (`×2`) compare **solo se > 1**, fra descrizione e importo, in `--c-muted` |
| `discount` | colore `--c-accent`, importo con **meno tipografico U+2212**, non un trattino |
| `surcharge` | stesso accento, segno `+` |
| `void` (storno) | descrizione e importo barrati, colore `--c-faint`, badge `STORNO` in pillola. Resta visibile 6 s poi esce dalla finestra |
| `note` (testo libero) | corsivo, `--c-muted`, nessun importo, indentata di 28 px sotto la riga che commenta |

Ogni variante ha **un ingresso suo** [IT2]: articolo, quantità, articolo già presente,
variante/nota, sconto, maggiorazione, storno. Le sette coreografie sono in `MOTION.md`.

Regole:
- Importi allineati a destra in colonna a larghezza fissa (150 px su 1920), descrizioni
  a sinistra, **nessun puntino di riempimento**.
- Tutti i numeri sono tabellari: `font-variant-numeric: tabular-nums`.
- Mai maiuscolo forzato sulle descrizioni: arrivano dalla cassa come sono scritte.
- Una descrizione lunga va a capo su **due righe massimo**, poi ellissi. Mai ridurre il
  corpo del testo.
- `key` = `line.id`. Mai l'indice.

### 3.2 [IT2] Accorpamento degli articoli ripetuti

Oggi battere due volte lo stesso articolo produce due righe identiche (si vede nelle
schermate: "Cestino di tigelle 4 pezzi" compare due volte). Il display le **riunisce**:

- Chiave: stessa `desc`, stesso importo unitario (`amount / qty`), **nessuna nota
  attaccata** a nessuna delle due. Una riga con una variante ("senza lardo") non si
  accorpa mai: è un altro piatto.
- Si accorpa anche se non sono consecutive: la riga resta dove era la prima.
- La riga accorpata tiene `sourceIds`; uno storno che punta a uno qualunque riduce la
  quantità invece di barrare tutta la riga. Se la quantità arriva a zero, si barra.
- La quantità salta con il rimbalzo "articolo già presente" (MOTION.md).
- **È solo vista.** `lines` resta com'è arrivata dalla cassa, il totale resta quello
  della cassa. Token per spegnerlo: non serve, finché nessuno lo chiede.

---

## 4. Totale — contachilometri

- 520 ms, curva `--e-out`.
- Il totale si spezza in caratteri. Ogni cifra è uno `<span>` con `overflow: hidden`
  alto `1em` che contiene una colonna `0…9`; si imposta
  `transform: translateY(-N em)` e si lascia animare la transizione.
- **Solo le cifre che cambiano si muovono.** Da 17,40 a 18,90 si muovono unità e
  decimi, le decine stanno ferme. Le cifre identiche non ricevono una nuova `key`.
- Il contenitore fa un battito `scale(1 → 1.035 → 1)` di 260 ms, sfalsato di 120 ms: è
  ciò che l'occhio percepisce da due metri.
- Formato: `€ 18,90` — simbolo euro, spazio fine, virgola decimale. Via
  `Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' })`.
- Livello `eco`: sostituzione secca con dissolvenza di 140 ms.

---

## 5. Elemento condiviso fra stati

Fra `sale` e `payment` il totale **non scompare e non ricompare: si sposta**.

- Tecnica FLIP, 520 ms, curva `--e-move`. Hook `useFlip(ref, deps)`:
  `getBoundingClientRect()` prima del cambio di fase, confronto dopo il commit in
  `useLayoutEffect`, delta applicato come `transform`, rimosso al frame successivo con
  la transizione attiva.
- Vale **solo** per `TotalBlock`. Nei livelli `light` ed `eco` l'hook ritorna subito e
  si usa la dissolvenza: una transizione condivisa a 30 fps è peggio di nessuna.
- Nello stesso intervallo: la lista esce con `translateX(-40px)` + opacità in 280 ms
  (parte subito), il blocco QR entra con `scale(.92 → 1)` + opacità in 360 ms con
  160 ms di ritardo.

---

## 6. Branding — il sistema a token

Tutto il tema è un insieme di variabili CSS sul nodo radice. Il pannello del locale
scrive solo queste. Nessun layout, nessuna dimensione del totale, nessuna posizione del
footer è negoziabile: un tema sbagliato può rendere il monitor brutto, non illeggibile.

Token: `ThemeTokens` in `src/types/protocol.ts`. Valori: `src/tokens/themes.ts`.

**[IT2] Dieci temi**, cinque scuri e cinque chiari. Sostituiscono i quattro temi × due
varianti dell'iterazione 1 (migrazione in `LEGACY_THEME`, `themes.ts`). Si scelgono dalla
**sala**, non dalla tavolozza.

| Tema | Scuro/chiaro | Gemello in `auto` | Per |
|---|---|---|---|
| `bluetech` | scuro | `giorno` | il default |
| `brace` | scuro | `forno` | griglia, carne |
| `notturno` | scuro | **nessuno** | cocktail bar, locali serali |
| `riserva` | scuro | `sale` | fine dining |
| `onda` | scuro | `menta` | cucina di mare |
| `giorno` | chiaro | `bluetech` | il default chiaro |
| `forno` | chiaro | `brace` | pizzeria, trattoria |
| `latte` | chiaro | **nessuno** | caffetteria, forneria |
| `sale` | chiaro | `riserva` | fine dining di giorno |
| `menta` | chiaro | `onda` | bistrot, bar moderni |

Notturno e Latte non hanno gemello di proposito: un cocktail bar non apre a colazione.
Con `modo: 'auto'` restano fissi e il pannello lo scrive, invece di mostrare un
interruttore che non fa niente.

Le varianti chiare **non si derivano** dalle scure schiarendo: i primari sono scuriti
apposta per tenere il bianco sopra oltre 4,5:1. Sui temi chiari il vetro è 0 (ombra al
posto del bordo luminoso) e lo scrim sopra un video è **bianco**, 0,72 → 0,90.

Derivati del vetro:
```
--glass-alpha = glass × 0.0012     // glass 0–100
--glass-blur  = glass × 0.28 px
```
Sopra `glass: 40` il blur si applica solo nel livello `full`. Nelle varianti giorno il
vetro è 0 e al suo posto c'è un'ombra `0 2px 24px rgba(…, 0.06)`.

**Il pannello non può rompere la leggibilità**: prima di salvare, calcola il contrasto di
`cText` su `cBg` e di `cOnPrimary` su `cPrimary`; sotto 4,5:1 non salva e propone la
correzione più vicina. Lo stesso controllo gira all'avvio del monitor, perché un tema può
arrivare da un backup vecchio.

**Font**: solo famiglie open source con licenza che consente l'incorporazione, servite
come woff2 **dalla cassa**, non da un CDN, con `font-display: swap` e un fallback di
sistema a metriche vicine. Il monitor deve partire identico con il router staccato.

---

## 7. Tipografia

| Elemento | 1920×1080 | 1366×768 | 1280×800 | 1024×768 | 1080×1920 |
|---|---|---|---|---|---|
| Totale | 150 | 120 | 124 | 104 | 150 |
| Importo pagamento | 150 | 120 | 124 | 86 | 170 |
| Riga | 28 | 23 | 24 | 23 | 32 |
| Importo riga | 32 | 25 | 26 | 25 | 34 |
| Quantità | 23 | 18 | 19 | 19 | 24 |
| Etichetta sezione | 17 | 14 | 15 | 15 | 22 |
| Messaggio cassa | 104 | 80 | 84 | 70 | 110 |

**[IT2] Palmare 390×844**: totale 96, riga 18, importo riga 20, quantità 14, etichetta 13,
messaggio 34 (banda 64). Il minimo del totale si calcola sul **lato corto**
(120 × 390/1080 = 43): 96 è più del doppio, perché il palmare si guarda da vicino ma di
fretta. Misurato con il font vero: "€ 118,00" in Oswald 700 a 96 px occupa 323 px.

Non è una scala proporzionale: su 10″ il cliente è più vicino, quindi la riga cala meno
del totale. I valori stanno in `src/tokens/tokens.css`, selezionati da `data-format` sul
nodo radice.

**Limite noto, da decidere con il cliente**: a 1024×768 la riga da 23 px dà una cap
height di ~4,8 mm su un 15″ (leggibile a ~0,95 m) e di ~3,3 mm su un 10,4″ (leggibile a
~0,65 m). Il requisito "righe leggibili a 1 m" quindi **non tiene su un 10″**. Il totale
sta tranquillo in entrambi i casi. Token `compactRows: false` porta la riga a 30 px sul
formato `narrow`, perdendo due righe visibili. Default proposto: `false` → riga 23 px,
perché un monitor cliente da 10″ sta sul banco e il cliente è a 50–80 cm.

---

## 8. Componenti

Albero e firme delle props: `src/components/README.md`.

---

## 9. Varianti di layout — il layout si adatta, non scala

Tre varianti, scelte dal **rapporto d'aspetto**, non dalla larghezza in px:

| Variante | Rapporto | Struttura |
|---|---|---|
| `wide` | ≥ 1,5 | scontrino + colonna laterale; totale dentro la card, in fondo |
| `narrow` | 1,2 – 1,5 | **[IT2]** scontrino a `colonneConto`/24 + colonna **solo testo** (modello A); a Sobrio scontrino a tutta larghezza come prima |
| `portrait` | 0,5 – 1 | pubblicità sopra a larghezza piena, scontrino sotto |
| `palmare` | < 0,5 | **[IT2]** nessuna colonna; una riga promo sopra il totale, solo a vendita ferma da 4 s |

Cosa cambia passando a `narrow` (es. 1024×768):
- **[IT2]** La colonna resta ma perde la foto: una foto 16:9 in 305 px è un francobollo, e
  il ritaglio ne terrebbe il 40%. Solo testo, modello A. Le righe si misurano in altezza,
  non in larghezza: 645 px bastano per descrizioni da 40 caratteri a 23 px.
  *(Iterazione 1 e prodotto in esercizio: colonna assente. Il cambio è **confermato da
  Gianluca**, ottobre 2026. A Sobrio si torna esattamente al comportamento di prima.)*
- Padding da 64/56 a 40/34, testata da 60 a 44 px, footer da 56 a 40 px con wordmark a
  15 px.
- Il totale passa su fondo pieno `--c-primary` invece del blocco in vetro.
- La banda messaggi passa da 150 a 104 px e continua a spingere la lista.
- **[IT2]** `qrLayout: 'beside'` ammesso (§2.4).
- Lo stato Grazie resta a due colonne: a 1024 px di larghezza testo + QR da 240 ci stanno.

Cosa cambia in `portrait` (1080×1920):
- Il footer Bluetech va **centrato** in basso, stessa altezza e stesso peso.
- Il logo del locale va centrato in alto.
- Nel pagamento: importo sopra, QR 660 px dentro una card 880×880, istruzione sotto su
  due righe.

Implementazione: container query sul rapporto d'aspetto, più un attributo `data-format`
sul root per la tabella tipografica. **Non** media query sulla sola larghezza: 1280×800
e 1280×1024 sono due layout diversi con la stessa larghezza.

Formati da verificare prima del rilascio: 1920×1080, 1366×768, 1280×800, 1024×768,
1080×1920, **[IT2]** 390×844. Lo schermo può forzare la disposizione
(`schermi[].override.disposizione`): un iPad Pro 1376×1032 è `narrow` per il rapporto dei
lati ma regge benissimo `wide`.

---

## 10. Accessibilità

- Contrasto minimo **4,5:1** per tutto il testo, in tutti e dieci i temi **[IT2]**.
  Calcolato da script (non stimato): testo su fondo da 13,7:1 a 17,1:1; `onPrimary` su
  primario da 4,6:1 a 8,1:1; accento su superficie piena da 5,3:1 a 13,2:1; `faint` ≥ 4,6:1
  sia su fondo sia su superficie. I valori per tema sono nei commenti di `themes.ts`.
- **[IT2]** Il pannello ricalcola il contrasto a ogni modifica di colore e **non salva**
  sotto 4,5:1: propone il colore più vicino che passa. Lo stesso controllo gira all'avvio
  del display, perché un file può essere stato scritto a mano.
- Elementi reali: `<button>`, `<a href>`, `<input>` + `<label>` anche dove sembrano
  decorativi. Mai `role` o `onClick` su un `div`.
- `aria-label` sui controlli con sola icona. SVG decorativi `aria-hidden="true"`, gli
  altri `role="img"` + `aria-label`.
- Il QR è `role="img"` con `aria-label` descrittiva.
- Colori che devono distinguersi differiscono anche in luminosità, non solo in tinta.

---

## 11. Kiosk e runtime

- Chromium `--kiosk --app` sul display secondario. Cursore nascosto, menu contestuale,
  selezione e zoom da tastiera bloccati.
- Riavvio automatico della pagina se il socket non torna entro 60 s.
- Il monitor tiene un anello degli ultimi 200 messaggi e le metriche fps, interrogabili
  dalla cassa. Serve al supporto per rispondere a "il monitor non si aggiorna" senza
  andare sul posto.
- **Degrado automatico**: un contatore su `requestAnimationFrame` misura i frame durante
  le animazioni. Se la media scende sotto 45 fps per 3 s consecutivi, il monitor scala
  al livello di animazione inferiore e lo scrive nel log. **Non torna su da sé**: lo
  rialza il tecnico, che a quel punto sa che quella cassa non ce la fa.

---

## 12. Protocollo

`src/types/protocol.ts`. **È una proposta, non una lettura della cassa reale.** Le quattro
domande da risolvere prima di implementare il trasporto sono in `CLAUDE.md`.

Regole di robustezza già decise:
- Ogni riga ha un `id` stabile, così lo storno può puntarla e React non rimonta la lista
  a ogni messaggio.
- `pay.qr.png` è un data URI o un percorso locale servito dalla cassa.
- Messaggi fuori sequenza (una `sale.line` senza `sale.open`) **aprono implicitamente la
  vendita** invece di essere scartati: in sala un monitor vuoto è peggio di un monitor
  con un dato in più.
- Ogni messaggio sconosciuto si logga e si ignora. Mai un errore a schermo.

---

## 13. Fuori scope

- Interazione del cliente: lo schermo non è touch e non ha controlli. **[IT2] Unica
  eccezione**: il pulsante della copia di carta sugli schermi touch (§2.7).
- Pagamenti diversi da Satispay (il protocollo ha `provider` per estenderlo).
- Scontrino fiscale e chiusure: restano sulla cassa. **[IT2]** La copia di cortesia e lo
  scontrino digitale invece sono in scope (§2.7); la stampa la fa l'Agent.

---

## 14. [IT2] Livelli di attrazione — asse A

Due assi indipendenti, che oggi si confondono:

- **Attrazione** — quanto il monitor vende. Scelta commerciale del titolare:
  `sobrio` · `vivo` (default) · `spettacolo` · `troppo`.
- **Animazioni** — quanto la macchina regge. Vincolo tecnico: `full` · `light` · `eco`
  (nel pannello: Piena, Leggera, Eco), con degrado automatico sotto 45 fps.

**TROPPO su una cassa lenta resta TROPPO**: stessa quantità di reclame, stessi QR, solo
fermi. Il titolare che ha comprato il pacchetto promozioni non deve vedersene mostrare
meno perché ha un PC vecchio.

Il movimento effettivo è il **più basso** fra: tetto dell'attrazione (sobrio→eco,
vivo→light, spettacolo e troppo→full), animazioni dello schermo, `prefers-reduced-motion`,
degrado fps. Il codice lo calcola e lo scrive in `data-motion`; l'attrazione va in
`data-attr` e porta solo i tempi di rotazione e gli effetti esclusivi di TROPPO.

| Voce | Sobrio | Vivo | Spettacolo | TROPPO |
|---|---|---|---|---|
| Fondo | tinta o immagine ferma | immagine o sfumato | video sempre, sfocato dietro il conto | video + mesh veloce |
| Attesa | logo, nome, scorrevole | playlist, dissolvenza 1,2 s | + Ken Burns, parallasse | slide da 5 s |
| Colonna accanto al conto | **assente** | ruota ogni 12 s | ogni 8 s | ogni 5 s |
| Ingresso riga | opacità 160 ms | scorrimento 300 ms | molla 420 ms | molla 480 ms, rimbalzo doppio |
| Sconti | colore | + ingresso dedicato | + traccia 600 ms | lampo sulla riga |
| Totale | secco | contachilometri | + battito | + respiro continuo |
| QR | pagamento e scontrino | + uno nel Grazie | + uno in attesa | + colonna a vendita ferma |
| Grazie | 8 s, niente | un anello, reclame | tre anelli, reclame grande, 30 s | tre ondate, nome a lettere |

**Non cambia mai, nemmeno a TROPPO**: il totale è l'elemento più grande; footer Bluetech
al suo posto; QR di pagamento immobile e scoperto; lo scontrino mai coperto da una
reclame; nessun effetto allunga il tempo per leggere il totale; contrasto ≥ 4,5:1.

---

## 15. [IT2] Il fondo: video, sfocatura, scrim

Cinque strati, dal basso:

1. **Media** a schermo intero, `object-fit: cover`, sovra-ritaglio 8% per il movimento.
2. **Sfocatura** `blur(22px)` **sulla card**, non sul video: sfocare un livello a
   schermo intero costa quattro volte tanto e serve solo sotto la card.
3. **Scrim** a gradiente **fisso**, 0,46 in alto → 0,72 in basso, calcolato sul caso
   peggiore (fotogramma bianco). In attesa scende a 0,20 al centro. Sui temi chiari è
   bianco, 0,72 → 0,90.
4. **Vetro** della card: `--glass-alpha` sopra lo scrim.
5. **Contenuto**, che non sa niente del fondo.

Lo scrim **adattivo** (luminanza misurata a ogni fotogramma) è stato **scartato**: serve
leggere i pixel da una canvas a ogni frame e produce una pulsazione quando la scena cambia.

Per livello: `full` video + `backdrop-filter`; `light` il video si ferma sul fotogramma
corrente quando si entra nel conto, card pretinta senza filtro; `eco` nessun video, si usa
il **primo fotogramma già sfocato e scurito dall'Agent** al caricamento. Il display non
sfoca mai un'immagine a runtime.

Video accettati: 1080p30 H.264, 20–40 s, senza audio, ≤ 200 MB, loop che chiude, niente
scritte, niente visi in primo piano.

---

## 16. [IT2] Reclame: modelli, ritaglio, rotazione

**Quattro modelli di slide**, decisi da una domanda sola al caricamento — *"questa foto
ha già delle scritte dentro?"* — non dal titolare:

| | Modello | Regola |
|---|---|---|
| A | Solo testo | fondo dalle luci del tema. Su 10" legge meglio di qualunque foto |
| B | Foto parlante | il sistema **non aggiunge titoli**: al massimo una riga di servizio in fascia bassa |
| C | Foto muta | titolo del sistema su scrim laterale, sempre nella metà sinistra |
| D | Con QR | QR = 1/3 dell'altezza (190 px su 1080) in un blocco suo, didascalia **sotto**, max 2 righe / 120 caratteri |

Regole dure: un titolo per slide; titolo ≤ 60 caratteri (oltre: scende di un gradino, poi
ellissi, mai tre righe); un QR per slide; una slide con QR dura **almeno 15 s**; margine
64 px (40 a 4:3, 48 in verticale). L'etichetta sopra il titolo è una scelta fra sei
(Promozione, Novità, Questa settimana, Il consiglio della casa, Evento, Diventa cliente)
o nessuna — non un campo libero.

**Ritaglio.** Una foto 16:9 portata su 390×844 conserva il **26%** della larghezza: una
safe zone comune non esiste. Tre risposte: (1) **punto di messa a fuoco** cliccato al
caricamento, default; (2) per il **modello B** sui formati stretti, **fasce** nei colori
del tema invece del ritaglio; (3) una seconda foto verticale, **proposta** solo a chi ha
davvero schermi verticali. Il pannello mostra la foto nei quattro ritagli insieme.

**Transizioni**, scelte dalla coppia di elementi: foto→foto dissolvenza 1200 ms con Ken
Burns che non si ferma; promo→promo **spinta** laterale 520 ms (due testi in dissolvenza
sono illeggibili a metà); foto↔promo 900 + 200 + 420 ms (immagine, scrim, testo).

**Rotazione**: ordine casuale con **"un QR ogni N"** a cadenza fissa; il QR dura 15 s; un
video dura quanto dura e sospende il conteggio.

---

## 17. [IT2] Griglia a 24 colonne

- `colonneConto`: 9–17 su 24, default 16. Fermi non opzionali: sotto 9 le descrizioni
  della cassa vanno a capo, sopra 17 la colonna non regge un QR con la quiet zone. A 1366 e
  1280 il massimo è 16.
- `colonneContoPagamento`: 8–14, default 10. Vale solo mentre il QR `beside` è a schermo.
- `colonnaAllineamento`: `alto` | `centro` | `basso` (default). Non vale su verticale e
  palmare, e il pannello lo dice.
- La colonna accanto **non ha un token di larghezza**: prende il resto. Esporre due numeri
  che devono sommare a 24 è il modo più rapido per avere una striscia nera in mezzo.
- Implementazione con `flex-grow`, non con `grid` (`tokens.css`).

---

## 18. [IT2] Organizzazione dei contenuti

Quattro piani separati, che oggi vivono tutti nel "set":

| Piano | Cosa | Entità |
|---|---|---|
| **Cosa** | il contenuto | foto, video, promozione. Esiste una volta, non sa dove finirà |
| **Dove** | il posto | `attesa`, `conto`, `grazie`. Fissi: sono il prodotto |
| **Quando** | la regola | casse, giorni, ore, date. Riusabile fra vetrine |
| **Come** | la resa | tema, modello, attrazione. Appartiene allo schermo |

**La catena di decisione** — una funzione pura, la stessa nell'Agent e nel pannello
(`ScegliVetrina` in `src/types/config.ts`):

1. filtra le vetrine per **posto**;
2. filtra per **regola** (cassa, giorno, ora, date di validità);
3. scarta le **vuote** (nessun elemento valido oggi);
4. prende la **prima per ordine**; le altre valide sono **coperte**.

Il pannello la scrive in chiaro sotto ogni vetrina: *"In onda su Cassa 1 e Cassa 2,
accanto al conto, perché è la prima valida alle 19:14 del venerdì. Copre: Sempre."*

**Cinque stati**, sempre visibili: In onda · **Coperta** (con il nome di chi la copre,
cliccabile) · Fuori orario ("torna venerdì alle 18") · Vuota · Spenta.

Parole del titolare, non del codice: set → **vetrina**, playlist → **cosa gira**,
slot → **dove si vede**, media → **foto e video**.

Una promozione usata in più vetrine è **una**: modificarla le cambia tutte, e il pannello
lo dice prima di salvare. Si elimina solo se non è usata da nessuno.

---

## 19. [IT2] Configurazione — `impostazioni.json` v3

Resta un file JSON, leggibile e copiabile, senza database. Cambia l'albero: quattro
livelli con eredità (installazione → marchio → cassa → schermo), **più marchi** per
installazione (bar e ristorante nello stesso edificio), regole riusabili, override
espliciti per schermo, due ruoli (titolare, tecnico) con due hash.

Tipi completi in `src/types/config.ts`. Regole:

- **L'eredità la risolve l'Agent**, non il display. Il display riceve una `ScreenConfig`
  già piatta (`cfg`). Due implementazioni dell'eredità divergono, sempre.
- Nel pannello un valore ereditato si vede com'è e dice da dove viene ("dal locale");
  appena lo tocchi diventa tuo, con **"riporta al valore del locale"** accanto.
- **Migrazione automatica** v2 → v3 al primo avvio: Aspetto → `marchi[0]`; Dispositivi →
  `schermi` (Disposizione in `override`); casse/giorni/orari di ogni set → una `regola`
  col nome del set; set → `vetrina`; password → `accessi[0]` titolare; token nuovi ai
  valori di oggi (Vivo, 16, basso, carta+QR). Il file vecchio resta come
  `impostazioni.v2.bak`, il log scrive riga per riga cosa è stato spostato. Due regole
  identiche si fondono in una, col nome di entrambe.

---

## 20. [IT2] Back office

Da nove schede (che seguivano il codice) a **sei per il titolare** più una sezione che il
titolare **non vede**:

| Titolare | Era | Salva |
|---|---|---|
| Adesso | Stato | sola lettura — cosa è in onda ora, con la miniatura vera |
| Il locale | Aspetto | pulsante Salva (prima vuoi vedere l'anteprima) |
| Vetrine | Promozioni › Set | da sé |
| Contenuti | Promozioni › gallerie | da sé |
| Schermi | Dispositivi | da sé |
| Statistiche | uguale | sola lettura, + scansioni QR per vetrina |

**Installazione** (solo tecnico): casse e file del gestionale, stampanti, rete e HTTPS,
marchi, accessi, log. Entrando da titolare **non c'è**: non grigia, non bloccata, assente.

L'**anteprima** è una colonna fissa a destra in Adesso, Il locale, Vetrine e Contenuti,
con formato e fase selezionabili. A **390 px** non si toglie nessuna funzione: le sezioni
diventano una lista, l'anteprima un pannello a schermo pieno.

**Primo avvio**: cinque passi — password, cassa, primo schermo, il locale, la prima
vetrina (saltabile). Il monitor funziona già dal passo 3.

Riferimento visivo: `design-reference/iterazione-2/BO-*.dc.html`.

---

## 21. [IT2] Le pagine del dispositivo

Quattro schermate che non vendono niente e decidono se il prodotto sembra finito. Tutte
sul tema del locale.

- **Abbinamento**: codice di sei cifre grande (si legge dall'altra parte del banco),
  scade in 8 minuti e si rigenera da solo.
- **Non autorizzato**: "Questo schermo non è abilitato" detto con calma; non spiega come
  entrare, nessun rosso.
- **Invito a installare l'app**: banda sopra lo schermo vero, una volta per dispositivo,
  30 giorni di silenzio dopo "non ora".
- **Mi ricollego**: **solo all'avvio**, quando non c'è niente da mostrare. A vendita in
  corso vale §2.6.
