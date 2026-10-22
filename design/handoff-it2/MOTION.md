# Bluetech Display — motion design

> **Iterazione 2.** Le parti nuove sono marcate **[IT2]**: sette coreografie di riga al
> posto di una, le transizioni della reclame, la rotazione della colonna, i prezzi a
> cascata, la sequenza Grazie → reclame → scontrino, e come l'**attrazione** (SPEC §14)
> si combina con i tre livelli. Le tre curve restano tre.

Il monitor gira su grafica integrata, dietro un browser in kiosk, su una macchina che sta
anche stampando scontrini. Da qui la regola base: **si animano solo `transform` e
`opacity`**. Blur e mesh animato esistono solo nel livello `full`, e ogni effetto ha un
equivalente `eco` che resta bello da fermo.

Tutte le animazioni sono CSS. Nessuna libreria. L'unico JS che tocca il movimento è
l'hook FLIP del totale e il contatore fps.

## Le tre curve — nessuna di più

| Nome | Valore | Uso |
|---|---|---|
| `--e-spring` | `cubic-bezier(.22, 1.2, .36, 1)` | **solo** ingresso riga e foto articolo. La sovraelongazione dà la sensazione del "battuto adesso" |
| `--e-out` | `cubic-bezier(.16, 1, .3, 1)` | la curva di default: totale, messaggi, comparse, dissolvenze |
| `--e-move` | `cubic-bezier(.33, 1, .68, 1)` | **solo** elementi che si spostano fra due stati (il totale verso il blocco QR). Simmetrica, così la traiettoria si legge |

Ticker e Ken Burns usano `linear`: un movimento continuo con una curva ease sembra un
difetto.

## Le sei coreografie

### Ingresso di una riga — 420 ms, `--e-spring`

`translateY(18px) → 0` con `scale(.985) → 1`; l'opacità arriva a 1 nei **primi 180 ms**,
così il testo non galleggia trasparente. Le righe sopra scorrono con la stessa curva nello
stesso frame. **Una riga per volta, nessuno stagger**: il cassiere batte un articolo alla
volta e due righe che entrano insieme sembrano un bug.

- `light`: 300 ms, `--e-out`, nessuna sovraelongazione.
- `eco`: solo opacità, 160 ms, nessun movimento.

### [IT2] Sette righe, sette ingressi

Il cliente guarda lo schermo tre secondi. Il modo in cui una riga arriva gli dice già che
è uno sconto, che una pizza è stata tolta, che la quantità è salita a due.

| Riga | `full` | `light` | `eco` |
|---|---|---|---|
| **Articolo** | molla 420 ms, sale di 18 px | scorrimento 300 ms | opacità 160 ms |
| **Con quantità** | come articolo; il `×2` arriva **80 ms dopo**, scala 0,8 → 1 | insieme alla riga | nessun ritardo |
| **Già presente** (accorpato, SPEC §3.2) | la quantità rimbalza 340 ms (1 → 1,28 → 0,94 → 1) + lampo leggero della riga | rimbalzo ridotto, niente lampo | il numero cambia e basta |
| **Variante / nota** | **scivola da sinistra** 300 ms e si ferma a opacità 0,75 — un asse diverso dice "commento alla riga sopra" | 220 ms | opacità 140 ms |
| **Sconto** | come articolo, poi una **traccia d'accento** attraversa la riga in 600 ms | traccia 400 ms, meno intensa | solo colore |
| **Maggiorazione** | come articolo, colore d'accento, **nessuna traccia**: una brutta notizia si mostra, non si festeggia | uguale | uguale |
| **Storno** | l'unica uscita del prodotto: barratura 320 ms da sinistra, sbiadita 240 ms, resta 6 s, poi esce | barratura 220 ms, uscita secca | compare già barrata, poi sparisce |

- La traccia dello sconto è un elemento assoluto dentro la riga (`overflow: hidden`),
  animato con `translateX`. A TROPPO diventa un **lampo** sull'intera riga (`bt-flash`).
