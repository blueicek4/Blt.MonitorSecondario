# Piano d'azione — Bluetech Display

Nuovo monitor cliente per Passepartout Menu: stesso file di scambio dell'attuale `MonitorCliente.exe`,
interfaccia web moderna e personalizzabile, logo Bluetech e "powered by Bluetech" sempre presenti.

> Le stime sono indicative (1 sviluppatore + Claude Code) e vanno riviste dopo la Fase 0.

## 1. Cosa ci dice il reverse engineering

Dettagli in [01-analisi-monitorcliente.md](01-analisi-monitorcliente.md). I punti che orientano il progetto:

1. **Il contratto con Passepartout è un file di testo** (`Scontrino.txt`) con tre tipi di contenuto:
   scontrino (`SNF`), messaggio visore (qualunque altra cosa), QR Satispay (`QRCODE` + immagine Base64).
   Possiamo sostituire il monitor **senza toccare Passepartout**.
2. **Lo scontrino arriva come testo già impaginato a 30 colonne**, non come dati. Il vecchio monitor lo spezza
   sull'ultimo spazio. Per un'interfaccia ricca (quantità, sconti, foto piatto) servono dati migliori:
   - MVP: parser tollerante del testo (compatibile al 100% con l'attuale);
   - poi: **il modello XML è un template che controlliamo noi** → proviamo a far emettere righe strutturate
     (`ART|descrizione|qtà|prezzo|totale`, `PAG|tipo|importo`…). Da verificare in Fase 0 quali comandi il driver invia al monitor.
3. **Il QR Satispay è un'immagine** generata da Passepartout. Il monitor **non riceve l'esito del pagamento**:
   non possiamo mostrare "pagamento riuscito" senza un segnale affidabile (da cercare in Fase 0).
4. **Bug da non ripetere**: copre la cassa se manca il secondo monitor, crash se si svuota la cartella immagini,
   scontrino mai cancellato, QR non scalato, UI congelata durante i ritentativi.

## 2. Architettura

```
                      PC CASSA (Windows 10/11)
┌───────────────────────────────────────────────────────────────────────────┐
│ Passepartout Menu ──► Scontrino.txt                                      │
│                            │ (watcher + polling + debounce)              │
│                            ▼                                             │
│  ┌──────────────────── Bluetech Display Agent (servizio Windows) ──────┐ │
│  │ parser v1 (testo) / v2 (strutturato) → macchina a stati → SignalR   │ │
│  │ API REST · media · tema · pannello admin · registrazione/replay     │ │
│  └──────────────────────────────┬───────────────────────────────────────┘ │
│                                 │ http://localhost:5080/display           │
│  Bluetech Display Player ◄──────┘  (WebView2 a tutto schermo, 2° monitor) │
└───────────────────────────────────────────────────────────────────────────┘
          │ opzionale, in LAN: tablet, smart TV, totem → stesso URL
```

| Componente | Tecnologia | Perché |
|---|---|---|
| **Agent** | .NET 10 LTS (supporto fino a nov. 2028), ASP.NET Core, SignalR, servizio Windows, eseguibile singolo | Vostro stack; nessun runtime da installare con publish self-contained; gira anche senza utente collegato |
| **Display** | React + TypeScript + Vite; animazioni con Motion/GSAP; WebGL solo nel profilo "Piena" | Effetti di alto livello, componenti riutilizzabili, handoff diretto dal design |
| **Pannello admin** | stessa app React, area protetta da password | Un solo stack front-end |
| **Player** | .NET Framework 4.7.2 + WebView2 — **già fatto** (`player/`) | Già presente da Windows 10 1803 / LTSC 2019: exe minuscolo, zero dipendenze |

### Modello dati pubblicato dall'Agent

```json
{
  "state": "idle | order | payment | thanks",
  "receipt": {
    "lines": [ { "kind": "item | discount | void | text", "description": "Pizza Margherita", "qty": 2, "amount": 17.00 } ],
    "total": 23.50,
    "currency": "EUR"
  },
  "displayMessage": { "text": "TOTALE € 23,50", "at": "2026-10-19T20:15:03" },
  "payment": { "method": "satispay", "qrImage": "data:image/png;base64,…", "amount": 23.50 },
  "updatedAt": "2026-10-19T20:15:03"
}
```

L'Agent è l'unica fonte di verità: un display che si riavvia (o un secondo schermo che si collega)
riceve subito lo stato corrente.

### Macchina a stati (bozza, da confermare con i file reali)

| Da | Evento | A |
|---|---|---|
| idle | file `SNF` o messaggio visore | order |
| order | `QRCODE` | payment |
| payment | qualunque file successivo | order (come il vecchio monitor) |
| order | nessun aggiornamento per *N* s (default 20, come "ARRIVEDERCI E GRAZIE") | thanks |
| thanks | dopo *M* s (default 8) | idle — **lo scontrino viene cancellato** |

## 3. Fasi

### Fase 0 — Raccolta dati reali (2–3 giorni) — *bloccante per il parser*
- **Registratore pronto**: [tools/](../tools/README.md) — `BluetechRecorder.exe` salva ogni versione di `Scontrino.txt`
  byte per byte, con note dell'operatore, differenze riga per riga e uno zip finale da inviare.
- **Primo giro (19/10)**: una sola versione registrata, nessuna attività nella cartella; causa ancora da capire.
- **Secondo giro (20/10)**: 42 versioni, 6 conti ([registrazione](../samples/registrazioni/2026-10-20_002138/README.md)).
  Passepartout elimina e ricrea il file a ogni aggiornamento, mai prima di 3 s dal precedente.
- **Prova A/B/A (20/10 alle 6:48)**: [confermato](../samples/registrazioni/2026-10-20_064854/README.md). Se il file c'è ancora Passepartout aspetta
  circa 3 s e lo toglie da sé; se è già stato tolto scrive subito (0,23–0,85 s tra due battute contro 3,3 s).
  **Decisione**: l'Agent toglie il file dopo averlo letto, come il registratore con `/consuma`. Il display del cliente
  guadagna 3 s a battuta rispetto a oggi e non perde più stati intermedi.
- Già chiarito dal secondo giro: formato a 48 colonne, intestazione e quantità unite in "dettagli conto", sconti sul
  subtotale e ripartiti, righe `PAGATO …` e `Resto:` a fine conto. Ancora da registrare: Satispay, messaggi del visore,
  annullo, reso, fattura, e perché il terzo conto non ha scritto la riga del pagamento.
- Casi da registrare su una cassa vera: vendita semplice, quantità > 1, sconto riga e subtotale, maggiorazione, storno,
  annullo, reso, preconto, conto separato, fattura, pagamento contanti / carta / **Satispay**, messaggi del visore.
- Verificare: codifica (UTF-8? ANSI? il simbolo €), se il file viene riscritto o ricreato, ordine degli eventi
  (messaggi visore prima o dopo lo scontrino), cosa arriva dopo un pagamento Satispay riuscito o annullato.
- **Esperimento chiave**: modificare il template XML (es. `StampaRigaArticolo`, `StampaPagamento*`) e vedere se il
  driver li invia al monitor → protocollo v2 strutturato.
- Censimento parco installato: versione di Windows, risoluzione dei monitor cliente (10" 1280×800? 15" 1024×768?), CPU/GPU.

**Uscita**: archivio di file reali + decisione v1/v2 + elenco risoluzioni da supportare.

### Fase 1 — Agent e parser (1–1,5 settimane)
- Watcher robusto: FileSystemWatcher + polling di riserva, debounce, lettura con condivisione e ritentativi **fuori** dal thread di visualizzazione.
- Parser v1 identico al vecchio (regole in §3 dell'analisi) + miglioramenti: righe di testo conservate, quantità
  agganciate all'articolo invece di scartate, sconti riconosciuti dal segno.
- **Confronto tra versioni**: Passepartout riscrive sempre il conto intero, quindi l'Agent confronta il conto in memoria
  con quello letto e pubblica le differenze (riga aggiunta, tolta, cambiata) che il display anima. Algoritmo già scritto
  e testato nel registratore (`ReceiptDiff`). Da chiarire con le registrazioni: come riconoscere l'inizio di un conto nuovo.
- Test "golden" su tutti i file della Fase 0 (ogni file reale → JSON atteso).
- Macchina a stati, SignalR, API REST, servizio Windows, log.
- Importazione del vecchio `MonitorCliente.exe.config` (colori `R;G;B` e nomi italiani, cartella immagini, testo scorrevole) → tema iniziale: migrazione indolore dei clienti esistenti.
- Modalità **replay**: riproduce una sequenza registrata → test e demo in showroom senza cassa.

### Fase 2 — Design (1 settimana, in parallelo alla Fase 1)
- Prompt pronto in [03-prompt-claude-design.md](03-prompt-claude-design.md).
- Revisione con 2–3 ristoratori veri prima di sviluppare.
- Uscita: design system a token, 6 stati, specifiche motion, prototipo cliccabile.

### Fase 3 — Display web (2 settimane)
- Stati: attesa, conto in corso, messaggio visore, pagamento QR, grazie, connessione assente.
- Motore media: immagini e video, playlist, transizioni; il QR viene **scalato** per restare leggibile.
- Tre profili di animazione (Piena / Leggera / Eco) con **rilevamento automatico** (misura degli FPS all'avvio).
- Offline: font e media in cache locale; riconnessione automatica a SignalR.
- Footer "powered by Bluetech" come livello di sistema, fuori dal controllo del tema.

### Fase 4 — Pannello admin e branding (1–1,5 settimane)
- Editor del tema con anteprima dal vivo (colori, font, logo, sfondo, vetro, intensità animazioni) e preset.
- Gestione media (caricamento, ordine, durata, fasce orarie), testo scorrevole, messaggi.
- Simulatore: "aggiungi riga", "mostra QR", "fine vendita" — indispensabile per vendere in demo.
- Guida alle dimensioni consigliate delle grafiche (sostituisce il vecchio F6).

### Fase 5 — Pacchetto e pilota (1 settimana + 2 settimane di pilota)
- Installer unico (Agent come servizio + Player in avvio automatico + WebView2 Runtime se manca).
- **Firma digitale** degli eseguibili: senza, SmartScreen e antivirus bloccano gli exe in avvio automatico.
- Pilota su 2–3 locali con hardware diverso; raccolta log.

### Fase 6 — opzionale: Bluetech Cloud
- Gestione centralizzata di temi e campagne su tutti i clienti, telemetria, stato dei display nel monitoraggio AUGUR.
- Ha senso solo se diventa un servizio in abbonamento: decisione commerciale, non tecnica.

## 4. Il Player (fatto in questa sessione — da collaudare su Windows)

`player/Bluetech.Display.Player` — dettagli e istruzioni in [player/README.md](../player/README.md).

- Si apre a tutto schermo **sul primo schermo secondario** (o su quello scelto), in avvio automatico con Windows.
- **Non ruba mai il focus alla cassa** e **non copre mai lo schermo principale**: se il secondo monitor manca si nasconde
  e ricompare quando viene ricollegato (al contrario del vecchio monitor).
- Configurazione protetta da **codice di accesso** (hash PBKDF2, blocco dopo 5 errori): indirizzo web, schermo,
  comportamento senza schermo, zoom, puntatore nascosto, standby, ricarica periodica, avvio automatico, cambio codice.
- Accesso alla configurazione: icona nell'area di notifica, **Ctrl+Alt+Maiusc+F12**, oppure `--config`.
- Se il server non risponde mostra "Connessione al monitor in corso…" e riprova (5 s → 60 s); ricrea il browser se va in crash.
- Da fare: icona ufficiale `.ico`, firma digitale, installer, collaudo su hardware reale (compilato su Linux, non ancora eseguito su Windows).

## 5. Branding e "powered by Bluetech"

- Il tema è un insieme di **token** (colori, font, raggi, sfondo, vetro, intensità animazioni) modificabili dal pannello.
- Logo Bluetech + "powered by Bluetech": componente di sistema, sempre in basso, in ogni stato. Il tema può
  sceglierne solo la variante chiara o scura per il contrasto.
- Onestamente: in una pagina web nulla è inviolabile (chi ha accesso al PC può sempre manometterla). La protezione
  sensata è tenerlo nel codice del display e fuori dal tema; se serve di più è un tema di licenza, non di CSS.

## 6. Effetti "wow" senza rallentare la cassa

- Il cliente guarda lo schermo per 2–3 secondi: **cosa ho preso, quanto pago, come pago**. Gli effetti devono
  guidare l'occhio verso queste risposte, non competere con loro.
- Molti PC cassa hanno grafica integrata modesta: animazioni su `transform` e `opacity`, blur e WebGL solo nel profilo
  "Piena", con un equivalente statico curato nel profilo "Eco".
- Il QR non si anima, non si sfoca, non si copre: deve essere letto al primo colpo.

## 7. Rischi

| Rischio | Impatto | Mitigazione |
|---|---|---|
| Il formato del file cambia con un aggiornamento di Passepartout | Scontrino mal interpretato | Test golden, parser tollerante, ripiego "testo grezzo" sempre leggibile |
| Passepartout aspetta che il file venga tolto prima di riscriverlo (confermato dalla prova A/B/A) | Senza nessuno che lo toglie, ogni battuta in ritardo di ~3 s; il 19/10 monitor fermo al primo conto (causa da capire) | L'Agent toglie il file dopo averlo letto, solo se è ancora la versione letta (come il registratore con `/consuma`); mai Agent e vecchio monitor insieme sullo stesso file: all'installazione il vecchio monitor si spegne |
| Nessun segnale di esito Satispay | Niente animazione "pagato" | Non promettere l'esito; Fase 0 per cercare un segnale |
| PC cassa lenti o Windows vecchi (7/8.1) | Effetti a scatti / player non installabile | Profili animazione automatici; censimento in Fase 0; Windows 10+ come requisito |
| Monitor cliente staccato o cambio risoluzione | Display sulla cassa | Gestito dal Player (si nasconde e ricompare) |
| Exe non firmati | Bloccati da SmartScreen/antivirus | Certificato di firma del codice prima del pilota |
| Accordi commerciali con Passepartout | Uso dei marchi | Vedi decisioni |

## 8. Decisioni che servono da te

1. **Marchio Passepartout**: il vecchio monitor mostra "Powered by Passepartout". Il nuovo mostra solo Bluetech, o
   anche "per Passepartout Menu"? Da verificare nell'accordo di rivendita.
2. **Parco installato**: quante casse, che Windows, che monitor cliente. Decide requisiti e profili grafici.
3. **Modello commerciale**: licenza una tantum o abbonamento con gestione contenuti. Decide se la Fase 6 esiste.
4. **Il codice del Player**: unico per installazione (attuale) o anche un codice tecnico Bluetech? Un codice
   "universale" dentro l'exe sarebbe leggibile da chiunque lo decompili, come abbiamo fatto noi con il vecchio monitor.

## 9. Idee oltre l'MVP

- **Foto del piatto**: quando si batte un articolo con foto associata, la foto appare accanto allo scontrino.
- **Promozioni contestuali** (es. pizza senza bevanda → promo birra): efficaci solo dove l'ordine si compone davanti al
  cliente (banco, fast food); al tavolo il conto arriva a cose fatte.
- **QR nella schermata "Grazie"**: recensione Google, fidelity, menu digitale.
- Testi fissi in più lingue a rotazione per le zone turistiche.
