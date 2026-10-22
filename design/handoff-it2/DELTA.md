# Iterazione 2 — cosa cambia rispetto all'iterazione 1

Ogni riga: cosa cambia, perché, dove sta scritto. Il "perché" quasi sempre è una delle sei
patologie viste nelle 81 schermate del prodotto in esercizio
(`design-reference/iterazione-2/Main.dc.html`):

| # | Patologia | Dove si vede |
|---|---|---|
| 01 | due titoli sulla stessa slide (foto con scritte + titolo del sistema) | attesa, colonna |
| 02 | card dello scontrino vuota per metà con conti corti | conto, grazie |
| 03 | il ritaglio mangia le scritte delle foto | palmare, verticale, 4:3 |
| 04 | il nastro scorrevole tagliato netto sul bordo | attesa |
| 05 | lo scontrino digitale lascia vuoto il 45% dello schermo | receipt |
| 06 | QR dove non converte (accanto al conto, mani occupate) | colonna |

## Display

| Cambio | Perché | Dove |
|---|---|---|
| Card dello scontrino alta quanto il contenuto, ancorata in basso | 02 | SPEC §2.2 |
| Larghezza del conto in 24 colonne (9–17, default 16) invece di 1180 px fissi | richiesta del titolare: la colonna accanto di larghezza variabile | SPEC §17, `tokens.css` |
| Larghezza nel pagamento `beside` in 24 colonne (8–14, default 10) invece di 760 px | stessa griglia | SPEC §2.4, §17 |
| Allineamento verticale della colonna: alto / centro / basso | richiesta del titolare | SPEC §17 |
| Livelli di attrazione Sobrio / Vivo / Spettacolo / TROPPO, separati dalle animazioni | "dal sobrio al TROPPO" senza legare la pubblicità alla potenza del PC | SPEC §14, MOTION.md |
| Sette coreografie di riga invece di una | il tipo di riga si capisce senza leggere | MOTION.md |
| Accorpamento degli articoli ripetuti | due righe identiche nelle schermate | SPEC §3.2 |
| Prezzi solo sul conto | modalità nuova, ruoli invertiti | SPEC §2.2.1 |
| Grazie: lo scontrino si ritira in due righe; reclame dopo la celebrazione | 02, 06 | SPEC §2.5 |
| Stato `receipt` (scontrino digitale) con tre blocchi componibili | 05 | SPEC §2.7 |
| Scontrino digitale: ogni blocco in uno dei sei posti (3 colonne × 2 righe) | **decisione di Gianluca**: "ogni blocco in qualsiasi posizione" | SPEC §2.7, `E-Receipt-Posizioni` |
| Copia di carta: un pulsante vero sui soli schermi touch | richiesta del brief | SPEC §2.7 |
| Fondo a cinque strati, scrim fisso calcolato sul bianco, derivati del video dall'Agent | video sempre visibile senza perdere leggibilità | SPEC §15 |
| Quattro modelli di slide A–D decisi da una domanda al caricamento | 01 | SPEC §16 |
| Punto di messa a fuoco, fasce per il modello B, seconda foto verticale proposta | 03 | SPEC §16 |
| Nastro con maschera sfumata ai bordi (`mask-image`) | 04 | `iterazione-2/A-Attesa-Promo.dc.html` |
| Transizione scelta dalla coppia di elementi; "un QR ogni N"; QR 15 s | caroselli che sembrano la stessa slide | SPEC §16, MOTION.md |
| QR in colonna solo a TROPPO e a vendita ferma | 06 | SPEC §2.2, §14 |
| Dieci temi (5 scuri, 5 chiari) invece di 4 × giorno/notte | richiesta del titolare | SPEC §6, `themes.ts` |
| Formato palmare 390×844 | era nei dispositivi ma non nel design | SPEC §7, §9 |
| 4:3: colonna accanto al conto **solo testo** (era assente) | spazio per vendere senza toccare le righe | SPEC §9 — **confermato da Gianluca** |
| 4:3: QR `beside` ammesso (era sempre a tutto schermo) | a 288 px resta sopra il minimo di 256 | SPEC §2.4 |
| Minimo del totale misurato sul **lato corto** | in verticale "per 1080 di altezza" chiedeva 213 px | CLAUDE.md |
| Pagine del dispositivo: abbinamento, non autorizzato, installa, mi ricollego | esistevano, non erano disegnate | SPEC §21 |

## Configurazione e back office

| Cambio | Perché | Dove |
|---|---|---|
| `impostazioni.json` v3: installazione → marchi[] → casse[] → schermi[] | un solo piano oggi: niente multi-marchio, niente override per schermo | SPEC §19, `config.ts` |
| Regole riusabili separate dalle vetrine | "il venerdì sera" va riscritto in ogni set | SPEC §18 |
| Catena di decisione in quattro passi, una funzione sola | "perché vedo questo adesso?" | SPEC §18 |
| Cinque stati di vetrina, con "Coperta" | chiamate al supporto per vetrine "rotte" | SPEC §18 |
| Parole del titolare: vetrina, cosa gira, dove si vede | vocabolario del codice nel pannello | SPEC §18 |
| Due ruoli (titolare, tecnico), sezione Installazione invisibile al titolare | il percorso del file di Passepartout in mezzo alle promozioni | SPEC §20 |
| Da nove schede a sei + Installazione; anteprima come colonna fissa | le schede seguivano il codice | SPEC §20 |
| Primo avvio in cinque passi; funziona dal terzo | — | SPEC §20 |
| Back office completo anche a 390 px | il titolare accende una vetrina dal telefono in sala | SPEC §20 |
| Migrazione v2 → v3 automatica, con `.bak` e log | nessuno deve rifare la configurazione | SPEC §19 |

## Codice nel pacchetto

| File | Stato |
|---|---|
| `src/tokens/themes.ts` | **riscritto**: dieci temi, `resolveTheme`, `LEGACY_THEME`, `contrastRatio`, `--scrim-rgb`. Contrasti calcolati da script |
| `src/tokens/tokens.css` | **esteso**: palmare, griglia (flex-grow), attrazione, durate delle sette righe, scrim, receipt. Le larghezze fisse `--receipt-w` sono state tolte |
| `src/types/protocol.ts` | **esteso**: `receipt`, `DisplayLine`, `ScreenConfig`, `ShowcaseByPlace`, `DisplayOut`, `palmare` |
| `src/types/config.ts` | **nuovo**: `impostazioni.json` v3 e la firma di `ScegliVetrina` |

## Correzioni fatte durante l'iterazione 2

Per trasparenza: tre rapporti di contrasto sull'artboard dei temi erano stimati e
sbagliati (Giorno 15,3 → 16,9; Sale 15,1 → 16,3; Menta 16,0 → 15,2; Latte 14,7 → 14,8).
Nessuno scendeva sotto 4,5:1, ma ora sono tutti calcolati dallo stesso script di
`themes.ts`. Tre artboard chiamavano "Ridotta" il livello che altrove si chiama
"Leggera": uniformato.
