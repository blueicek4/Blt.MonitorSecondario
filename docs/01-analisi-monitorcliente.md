# MonitorCliente.exe — analisi di reverse engineering

> Scopo: documentare **esattamente** cosa fa l'attuale monitor secondario per Passepartout Menu,
> così che il nuovo prodotto Bluetech sia compatibile al 100% con il file di scambio
> e risolva i limiti dell'originale. Nessun codice o asset dell'originale viene riutilizzato:
> reimplementiamo il comportamento e il protocollo (interoperabilità).

## 1. Identità dell'eseguibile

| Voce | Valore |
|---|---|
| File | `MonitorCliente.exe` — 43 KB |
| Versione | 2.0.0.0 |
| Copyright | Hewlett-Packard Company 2015 |
| Tecnologia | .NET Framework 3.5 (CLR 2.0), Windows Forms |
| Offuscamento | nessuno |
| Classi | `Main` (tutta la logica), `Program`, `DoubleBufferedPanel`, `ViewOnlyTextBox` (inutilizzato), `Resources`, `Settings` |
| Risorse incorporate | `powered_by_passepartout` (173×55), `marchio_passepartout_240x60`, `marchio_passepartout_80x20`, icona |

Metodo: disassemblaggio IL completo (dnfile + dncil) verificato con decompilazione ILSpy (`ilspycmd`).
Tutti i 24 metodi di `Main` sono stati letti.

## 2. Architettura

Un'unica finestra WinForms. Niente rete, niente database: tutto passa da **un file di testo**.

```
Passepartout Menu ──(driver "stampa su file", modello XML "Monitor")──► Scontrino.txt
                                                                            │
                                               FileSystemWatcher (evento Changed)
                                                                            ▼
                                                                  MonitorCliente.exe
```

- `FileSystemWatcher.Path` = cartella dell'eseguibile (`Application.StartupPath`)
- `Filter` = `Scontrino.txt`
- Gestito solo l'evento **Changed** (l'handler di Created è vuoto)
- `SynchronizingObject` = la form → l'elaborazione avviene sul thread dell'interfaccia

Timer:

| Timer | Intervallo | Funzione |
|---|---|---|
| `timer` | 3 s | dopo 20 s di inattività mostra "ARRIVEDERCI E GRAZIE" |
| `timerImg` | `ScrollingImageDelay` × 1000 ms (default 5 s) | slideshow immagini |
| `timerMarquee` | `ScrollingTextDelay` ms (default 20, config 15) | testo scorrevole: 1 pixel per tick |

## 3. Protocollo del file `Scontrino.txt`

A ogni modifica il file viene letto per intero (`File.ReadAllLines`, codifica UTF-8 con rilevamento BOM).
Fino a **3 tentativi** a 500 ms di distanza se il file è bloccato dallo scrittore.
Se la prima riga è vuota, il file viene ignorato.

Prima di tutto: **se è visualizzato un QR, qualunque nuovo file lo toglie** e ripristina la pubblicità.

### 3.1 Prima riga = `SNF` → scontrino (stampa non fiscale)

1. Svuota il messaggio della barra in alto.
2. Cerca una riga uguale a `QRCODE`: se c'è, **la riga successiva è un'immagine PNG/JPG in Base64**
   → la mostra al posto della pubblicità (vedi §3.3) e **termina** (il resto del file è ignorato, lo scontrino a video non cambia).
3. Altrimenti scarta la riga `SNF` e tratta il resto come righe di scontrino:
   - scarta le righe che **iniziano con `SUBTOT`**
   - scarta le righe che **contengono ` x `** (case-insensitive: sono le righe di dettaglio quantità, es. `2 x 3,50`)
   - ogni riga viene divisa sull'**ultimo spazio**: a sinistra la descrizione, a destra l'importo
   - la riga che **inizia con `TOT. DA PAGARE`** diventa il totale (mostrato come "TOTALE" + importo, in grassetto, in fondo)
   - le righe **senza spazi** diventano righe vuote (es. `--------` o una parola sola)
   - la descrizione viene troncata a 2/3 della larghezza della card
   - se le righe non entrano, si mostrano **le ultime** (le più recenti)

Esempio (formato presunto, da confermare con file reali — vedi piano, Fase 0):

