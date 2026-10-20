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
- **Visto sul campo** ([registrazione del 20/10 alle 7:56](../samples/registrazioni/2026-10-20_075632/README.md)): il file è `SNF`, una riga di spazi, `QRCODE`, il PNG in Base64 su una riga sola,
  una riga di spazi. L'immagine è un PNG 197×197 in bianco e nero: QR versione 8, 49×49 moduli da 4 pixel, **senza
  quiet zone**. Arriva 4,9 s dopo "dettagli conto"; dopo il QR la cassa rimanda il conto (che toglie il QR) e poi la
  riga `PAGATO`. A grandezza naturale, su uno schermo da 1920, il QR è largo un decimo dello schermo.

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
| `NumeroColonne` | 30 (dal 20/10: 48) | larghezza delle righe di testo |
| `NumeroColonneDisplay` | 16 | larghezza del visore |
| `CaratteriAmmessi` | `!/:-=€` | probabilmente per questo gli accenti arrivano come apostrofo (`Maesta'`): da provare aggiungendo `àèéìòù`, guardando col registratore che codifica usa poi il file |
| `TestoSoloMaiuscole` | `false` | maiuscole e minuscole come in anagrafica |
| `ParametriStampaSuFile` | 3 tentativi, 1 s, `SovrascriviSeEsistente=false` (dal 20/10: `true`), `DisplayInviaSNF=true`, `DisplayInviaQRCode=true` | se il file c'è già, Passepartout aspetta e poi lo sostituisce o rinuncia (sotto) |

**Ipotesi da verificare** (dai nomi, non dal codice del monitor):
- `DisplayInviaSNF=true` → la cassa invia al monitor una copia testuale (SNF) dello scontrino.
- `DisplayInviaQRCode=true` → la cassa invia al monitor il QR di pagamento.
- Se la cassa scrivesse nel file i comandi fiscali (`SF`, `VEND …`), il monitor li mostrerebbe come testo nella
  barra alta: quindi, presumibilmente, non lo fa.

**Osservato sul campo** (registratore, stesso PC):

- **19/10**, modello a 30 colonne: il file c'era già (scritto un minuto e mezzo prima) e per 2 minuti e mezzo di prove
  in cassa nella cartella non è successo niente: nessuna eliminazione né creazione (un tentativo fallito, per esempio
  su un file in sola lettura, non lascerebbe traccia). Una sola versione.
- **20/10**, modello a 48 colonne, file assente all'avvio: 42 versioni in 14 minuti
  ([registrazione](../samples/registrazioni/2026-10-20_002138/README.md)). A ogni aggiornamento Passepartout **elimina il
  file e lo ricrea** 0,25 s dopo (vuoto, poi lo riempie). E **mai prima di 3,06 s** dalla scrittura precedente: 20
  intervalli su 41 sono tra 3,06 e 3,14 s (articoli battuti di fila, e contenuti rimandati uguali).

- **20/10 alle 6:48**, [prova A/B/A](../samples/registrazioni/2026-10-20_064854/README.md) con lo stesso modello: senza `/consuma` 12 scritture su 12 arrivano 3,31–4,08 s dopo
  la precedente, ognuna preceduta da Passepartout che elimina il file; con `/consuma` (il registratore toglie il file
  appena letto) 10 su 10 arrivano dopo 0,23–0,85 s, cioè col ritmo di chi batte, e Passepartout non elimina niente.
  Tre cambi di modalità nella stessa registrazione, tutti coerenti.

**Come scrive Passepartout**, coerente con tutte e tre le prove. I due modelli XML
([30 colonne](reference/XMLMonitorCliente.xml), quello del 19/10; [48 colonne](reference/XMLMonitorCliente-48colonne.xml),
dal 20/10) differiscono solo in `NumeroColonne` e `SovrascriviSeEsistente`.

- Se il file **non c'è**, lo scrive subito.
- Se **c'è**, riprova `NumeroTentativi` volte a `SecondiAttesa` di distanza (3 × 1 s). Poi, se il file c'è ancora:
  - con `SovrascriviSeEsistente=true` lo elimina e scrive il nuovo (20/10): circa 3 s di ritardo;
  - con `false` **rinuncia**: quell'aggiornamento non arriva mai (19/10: una sola versione, nessuna attività nella
    cartella).

Il vecchio monitor non toglie mai il file, quindi **oggi ogni battuta arriva al display circa 3 s dopo** (con `true`)
o non arriva affatto dopo la prima (con `false`). Con `true`, se in cassa si batte più in fretta, le versioni escono
comunque una ogni 3,3 s e alcuni stati intermedi si perdono (due righe nuove nella stessa versione).

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
| 14 | Scarta ogni riga che contiene ` x ` (pensato per il dettaglio quantità `2 X 3,00 Euro`) | Spariscono anche gli articoli con ` x ` nel nome, come `Tagliere Emilia x 2` (registrazione del 20/10) |

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

## 10. La versione 6.0.0.0 (Passepartout, agosto 2026)

Arrivata il 20/10 da un'installazione aggiornata (`24 MonitorSecondario.zip`): `MonitorCliente.exe` di 93 KB, versione
6.0.0.0, copyright "Mirco & Mika @ Passepartout S.p.a." (la 2.0.0.0 era di Hewlett-Packard, 2015). Stesso progetto
(stesso GUID), decompilato con `ilspycmd` e confrontato riga per riga con la 2.0. Il modello XML allegato è quello a 30
colonne con `SovrascriviSeEsistente=false`; cambiano solo due comandi da stampante fiscale (`VEND … QTY=`) che il
monitor non usa.