- Storno: **il totale si aggiorna all'inizio della barratura**, non alla fine. Il numero
  non aspetta l'animazione.
- Il collasso dopo i 6 s **non** si anima con `height`: la riga esce in opacità, viene
  tolta, e le righe sopra scivolano con FLIP (`transform`). Stessa tecnica del totale.
- In `eco` le sette restano distinguibili per colore, barrato, rientro e moltiplicatore.
  Si perde il movimento, non l'informazione: è la prova che non è decorazione.

### Aggiornamento del totale — 520 ms, `--e-out`

Contachilometri. Specifica completa in `SPEC.md` §4.

- `light`: scorrimento 320 ms, nessun battito del contenitore.
- `eco`: sostituzione secca con dissolvenza 140 ms.

### Cambio di stato — 520 ms, `--e-move`

Il totale è elemento condiviso (FLIP). Cronologia:

```
0 ms        lista esce          translateX(-40px) + opacity, 280 ms
0–520 ms    totale migra        FLIP, --e-move
160–520 ms  blocco QR entra     scale(.92 → 1) + opacity, 360 ms
560 ms      attesa attorno al QR parte
```

Il QR non si anima mai da solo: si anima il contenitore, e l'immagine è già a pieno
contrasto quando inizia a essere leggibile.

- `light`: nessun FLIP, dissolvenza incrociata 320 ms.
- `eco`: taglio secco con dissolvenza 160 ms.

### Playlist pubblicitaria

- Immagine **7 s**, video per la durata nativa.
- Transizione **1200 ms** di dissolvenza, due slide sovrapposte.
- Ken Burns: `scale(1 → 1.08)` più `translateX` di 8–14 px su tutta la durata della
  slide, `linear`, **direzione alternata** fra una slide e la successiva.
- Parallasse: logo del locale e ticker si muovono di 12 px in controfase.
- La slide successiva si decodifica con `decode()` **prima** di entrare, così la
  dissolvenza non parte su un'immagine non pronta.
- `light`: dissolvenza 600 ms, nessun Ken Burns, nessuna parallasse.
- `eco`: dissolvenza 200 ms, slide immobile; i video non partono, si usa il primo
  fotogramma.

### Celebrazione finale — 900 ms, una volta sola

Tre anelli nei colori del locale: `scale(.6 → 1.9)` con opacità `.9 → 0`, 1800 ms,
sfalsati di **140 ms**. Sopra, "Grazie e arrivederci" entra con `translateY(24px)` in
520 ms, `--e-out`. Nessun coriandolo, nessuna particella.

- `light`: un solo anello.
- `eco`: il fondo si schiarisce verso il primario per 300 ms e torna; il testo entra solo
  in opacità.

### Messaggio dalla cassa

Entra in **320 ms** con `translateY(-16px)` + opacità, spingendo la lista con la stessa
curva. Resta 6 s, esce in 240 ms. Messaggio nuovo mentre uno è a schermo: dissolvenza
incrociata del testo di 180 ms, la banda non esce.

### Connessione assente

Pillola in dissolvenza di 400 ms dopo 8 s di silenzio. Il puntino respira in opacità da
0,4 a 1 ogni 2 s, `ease-in-out`: unica animazione infinita permessa fuori dall'attesa, e
non si vede dal tavolo. Alla riconnessione esce in 300 ms.

### [IT2] Reclame: la transizione la sceglie la coppia

| Da → a | Transizione |
|---|---|
| foto → foto | dissolvenza 1200 ms; il **Ken Burns non si ferma**: la seconda entra già in movimento, in direzione opposta |
| promo → promo | **spinta** laterale 520 ms, sempre da destra a sinistra: in ogni istante un solo testo a piena opacità |
| foto ↔ promo | tre tempi: immagine 900 ms, scrim 200 ms, testo 420 ms (sale di 24 px) |