```
SNF
PIZZA MARGHERITA          8,50
2 x 2,00                          ← scartata
COPERTO                   4,00
SUBTOTALE                12,50    ← scartata
TOT. DA PAGARE           12,50    ← diventa il totale
```

### 3.2 Prima riga ≠ `SNF` → messaggio "visore" (display cliente)

Tutte le righe vengono unite con uno spazio (`riga1 + " " + riga2.Trim() …`) e mostrate al centro della
**barra in alto**. Se il testo è troppo largo e contiene `€`, va a capo prima del simbolo euro.
Corrisponde al comando `VisualizzaMessaggioDisplay` del modello XML (`{1} {2}` = riga 1 e riga 2 del visore).

### 3.3 QR code (pagamento Satispay)

- Il QR arriva come immagine Base64 nella riga dopo `QRCODE` (comando `StampaQRCode` del modello XML: `QRCODE\n{0}`).
- Viene mostrato **alle dimensioni native, centrato, senza ridimensionamento**, su sfondo bianco,
  nell'area pubblicità, con la scritta in alto "Leggi il QR code per effettuare il pagamento".
- Lo slideshow si ferma; riparte al primo file successivo (qualunque esso sia).
- Non c'è timeout né esito del pagamento: il QR resta finché la cassa non scrive di nuovo il file.

### 3.4 Stato "Arrivederci"

Ogni 3 s: se la barra in alto contiene un testo **e** sono passati più di 20 s dall'ultimo aggiornamento
(scontrino o messaggio), la barra mostra `ARRIVEDERCI E GRAZIE`.
Lo scontrino invece **non viene mai cancellato**: resta a video fino alla vendita successiva.

### 3.5 Formato reale osservato (primo file dalla cassa)

File d'esempio: [samples/scontrino-bar-9-righe.txt](../samples/scontrino-bar-9-righe.txt).

```
SNF
␠
␠SPREMUTA ARANCIA         4,00
␠CAPPUCCINO               1,80
…
SUBTOT.                  19,60
␠
TOT. DA PAGARE           19,60
␠
␠
```

- ASCII puro, fine riga CRLF, nessun BOM. Codifica di accenti ed euro ancora da verificare (registratore).
- Righe di **30 colonne esatte** (`NumeroColonne` del modello XML): articoli con uno spazio iniziale, descrizione
  allineata a sinistra, importo allineato a destra; `SUBTOT.` e `TOT. DA PAGARE` partono da colonna 0.
- Le righe "vuote" contengono uno spazio.
- Lo stesso articolo battuto più volte compare su **righe separate** (non come quantità).
- **A ogni modifica Passepartout riscrive il conto intero.** Il nuovo monitor deve quindi confrontare il conto in
  memoria con quello appena letto e animare solo le differenze (righe aggiunte, tolte, cambiate). Il registratore
  ([tools/](../tools/README.md)) usa già questo confronto, basato sulla sottosequenza comune più lunga: un articolo
  ripetuto viene riconosciuto come aggiunto in fondo, uno storno come riga tolta nella sua posizione.

## 4. Layout

Progettato su 1264×681, scalato in proporzione su un riferimento 1920×1020.

```
┌────────────────────────────────────────────────────────────────────┐
│ [Logo locale 350px]        MESSAGGIO VISORE (centrato)            │  barra alta ~80px
├──────────────────────┬─────────────────────────────────────────────┤
│  ╭────────────────╮  │                                             │
│  │ DESCRIZIONE  € │  │     Slideshow immagini pubblicitarie        │
│  │ ...            │  │            oppure                           │
│  │────────────────│  │     QR code + "Leggi il QR code…"           │
│  │ TOTALE   12,50 │  │                                             │
│  ╰────────────────╯  │                                             │
│   colonna 600px      │                                             │
├──────────────────────┴───────────────────────────┬─────────────────┤
│  testo scorrevole ←←←                            │ Powered by PP   │  barra bassa ~78px
└──────────────────────────────────────────────────┴─────────────────┘
```

Su 1920×1080: barra alta ≈ 85 px, barra bassa ≈ 83 px, colonna scontrino 600 px, area pubblicità ≈ 1320×912 px.
Card scontrino: rettangolo arrotondato (raggio 10), margine 30 px, area totale alta 90 px con linea separatrice di 4 px.
Font: Ubuntu (se non installato, Windows ripiega su Microsoft Sans Serif).

