# Bluetech Recorder — registratore di `Scontrino.txt`

Registra **ogni versione** del file che Passepartout Menu scrive per il monitor cliente, byte per byte,
con l'ora al millisecondo. Serve per la Fase 0: capire con dati veri come si comporta la cassa
(scontrino, storni, sconti, QR Satispay, messaggi del visore) prima di scrivere l'Agent.

Non intralcia la cassa e funziona anche con il vecchio `MonitorCliente.exe` acceso. Legge il file senza negare
a nessuno né la lettura né la scrittura. Per confermare che Passepartout abbia finito di scriverlo lo apre per un
istante come fa il vecchio monitor a ogni lettura (che però lo tiene aperto per tutta la lettura): se in
quell'istante Passepartout volesse scrivere, riproverebbe da sé, come già succede con il vecchio monitor.

## Avvio

1. Copia `BluetechRecorder.exe` sul PC cassa. Basta il solo exe (il `.config` accanto è facoltativo).
   Serve Windows 10 dalla versione 1607 (anche LTSB 2016 e LTSC 2019) o Windows 11: .NET Framework 4.6.2 è già incluso.
2. Doppio clic: registra `C:\MonitorSecondario\Scontrino.txt`.
   - Altro percorso: trascina il file `Scontrino.txt` (o la sua cartella) sull'exe, oppure
     `BluetechRecorder.exe D:\percorso\Scontrino.txt`.
   - Altra cartella di destinazione: `--out D:\Registrazioni`.
   - Modalità consumo da subito: `--consuma` (vedi sotto).
3. Lavora in cassa. Prima di ogni prova **scrivi una nota** nella finestra e premi Invio
   (es. `storno seconda riga`): finisce nell'indice con l'ora, così ogni file si collega a cosa stavi facendo.
4. Per finire scrivi `/fine` (oppure Ctrl+C, oppure chiudi la finestra: lo zip viene creato lo stesso, di norma
   anche se spegni il PC; in ogni caso tutti i file restano nella cartella della registrazione).
5. Invia lo zip indicato a fine registrazione:
   `C:\MonitorSecondario\Registrazioni\AAAA-MM-GG_hhmmss.zip`.

Se la cartella non è scrivibile, le registrazioni vanno sul Desktop in `Registrazioni Monitor`.
La selezione del testo col mouse nella finestra è disattivata: in Windows bloccherebbe la console.

## Se non arrivano versioni nuove

Cosa sappiamo dalle prove sul campo (stesso PC):

- **19/10**: il file c'era già (scritto un minuto e mezzo prima, modello a 30 colonne) e per 2 minuti e mezzo di
  prove in cartella non è successo niente: nessuna eliminazione né creazione. Un tentativo fallito non lascia
  traccia: per questo il registratore ora segnala all'avvio se il file è in sola lettura. Una sola versione.
- **20/10**: modello a 48 colonne, file assente all'avvio: [42 versioni](../samples/registrazioni/2026-10-20_002138/README.md).
  Passepartout **elimina il file e lo ricrea** a ogni aggiornamento, e mai prima di 3 s dalla scrittura precedente.

- **20/10 alle 6:48**, [prova A/B/A](../samples/registrazioni/2026-10-20_064854/README.md): **confermato** che, se il
  file c'è ancora, Passepartout aspetta circa 3 s (`NumeroTentativi` × `SecondiAttesa`) e lo toglie da sé; se è già
  stato tolto, scrive subito. Con `/consuma` le battute arrivano dopo 0,23–0,85 s invece che dopo 3,3 s.

Il perché della prima prova non è ancora noto: quel giorno Passepartout non ha nemmeno eliminato il file.

Il registratore ti avvisa alla seconda nota se nel frattempo il file non è cambiato, e ti dice cosa può significare:
file assente (percorso sbagliato), file mai riscritto (prova qui sotto), vecchio monitor che legge un'altra cartella.

### La prova A/B/A (in una sola registrazione)

1. Avvia il registratore senza `/consuma`. Nota `A`, poi batti 3 articoli di fila, veloce.
2. Scrivi `/consuma`. Nota `B`, batti altri 3 articoli di fila.
3. Scrivi di nuovo `/consuma` (si spegne). Nota `A2`, batti altri 3 articoli.
4. `/fine`.

Con `/consuma` il registratore toglie il file subito dopo averlo letto, ma solo se contiene ancora la versione
appena registrata: lo apre negando la scrittura, ricontrolla il contenuto e lo elimina prima di chiuderlo. Ogni
versione resta salvata nella registrazione.

