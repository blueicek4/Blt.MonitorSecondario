# Bluetech Display — motion design

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

## Igiene

- `will-change` si aggiunge all'inizio dell'animazione e si **toglie alla fine**, mai in
  modo permanente.
- Nessun `box-shadow` animato, nessun `filter` animato.
- Blur massimo 28 px e su un solo livello di impilamento.
- Video ≤ 1080p30 H.264.
- Degrado automatico sotto 45 fps per 3 s: vedi `SPEC.md` §11.
