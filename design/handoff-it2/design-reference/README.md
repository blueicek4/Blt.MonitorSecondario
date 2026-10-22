# Riferimento visivo

Due cartelle, una per iterazione. Il formato dei file e come tradurlo in React è spiegato
in `README-iterazione-1.md` e vale per entrambe.

Quando l'iterazione 1 e la 2 dicono cose diverse, vale la 2. Quando un sorgente e il
pacchetto (`SPEC.md`, `src/`) dicono cose diverse, vale il pacchetto.

## `iterazione-2/` — 39 artboard

### Diagnosi e sistema
| File | Cosa guardare |
|---|---|
| `Main.dc.html` | le sei patologie viste nelle schermate, con lo schemino di ognuna |
| `Attrazione.dc.html` | i due assi; la tabella voce per voce dei quattro livelli |
| `Fondo.dc.html` | i cinque strati, lo scrim fisso, cosa succede in Piena/Leggera/Eco |
| `Motion2.dc.html` | le sette righe animate dal vivo, le coreografie nuove, la tabella per livello |
| `Griglia.dc.html` | 24 colonne, i fermi, i quattro preset, l'allineamento |
| `Temi.dc.html` | i dieci temi sullo stesso layout, con coppie e rapporti |

### Reclame
| File | Cosa guardare |
|---|---|
| `Reclame-Modelli.dc.html` | modelli A–D, anatomia del D, regole dure |
| `Reclame-SafeZone.dc.html` | ritaglio sui quattro formati, le tre risposte |
| `Caroselli.dc.html` | transizioni per coppia, rotazione con "un QR ogni N", i tre posti |

### Stati a 1920×1080 (livello Spettacolo, animazioni Piena: il caso peggiore)
`A-Attesa-Video`, `A-Attesa-Promo`, `B-Conto`, `B-Conto-SenzaPrezzi`, `C-Pagamento`,
`D-Grazie`.

### Scontrino digitale
`E-Receipt-169` (con la striscia delle cinque composizioni), `E-Receipt-Lungo-169`,
`E-Receipt-Palmare`, `E-Receipt-Verticale`, `E-Receipt-Stati` (i quattro stati del
pulsante), **`E-Receipt-Posizioni`** (ogni blocco dove vuoi: editor a sei posti, regole,
sei composizioni, come si traduce sugli altri formati).

### Back office (1366×768 e 390 px)
`BO-Architettura`, `BO-Aspetto`, `BO-Promozioni`, `BO-Set-Timeline`, `BO-Schermi`,
`BO-PrimoAvvio`, `BO-Mobile`.

### Contenuti e configurazione
| File | Cosa guardare |
|---|---|
| `Contenuti.dc.html` | i quattro piani, la catena di decisione, i cinque stati, le parole |
| `Config.dc.html` | l'albero di `impostazioni.json` v3, cosa si sposta e perché, la migrazione |
| `Dispositivo-Pagine.dc.html` | abbinamento, non autorizzato, installa, mi ricollego |

### Formati e temi
| File | Cosa guardare |
|---|---|
| `F-Regole.dc.html` | **la tabella**: sei stati × sei formati, e la scala tipografica |
| `F-1366`, `F-1280`, `F-1024`, `F-1024-Pagamento`, `F-Verticale`, `F-Palmare` | il conto a grandezza vera in cinque temi, due chiari e tre scuri |

### Prototipo
`Prototipo2.dc.html` — macchina a stati con tutte le leve: dieci temi, quattro livelli
di attrazione, tre di animazione, quattro disposizioni, larghezza del conto 9–17,
prezzi solo sul conto, QR accanto o a tutto schermo. Il riferimento per la logica.

## `iterazione-1/` — 23 artboard

Vedi `README-iterazione-1.md`. Ancora valido per: sistema di design di base, messaggio
dalla cassa, connessione assente, le due modalità del QR.
