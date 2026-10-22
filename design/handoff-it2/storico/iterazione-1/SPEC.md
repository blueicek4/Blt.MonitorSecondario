# Bluetech Display — specifica funzionale

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
```

`message` e `offline` **non sono stati**: sono livelli sovrapposti a qualunque stato. Un
messaggio dalla cassa durante il pagamento non deve smontare il QR, e la perdita di
connessione non deve cambiare schermata. Nel modello sono due campi indipendenti, non
rami dello stesso enum. È il punto dove una macchina a stati scritta male rovina tutto.

Stato completo: `DisplayState` in `src/types/protocol.ts`.

---

## 2. I sei stati

### 2.1 Attesa (`idle`)

Nessun conto in corso. Deve sembrare digital signage di alto livello, non uno screensaver.

- Playlist pubblicitaria a tutto schermo: immagini **7 s**, video per la durata nativa.
- Logo del locale in alto a sinistra (centrato in verticale), con parallasse di 12 px.
- Indicatore di playlist: trattini, quello attivo più largo.
- Testo scorrevole opzionale in basso, sopra il footer.
- Scrim a gradiente sopra la slide: senza, logo e ticker diventano illeggibili su
  un'immagine chiara. Alto 26% in testa, 42% in coda.
- Due slide in memoria alla volta, mai tre: la terza si decodifica a transizione finita.

### 2.2 Conto in corso (`sale`)

Lo scontrino si compone in tempo reale.

- Card dello scontrino a sinistra (1180 px su 1920), colonna laterale a destra.
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

**`beside`** — il QR prende lo spazio delle promozioni e **lo scontrino si restringe da
1180 a 760 px**, con la tipografia ridotta (riga 22 px, importi 24 px) e il totale
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

Su `narrow` e `portrait` il token `qrLayout` **viene ignorato** e vale sempre
`fullscreen`: non c'è colonna laterale da cui prendere spazio.

### 2.5 Grazie (`thanks`)

- Celebrazione: tre anelli nei colori del locale, 900 ms, una volta sola.
- "Grazie e arrivederci" a 124 px, più l'importo pagato.
- QR opzionale a destra (340 px di card, 252 di codice) con didascalia: il locale scegli
  se punta a Google, alla fidelity o al menu digitale.
- Dopo **8 s** ritorno automatico all'attesa con dissolvenza di 900 ms.
- Nessun coriandolo, nessuna particella: su grafica integrata costano quanto tutto il
  resto insieme e invecchiano male.

### 2.6 Connessione assente (`offline`, livello)

- Compare dopo **8 s** senza messaggi dalla cassa (`link: 'stale'`).
- Pillola discreta in basso a sinistra, grigia, con un puntino che respira in opacità
  (0,4 → 1 ogni 2 s). È l'unica animazione infinita permessa fuori dall'attesa.
- **L'ultima schermata resta intatta e immobile.** Il fondo animato si ferma.
- Riconnessione con backoff 1-2-4-8 s fino a 15 s. Alla ripresa la cassa rimanda lo
  stato completo, non un delta.
- Alla riconnessione la pillola esce in 300 ms e lo schermo riprende da dove era.

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

Regole:
- Importi allineati a destra in colonna a larghezza fissa (150 px su 1920), descrizioni
  a sinistra, **nessun puntino di riempimento**.
- Tutti i numeri sono tabellari: `font-variant-numeric: tabular-nums`.
- Mai maiuscolo forzato sulle descrizioni: arrivano dalla cassa come sono scritte.
- Una descrizione lunga va a capo su **due righe massimo**, poi ellissi. Mai ridurre il
  corpo del testo.
- `key` = `line.id`. Mai l'indice.

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

**Quattro temi, ognuno in due varianti**, giorno e notte, con gli stessi nomi di token:

| Tema | Carattere |
|---|---|
| `bluetech` | neutro, moderno, tecnologico. Il default |
| `trattoria` | caldo, carta e terracotta, serif elegante |
| `fine` | scuro/avorio, oro, minimale, molto respiro, angoli vivi |
| `cocktail` | notte/giorno, neon, gradienti vivi |

Le due varianti **non si derivano l'una dall'altra schiarendo**: sono progettate
separatamente, perché i colori primari vanno scuriti nelle varianti giorno per tenere il
bianco sopra sopra 4,5:1. `mode: 'auto'` alterna sugli orari di apertura del locale (o su
un sensore di luce, se il monitor ne ha uno) con una dissolvenza di 900 ms, e non tocca
mai layout o dimensioni.

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
| `narrow` | 1,2 – 1,5 | scontrino a tutta larghezza; **colonna laterale assente**; totale sotto la lista |
| `portrait` | < 1 | pubblicità sopra, scontrino sotto |

Cosa cambia passando a `narrow` (es. 1024×768):
- Spariscono colonna laterale, foto articolo e spazio promozioni. Le promozioni tornano
  nella playlist dell'attesa, dove hanno tutto lo schermo.
- Padding da 64/56 a 40/34, testata da 60 a 44 px, footer da 56 a 40 px con wordmark a
  15 px.
- Il totale passa su fondo pieno `--c-primary` invece del blocco in vetro.
- La banda messaggi passa da 150 a 104 px e continua a spingere la lista.
- `qrLayout` ignorato: il QR va sempre a tutto schermo.
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
1080×1920.

---

## 10. Accessibilità

- Contrasto minimo **4,5:1** per tutto il testo, in tutti e otto i set di token.
  Verificato: testo su fondo da 13,7:1 a 17,1:1; bianco su primario da 4,6:1 a 7,1:1;
  accento su superficie da 5,2:1 a 14,3:1.
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

- Interazione del cliente: lo schermo non è touch e non ha controlli.
- Pagamenti diversi da Satispay (per ora il protocollo ha `provider` per estenderlo).
- Pannello di configurazione del locale: è un altro prodotto, questo ne consuma i token.
- Stampa dello scontrino, scontrino fiscale, chiusure: restano sulla cassa.
