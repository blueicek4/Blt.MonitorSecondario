# Riferimento visivo — i sorgenti degli artboard

Qui ci sono i **23 artboard dell’iterazione 1 del canvas di design, come sorgente**. Sono la fonte da cui
leggere i valori esatti quando `SPEC.md` dice "28 px" e vuoi sapere dove va, oppure
quando un layout non ti torna.

## Cosa sono e cosa NON sono

Sono file `.dc.html`: HTML in un formato di authoring che dipende da un runtime
(`./support.js`, il tag `<x-dc>`, il blocco `<helmet>`, i `{{segnaposto}}`).
**Non si aprono nel browser così come sono** e non vanno copiati nel progetto.

Servono per **leggere**, non per eseguire. Ogni misura, ogni colore, ogni peso del font e
ogni durata sono scritti letteralmente nel markup inline. Sono più precisi di qualunque
screenshot.

Come usarli:

```bash
# tutti i valori di font-size dello stato Conto a 1920
grep -o 'font-size: [0-9]*px' design-reference/iterazione-1/S2-Conto-169.dc.html | sort -u

# la struttura della card scontrino
grep -n 'width: 1180px' design-reference/iterazione-1/S2-Conto-169.dc.html

# tutti i colori usati in un tema
grep -o '#[0-9A-Fa-f]\{6\}' design-reference/iterazione-1/S2-Conto-43.dc.html | sort -u
```

## Come tradurre il formato

| Nel `.dc.html` | Nel progetto React |
|---|---|
| `<helmet><style>…</style></helmet>` | va in `tokens.css` o nel CSS del componente |
| `<x-dc>…</x-dc>` | si elimina: dentro c'è il JSX |
| `style="…"` inline | stesso valore, ma come `var(--token)` dove il token esiste |
| `{{hole}}` | una prop o un valore da `renderVals()` |
| `<sc-for list="{{rows}}" as="r">` | `rows.map(r => …)` |
| `<sc-if value="{{cond}}">` | `cond && …` |
| `<script type="text/x-dc">class Component extends DCLogic` | la logica del componente |
| `$preview: {width, height}` | la dimensione del frame, non una regola CSS |

**I colori inline nei sorgenti sono letterali, non token.** Nel progetto vanno
sostituiti con `var(--c-*)` da `themes.ts`: un hex ripetuto a mano è un tema che non si
può cambiare dal pannello. I valori letterali servono solo a sapere *quale* colore.

## I 23 artboard

### Fondamenta
| File | Cosa guardare |
|---|---|
| `Main.dc.html` | pagina del sistema di design: token, 8 palette con i contrasti, scala tipografica, i 7 componenti con le 6 varianti di riga |
| `Motion.dc.html` | le tre curve disegnate, le sei coreografie, i tre livelli |
| `Note-Sviluppo.dc.html` | albero componenti, tipi, protocollo (è la versione prosa di `SPEC.md`) |

### Prototipo e temi
| File | Cosa guardare |
|---|---|
| `Prototipo.dc.html` | **il più utile**: contiene la macchina a stati completa in JS, le 8 palette come oggetto, la costruzione delle righe per variante, il generatore del pattern QR. È il riferimento per la logica, non solo per il layout |
| `Temi.dc.html` | gli 8 set di token applicati allo stesso layout |

### Gli stati a 1920×1080
| File | Stato |
|---|---|
| `S1-Attesa-169.dc.html` | attesa, playlist + ticker + parallasse |
| `S2-Conto-169.dc.html` | conto, tema notte — **il riferimento principale per il layout `wide`** |
| `S2-Conto-169-Giorno.dc.html` | lo stesso conto in variante giorno |
| `S3-Messaggio-169.dc.html` | banda messaggi + conto a 31 righe con finestra scorrevole |
| `S4-Satispay-169.dc.html` | QR a tutto schermo |
| `S4b-Satispay-Conto-169.dc.html` | QR accanto al conto ristretto (1180 → 760) |
| `S5-Grazie-169.dc.html` | celebrazione + QR recensione |
| `S6-Offline-169.dc.html` | ultima schermata congelata + pillola |

### Gli altri formati
| File | Formato |
|---|---|
| `S1…S6-*-43.dc.html` (6 file) | 1024×768, variante `narrow`, tema Trattoria giorno |
| `S2-Conto-1366x768.dc.html` | 1366×768 |
| `S2-Conto-1280x800.dc.html` | 1280×800, variante giorno |
| `V-Attesa.dc.html` | 1080×1920, tema Cocktail bar |
| `V-Satispay.dc.html` | 1080×1920, QR verticale |

`canvas.json` è l'indice del canvas: posizione e dimensione di ogni frame. Serve solo a
capire come erano disposti, non al progetto.

## Nove file contengono template loop

`Main`, `Prototipo`, `S4-Satispay-169`, `S4-Satispay-43`, `S4b-Satispay-Conto-169`,
`S5-Grazie-169`, `S5-Grazie-43`, `Temi`, `V-Satispay` usano `sc-for` e `{{holes}}`.
Nei file dei QR il loop genera il **pattern del codice**: nel progetto quel generatore
non serve, il QR arriva dalla cassa come PNG. Serviva solo per mostrare la geometria
corretta nel mockup.

## Il QR nei mockup è finto

Pattern con geometria corretta — finder pattern, timing pattern, alignment pattern, quiet
zone di 4 moduli — ma **non scansionabile**. Esiste solo per validare proporzioni e
leggibilità del layout.