All'avvio si posiziona sul **primo schermo non primario**, senza bordi, massimizzato.

## 5. Configurazione (`MonitorCliente.exe.config`, sezione appSettings)

| Chiave | Default nel codice | Note |
|---|---|---|
| `ImageFolder` | — | Cartella immagini. Tutti i file (nessun filtro estensione), riletta a ogni cambio immagine |
| `ScrollingImageDelay` | 5 s | Secondi per immagine |
| `KeepImageRatio` | true | true = Zoom (mantiene proporzioni), false = Stretch |
| `ScrollingTextFile` | — | File col testo scorrevole (percorso relativo alla cartella di lavoro, non all'exe) |
| `ScrollingTextDelay` | 20 ms | Millisecondi per pixel: **più basso = più veloce** |
| `ImageLogo` | — | Logo in alto a sinistra, mostrato a dimensione nativa nel box 350×80 |
| `TopBackColor` / `TopForeColor` | 84,87,90 / bianco | |
| `BottomBackColor` / `BottomForeColor` | 84,87,90 / bianco | `BottomForeColor` viene ignorato (vedi bug) |
| `ReceiptBackColor` / `ReceiptForeColor` | bianco / nero | Card scontrino |
| `ReceiptBackgroundColor` | 218,218,215 | Sfondo attorno alla card e dietro le immagini |
| `ReceiptLineSeparatorColor` | 218,218,215 | Linea sopra il totale |
| `Currency` *(non documentata)* | `EURO` | Letta ma mai usata |

Formato colori: `R;G;B` oppure un nome italiano tra `Nero, Bianco, Rosso, Verde, Blu, Giallo, Arancione, Azzurro, Grigio, Viola, Rosa`.
Un nome non riconosciuto diventa bianco.

## 6. Tasti

| Tasto | Azione |
|---|---|
| F11 / Esc | alterna schermo intero senza bordi ↔ finestra ridimensionabile |
| F5 | ricarica il testo scorrevole dal file |
| F6 | mostra/nasconde le dimensioni in pixel di finestra e area immagini (utile per preparare le grafiche) |

## 7. Il modello XML di Passepartout (`XMLMonitorCliente.xml`)

È il **template** con cui il driver "stampa su file" di Passepartout traduce i comandi di stampa in testo
(ProtocolloXml 1.3, modello `Monitor`). È stato evidentemente derivato da un modello di stampante fiscale
(comandi `VEND REP=…`, `CHIUS T=…`, `PRMSG`), ma per il monitor contano soprattutto:

| Elemento | Valore | Effetto sul monitor |
|---|---|---|
| `InizioStampaNonFiscale` | `SNF` | intestazione che il monitor riconosce come scontrino |
| `StampaStringa` | `{0}` | le righe di testo dello scontrino |
| `StampaQRCode` | `QRCODE` + a capo + `{0}` | QR Satispay |
| `VisualizzaMessaggioDisplay` | `{1} {2}` | messaggi del visore → barra alta |
| `NumeroColonne` | 30 | larghezza delle righe di testo |
| `NumeroColonneDisplay` | 16 | larghezza del visore |
| `ParametriStampaSuFile` | 3 tentativi, 1 s, `DisplayInviaSNF=true`, `DisplayInviaQRCode=true` | |

**Ipotesi da verificare** (dai nomi, non dal codice del monitor):
- `DisplayInviaSNF=true` → la cassa invia al monitor una copia testuale (SNF) dello scontrino.
- `DisplayInviaQRCode=true` → la cassa invia al monitor il QR di pagamento.
- Se la cassa scrivesse nel file i comandi fiscali (`SF`, `VEND …`), il monitor li mostrerebbe come testo nella
  barra alta: quindi, presumibilmente, non lo fa.

**Osservato sul campo (19/10/2026)**: con `SovrascriviSeEsistente=false` e nessuno che toglie il file, Passepartout
ha scritto `Scontrino.txt` una sola volta e poi più nulla per tutta la sessione (2 minuti e mezzo di prove in cassa,
nessuna modifica né file nuovo nella cartella). L'interpretazione coerente: Passepartout non sovrascrive un file
esistente, aspetta che il lettore lo "consumi" (lo tolga) e dopo `NumeroTentativi` × `SecondiAttesa` rinuncia.
Il vecchio monitor non toglie mai il file: va chiarito come funziona in produzione (parametro a `true`, o altro).
Il registratore ha una modalità consumo (`/consuma`) per verificarlo.

**Opportunità**: essendo un template, possiamo definire noi l'output dei comandi (es. `StampaRigaArticolo` →
`ART|{0}|{1}|{2}|{5}`) e ottenere dati **strutturati** (quantità, prezzo unitario, reparto, sconti, metodo di
pagamento) invece di interpretare testo impaginato a 30 colonne. Va verificato quali comandi il driver invoca
davvero per un dispositivo di tipo monitor.