### 10.1 Cosa cambia

| Novità della 6.0 | Dettaglio dal codice |
|---|---|
| **QR dello scontrino digitale** | Oltre a `QRCODE` riconosce `QRCODESD`: stessa forma (riga del marcatore, PNG in Base64 sulla riga dopo), didascalia predefinita "Scarica il tuo scontrino digitale!" invece di "Leggi il QR code per effettuare il pagamento". Il modello XML non lo contiene: lo scrive Passepartout da sé |
| **Didascalia del QR dalla cassa** | La riga dopo il Base64 diventa la didascalia sopra il QR. Con i file visti il 20/10 quella riga è di 30 spazi: la didascalia risulta vuota. Se il Base64 è l'ultima riga del file l'indice va fuori dai limiti, l'eccezione è presa dal ciclo dei tentativi e il QR non compare (bug) |
| **Il QR non si toglie con il file successivo** | Finché il QR è a video, ogni file nuovo viene **ignorato** per `TimeoutClearDisplay` secondi (20 di default); poi il QR sparisce da solo. Nella 2.0 lo toglieva qualunque file. Se in quei secondi arriva lo scontrino con `PAGATO`, va perso: il monitor non lo rilegge |
| **Pulizia dopo l'inattività** (`TimeoutClearDisplay`, default 20 s) | Passati i secondi senza file: toglie il QR, scrive "ARRIVEDERCI E GRAZIE" in alto se c'era un messaggio e **cancella lo scontrino** (nella 2.0 restava a video fino alla vendita successiva, bug 10). Vale anche a conto aperto: una pausa di 20 s mentre si batte svuota il monitor fino alla battuta successiva |
| **Intestazione** | Le righe prima della riga di trattini sono l'intestazione: centrate in un blocco in alto invece che spezzate sull'ultimo spazio (la foto del 20/10: `40033 CASALECCHIO DI R` … `BO`). `HideHeader=true` la nasconde sempre |
| **Prezzi durante la presa comanda** (`ShowPricesDuringOrder`, default true) | Con `false`, finché il conto non ha l'intestazione (cioè durante la battuta, prima di "dettagli conto", preconto o scontrino) mostra solo le descrizioni: niente importi e niente totale |
| **Riga della valuta** | La riga uguale a `Currency` (default `EURO`) non si mostra più |
| **Dimensione del testo** (`ReceiptFontSize`, default 20) | Letta solo se nel config c'è anche `ShowPricesDuringOrder` (bug) |
| Varie | Area immagini nascosta se non ci sono immagini; logo Passepartout in basso ridimensionato invece che centrato |

Restano com'erano: filtro delle righe con ` x ` (bug 14), QR a grandezza naturale (bug 7), nessuna cancellazione del
file (Passepartout continua ad aspettare 3 s o a rinunciare, §7), nessun debounce e `Thread.Sleep` sul thread
dell'interfaccia (bug 4 e 5), solo l'evento Changed, file letto dalla cartella dell'exe.

### 10.2 Cosa serve al nostro progetto

| Novità | Per noi | Dove |
|---|---|---|
| `QRCODESD` | **Serve**: senza, l'Agent leggerebbe il marcatore e il Base64 come righe di testo del conto. Diventa un messaggio a parte, `receipt.qr`, mostrato nello stato Grazie al posto del QR di ringraziamento del pannello | Agent, ALLINEAMENTO §8 |
| Didascalia dalla cassa | **Serve**, solo se non è vuota: va nel messaggio del QR (`caption`); se manca, il display usa i suoi testi | Agent |
| QR che resta nonostante i file successivi | **Serve in parte.** Indica che dopo il QR arrivano altri file (verosimilmente il conto rimandato) che nella 2.0 toglievano il QR troppo presto. Noi non ignoriamo i file, perché `PAGATO` non si deve perdere: il conto **identico** rimandato nei 20 s dopo il QR non toglie il QR; un conto cambiato o `PAGATO` sì | Agent |
| Pulizia dopo l'inattività | **Già fatto, meglio**: chiusura dopo 60 s senza file (configurabile), Grazie solo con `PAGATO`, il QR almeno 3 minuti, poi le slide. La 6.0 svuota il monitor dopo 20 s anche a conto aperto e durante il QR | Agent, ALLINEAMENTO §9 |
| Intestazione e `HideHeader` | **Non serve**: il nostro display non mostra l'intestazione di testo, l'identità del locale la fa il tema (logo) | — |
| `ShowPricesDuringOrder` | **Serve per la migrazione**: chi l'ha impostato a `false` se lo aspetta anche dal nuovo monitor. L'Agent dice al display se il conto è "in battuta" o "conto" (`sale.total.bill`); il display nasconde importi e totale finché `bill` è falso, se l'opzione del locale lo chiede | Agent, display (M5), pannello |
| `Currency` | **Serve poco**: l'Agent salta già `EURO`; ora salta qualunque riga di una sola parola subito dopo i trattini | Agent |
| `ReceiptFontSize`, `TimeoutClearDisplay`, `HideHeader` | Da importare dal vecchio `.config` quando si migra un cliente (Fase 1, importazione): `TimeoutClearDisplay` → `ChiusuraVenditaDopoSecondi` solo se più lungo di 60 s | Importazione |

