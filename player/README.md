# Bluetech Display Player

Mini eseguibile che apre il monitor cliente web a schermo intero sullo schermo secondario della cassa,
dentro una WebView2 (il motore di Microsoft Edge), e parte da solo all'accensione.

## Requisiti

- Windows 10 dalla versione 1803 (anche LTSC 2019) o Windows 11: .NET Framework 4.7.2 è già incluso.
  Su Windows 10 LTSB 2016 va installato .NET Framework 4.8.
- Microsoft Edge WebView2 Runtime: presente in Windows 11 e nei Windows 10 aggiornati. Se manca, il player lo
  segnala a video; si installa da <https://go.microsoft.com/fwlink/p/?LinkId=2124703>.

## Installazione

1. Copia la cartella di build (es. in `C:\Program Files\Bluetech\DisplayPlayer`). Contiene
   `BluetechDisplayPlayer.exe`, le DLL `Microsoft.Web.WebView2.*` e la cartella `runtimes`.
2. Avvia `BluetechDisplayPlayer.exe`: al primo avvio si apre la **prima configurazione**
   - indirizzo web del monitor: `http://localhost:5080/auth` se l'Agent è sullo stesso PC, altrimenti
     `http://<pc-dell-agent>:5080/auth` (senza `http://` viene aggiunto). La prima volta lo schermo mostra un codice
     di 6 cifre: si scrive nel back office dell'Agent (Dispositivi) e il Player passa da solo allo schermo del
     cliente. Le volte dopo `/auth` porta subito lì: il Player ricorda l'autorizzazione nel suo profilo;
   - schermo (automatico = primo schermo secondario; "Identifica schermi" mostra il numero su ogni monitor);
   - **codice di accesso** (almeno 4 caratteri), obbligatorio;
   - avvio automatico con Windows (attivo di default).
3. Salva: il player si apre sul monitor cliente.

Fai la configurazione con l'utente Windows che usa la cassa, non con "Esegui come amministratore":
avvio automatico e dati del browser sono per utente.

## Uso quotidiano

| Azione | Come |
|---|---|
| Configurazione | doppio clic sull'icona Bluetech nell'area di notifica, oppure **Ctrl+Alt+Maiusc+F12**, oppure `BluetechDisplayPlayer.exe --config` |
| Ricaricare la pagina | menu dell'icona → Ricarica la pagina |
| Chiudere il player | menu dell'icona → Esci… (chiede il codice). Alt+F4 è disattivato |
| Prove su PC con un solo monitor | `BluetechDisplayPlayer.exe --windowed` |

Ogni accesso alla configurazione e la chiusura chiedono il codice. Dopo 5 codici errati l'accesso si blocca per 60 secondi.

## Comportamento

- **Non prende mai il focus**: la tastiera resta a Passepartout anche quando il player si apre o si riposiziona.
- **Non copre mai la cassa**: se il monitor cliente è scollegato il player si nasconde e ricompare quando torna
  (in alternativa, dalla configurazione: finestra sullo schermo principale).
- Si riposiziona da solo quando cambiano monitor, risoluzione o scala e al risveglio dallo standby.
- Se il server non risponde mostra "Connessione al monitor in corso…" e riprova con attesa crescente (5 s → 60 s).
  Se il motore del browser va in crash, lo ricrea.
- Impedisce lo spegnimento dello schermo, nasconde il puntatore, blocca menu contestuale, zoom, popup e
  scorciatoie del browser. I video possono partire con l'audio senza interazione.

## File

| Cosa | Dove |
|---|---|
| Configurazione | `C:\ProgramData\Bluetech\DisplayPlayer\player.config.xml` |
| Log (14 giorni) | `C:\ProgramData\Bluetech\DisplayPlayer\logs\` |
| Dati del browser | `%LOCALAPPDATA%\Bluetech\DisplayPlayer\WebView2\` |
| Avvio automatico | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → "Bluetech Display Player" |

**Codice dimenticato**: chiudi il player (Gestione attività), cancella `player.config.xml` e riavvialo:
riparte la prima configurazione. Il codice protegge dalle modifiche dall'interfaccia, non da chi ha accesso
ai file di Windows.

## Compilazione

```bash
dotnet build player/Bluetech.Display.Player -c Release
```

Funziona anche su Linux e macOS (pacchetto `Microsoft.NETFramework.ReferenceAssemblies`).
Output in `player/Bluetech.Display.Player/bin/Release/net472/`.
A ogni push la GitHub Action **Build** compila su Windows e pubblica la cartella pronta come artifact.

## Da fare prima della distribuzione

- Icona ufficiale `.ico` (ora è generata a runtime).
- Firma digitale dell'exe: senza, SmartScreen e alcuni antivirus bloccano i programmi in avvio automatico.
- Installer (insieme all'Agent).
- Collaudo su hardware reale: il codice è stato compilato ma non ancora eseguito su Windows.