Ingresso di una slide promozione: l'immagine in 1200 ms, il testo **260 ms dopo**, il QR
per ultimo a 460 ms e **solo in opacità** — un QR che si muove non si inquadra.

### [IT2] Rotazione della colonna accanto al conto

La scheda esce verso l'alto, la successiva entra dal basso, 420 ms, 120 ms di
sovrapposizione. **Mai durante l'ingresso di una riga**: se la cassa sta battendo il cambio
slitta di 1,5 s. Due cose che si muovono in punti diversi dello schermo si annullano.

### [IT2] Prezzi solo sul conto — la cascata

Quando arriva il totale, gli importi entrano dall'esterno verso l'interno (12 px da
destra), dall'alto in basso, **40 ms** fra una riga e l'altra; il totale arriva **300 ms
dopo** l'ultimo importo. Il momento più teatrale del prodotto, sotto il secondo.
In `eco`: tutti insieme, in opacità.

### [IT2] Grazie → reclame → scontrino

1. Celebrazione (900 ms). 2. La reclame del posto `grazie` sale **420 ms dopo la fine
degli anelli**: due spettacoli insieme non si vedono. 3. Se arriva `receipt.show`, il QR
dello scontrino è l'elemento condiviso che si ingrandisce verso il centro (FLIP, 520 ms,
`--e-move`) e la carta si srotola sotto (`translateY` + opacità, non `height`).

## I tre livelli

| | `full` | `light` (default consigliato) | `eco` |
|---|---|---|---|
| Durate | base | ×0,7 | 140–200 ms |
| Sovraelongazione | sì | no | no |
| FLIP | sì | no | no |
| Vetro | `backdrop-filter` fino a 28 px | superficie semitrasparente pretinta, statica | nessuno |
| Mesh di fondo | animato, 4 blob, 24 s | disegnato una volta, immobile | nessuno |
| Ken Burns / parallasse | sì | no | no |
| Video | sì | sì | primo fotogramma |
| Budget | 60 fps, ≤ 3 elementi animati per frame | 60 fps, ≤ 2 | nessun requisito |

`light` è il livello su cui va tarata la demo in sala. `eco` resta bello perché la
gerarchia tipografica e il colore non dipendono dal movimento.

`prefers-reduced-motion: reduce` **forza `eco`**, qualunque cosa dica il token del locale.

### [IT2] Attrazione e livelli: vince il più basso

L'attrazione (SPEC §14) ha un **tetto di movimento**: Sobrio → `eco`, Vivo → `light`,
Spettacolo e TROPPO → `full`. Il livello effettivo è il minimo fra quel tetto, il token
animazioni dello schermo, `prefers-reduced-motion` e il degrado fps:

```ts
const order = { eco: 0, light: 1, full: 2 } as const;
const cap = { sobrio: 'eco', vivo: 'light', spettacolo: 'full', troppo: 'full' } as const;
const effective = [cap[attr], screen.animazioni, reduced ? 'eco' : 'full', fpsCap]
  .reduce((a, b) => (order[a] <= order[b] ? a : b));
document.documentElement.dataset.motion = effective;
document.documentElement.dataset.attr = attr;
```

Quindi TROPPO su una macchina in `eco` mostra **tutte** le reclame e i QR di TROPPO, fermi.
Gli effetti esclusivi di TROPPO (lampo sugli sconti, respiro del totale, mesh veloce)
esistono solo con `data-motion="full"`.

Nota: il default di Vivo è `light` — coerente con "`light` è il livello su cui va tarata
la demo in sala" qui sopra.

## Igiene

- `will-change` si aggiunge all'inizio dell'animazione e si **toglie alla fine**, mai in
  modo permanente.
- Nessun `box-shadow` animato, nessun `filter` animato.
- Blur massimo 28 px e su un solo livello di impilamento.
- Video ≤ 1080p30 H.264.
- Degrado automatico sotto 45 fps per 3 s: vedi `SPEC.md` §11.