## 8. Bug e limiti trovati

| # | Problema | Conseguenza |
|---|---|---|
| 1 | Schermo intero senza bordi applicato **anche senza secondo monitor**; le proporzioni sono calcolate sullo schermo principale | Se il monitor cliente è scollegato, l'app **copre la cassa**; su monitor cliente piccoli (es. 1024×768 con principale 1920×1080) testi e colonne risultano sovradimensionati |
| 2 | Cartella immagini svuotata, rinominata o cancellata con l'app aperta | Divisione per zero o eccezione nel timer → **crash** |
| 3 | Cartella con soli file non immagine (es. `Thumbs.db`) | Ciclo infinito → **app bloccata** |
| 4 | Ritentativi con `Thread.Sleep(500)` sul thread UI | Interfaccia congelata fino a 1,5 s |
| 5 | Nessun debounce: l'evento Changed arriva spesso 2+ volte per scrittura | Doppia elaborazione, possibili letture di file a metà |
| 6 | Dopo il primo QR lo sfondo dell'area immagini resta bianco | Il colore configurato non torna |
| 7 | QR mostrato a dimensione nativa, mai scalato | Troppo piccolo o tagliato a seconda della risoluzione |
| 8 | `BottomForeColor` ignorato: il testo scorrevole è sempre bianco | Su barra chiara il testo è illeggibile |
| 9 | Righe senza spazi rese vuote; testo libero diviso a caso sull'ultimo spazio | Note, intestazioni e separatori resi male |
| 10 | Scontrino mai cancellato | Il cliente successivo vede il conto del precedente |
| 11 | Percorsi relativi (`testo.txt`, `Logo.png`) risolti sulla cartella di lavoro | Se avviato da collegamento con "Da:" diverso, logo e testo spariscono senza errori |
| 12 | Nessun log, nessuna diagnostica, nessuna gestione remota | Assistenza solo sul posto |
| 13 | Branding fisso Passepartout/HP, testi solo in italiano, nessun video | |

## 9. Pseudo-codice ricostruito delle funzioni chiave

```csharp
void GestioneFile(FileSystemEventArgs e)
{
    int tentativi = 0; bool ok = false;
    if (qrVisibile) VisualizzaImmagine(null);          // ogni nuovo file toglie il QR
    do {
        try {
            var righe = File.ReadAllLines(e.FullPath);
            if (righe.Length > 0 && righe[0] != "") {
                if (righe[0] == "SNF") {
                    barraAlta.Text = "";
                    for (int i = 0; i < righe.Length; i++)
                        if (righe[i] == "QRCODE") {
                            VisualizzaImmagine(Image.FromStream(new MemoryStream(Convert.FromBase64String(righe[i + 1]))));
                            return;
                        }
                    MostraScontrino(righe.Skip(1)
                        .Where(r => !r.StartsWith("SUBTOT") && !r.ToLower().Contains(" x ")));
                } else {
                    barraAlta.Text = "";
                    MostraMessaggio(righe.Aggregate((a, b) => a + " " + b.Trim()));
                }
            }
            ok = true;
        } catch { tentativi++; Thread.Sleep(500); }
    } while (tentativi < 3 && !ok);
}

// Disegno di ogni riga di scontrino
int sp = riga.LastIndexOf(' ');
if (riga.StartsWith("TOT. DA PAGARE")) totale = riga.Substring(sp);
else if (sp > 0) { descrizione = riga[..sp].TrimStart(); importo = riga[sp..].TrimEnd(); }
else             { descrizione = ""; importo = ""; }
```