Cosa guardiamo: se in `B` le versioni arrivano subito (e non più a 3,3 s una dall'altra) Passepartout aspetta davvero
che qualcuno tolga il file, e il nuovo monitor dovrà farlo. Il riepilogo riporta il tempo minimo tra due scritture,
con e senza `/consuma`. Durante la prova guarda anche la cassa: si ferma qualche secondo a ogni battuta? Compare un
errore?

`/consuma` non si attiva se è aperto il vecchio `MonitorCliente.exe` e legge lo stesso file: gli toglierebbe il conto
da mostrare. Chiudilo, oppure scrivi `/consuma forza`. Il vecchio monitor legge sempre `Scontrino.txt` nella cartella
del proprio exe. Se è aperto in un'altra cartella, il registratore te lo dice all'avvio: se il display del cliente si
aggiorna, è lì che scrive Passepartout. Se Windows non dice la cartella (vecchio monitor avviato da un altro utente),
avvia il registratore come amministratore.

Gli altri file modificati nella stessa cartella compaiono comunque a video (`cartella: modificato …`).

## Cosa vedi a video

```
18:53:48.974  #0002  SCONTRINO  2 righe · totale 4,00 → 5,80
   +   2  CAPPUCCINO               1,80
18:53:50.778  #0004  SCONTRINO  2 righe · totale 7,60 → 5,80   (uguale a #0002)
   −   3  CAPPUCCINO               1,80
18:53:51.377  #0005  MESSAGGIO  "TOTALE 5,80"
18:53:51.978  #0006  QR CODE    immagine PNG 290×290 → 0006_qrcode.png
```

Passepartout rimanda sempre il conto intero: il registratore lo confronta con la versione precedente e mostra
solo le righe aggiunte (+) e tolte (−), con la loro posizione. È lo stesso confronto che userà il nuovo monitor
per animare le differenze.

Una versione viene registrata solo se il file è **stabile**: dimensione e data non cambiano durante la lettura né
nei 30 ms successivi, e nessuno lo ha più aperto in scrittura. Un file preso mentre Passepartout lo sta ancora
scrivendo (vuoto, a metà) non è uno stato che il monitor avrebbe mostrato: il registratore aspetta che la scrittura
finisca; se non riesce a confermarla, il contenuto finisce in `letture-instabili\` e a video compare come
`lettura durante la scrittura`. Le "riscritture identiche" sono i casi in cui Passepartout riscrive lo stesso
identico conto (stesso contenuto, data del file cambiata).

## Cosa contiene lo zip

| File | Contenuto |
|---|---|
| `0001_hh-mm-ss.fff.txt`… | Ogni versione diversa del file, identica byte per byte all'originale |
| `0006_qrcode.png` | L'immagine del QR, se Passepartout la manda in Base64 |
| `letture-instabili\` | Contenuti letti mentre il file veniva scritto (raccontano come scrive Passepartout) |
| `eventi.jsonl` | Una riga per evento: versioni (codifica, righe aggiunte/tolte, righe in comune col conto precedente, totale, dimensione, data di modifica e di creazione del file), riscritture identiche, letture instabili, file eliminato/creato, note, avvisi, `/consuma` |
| `sessione.txt` | Versione di Windows, schermi e risoluzioni, WebView2 installato o no, vecchio monitor aperto |
| `riepilogo.txt` | Conteggi finali, tempo minimo tra due scritture (con e senza `/consuma`) |
| `contesto\` | Copia di `XMLMonitorCliente.xml` e `MonitorCliente.exe.config` se sono nella stessa cartella o in quella del vecchio monitor aperto |

Attenzione: le registrazioni contengono gli scontrini. Per le prove usa articoli di test e, per le fatture,
un cliente fittizio.

## Il giro completo

Una nota prima di ogni punto. Se una funzione non la usate, saltala.

1. Conto semplice: 1 articolo, poi altri 3, di cui 2 uguali.
2. Articolo con quantità (moltiplicatore, es. 3 × caffè).
3. Storno di una riga in mezzo e dell'ultima riga.
4. Sconto % su una riga, sconto % sul subtotale, sconto a valore, maggiorazione.
5. Annullo dell'intero scontrino.
6. Preconto, se lo usate.
7. Pagamento in contanti con resto.
8. Pagamento con carta/bancomat.
9. **Satispay**: QR a video → pagamento riuscito; poi un secondo QR → pagamento annullato o scaduto.
10. Scontrino con codice fiscale o P.IVA e una fattura.
11. Articoli con descrizione lunga, accenti (Caffè, Tiramisù), simbolo €, apostrofo (Pizza dell'orto).
12. Omaggio o prezzo zero, reso.
13. Conto al tavolo e conto diviso, se li usate.
14. Due conti di fila, per vedere come inizia un conto nuovo.
15. Dopo l'ultimo pagamento lascia tutto fermo 1 minuto, poi chiudi con `/fine`.

## Compilazione e test

```bash
dotnet build tools/Bluetech.Display.Recorder -c Release      # exe in bin/Release/net462/
dotnet test tools/Bluetech.Display.Recorder.Tests
```

Su Windows i test girano anche sulla build .NET Framework 4.6.2. La GitHub Action **Build** compila e testa su Windows
e pubblica `BluetechRecorder` e `BluetechDisplayPlayer` come artifact.
