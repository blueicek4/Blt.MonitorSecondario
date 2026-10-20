# Bluetech Display Agent

Il servizio che gira sul PC cassa:

1. **Legge** `Scontrino.txt` quando Passepartout ha finito di scriverlo.
2. **Lo toglie subito dopo la lettura**, così Passepartout scrive il successivo senza aspettare. Se il file resta lì,
   Passepartout aspetta circa 3 s prima di sostituirlo (con `SovrascriviSeEsistente=true`), oppure rinuncia (con
   `false`): [prova A/B/A](../samples/registrazioni/2026-10-20_064854/README.md).
3. **Manda al display** i messaggi di `protocol.ts` (`display/src/types/protocol.ts`, ramo `claude/design-handoff`) via
   WebSocket, solo con quello che cambia.

La lettura e la cancellazione sono quelle del [registratore](../tools/README.md), già provate in cassa: il file si
toglie solo se contiene ancora la versione letta.

## Installazione

Serve Windows 10 o 11 a 64 bit. L'exe contiene già .NET: non si installa niente.

1. Copia `BluetechDisplayAgent.exe` e `appsettings.json` in una cartella, per esempio `C:\Bluetech\Agent`.
2. **Chiudi il vecchio `MonitorCliente.exe` e toglilo dall'avvio automatico.** L'Agent toglie il file dopo averlo letto:
   il vecchio monitor resterebbe senza conti. Se lo trova aperto, l'Agent lo scrive nel log e in `/api/stato`.
3. Prova a mano: doppio clic sull'exe, poi apri <http://localhost:5080/diagnostica> e batti un articolo in cassa.
4. Come servizio (prompt dei comandi da amministratore):

   ```bat
   sc.exe create BluetechDisplayAgent binPath= "C:\Bluetech\Agent\BluetechDisplayAgent.exe" start= delayed-auto DisplayName= "Bluetech Display Agent"
   sc.exe description BluetechDisplayAgent "Legge lo scontrino di Passepartout e lo manda al monitor cliente"
   sc.exe failure BluetechDisplayAgent reset= 86400 actions= restart/5000/restart/5000/restart/30000
   sc.exe start BluetechDisplayAgent
   ```

5. Nel modello XML del monitor di Passepartout conviene `SovrascriviSeEsistente=true`: se l'Agent si ferma, il
   display riceve gli aggiornamenti in ritardo invece di perderli.

Il Player apre `http://localhost:5080/display`, che rimanda al display.

## Configurazione (`appsettings.json`, sezione `Agent`)

| Chiave | Default | |
|---|---|---|
| `FileCassa` | `C:\MonitorSecondario\Scontrino.txt` | Il file del dispositivo monitor di Passepartout |
| `Porta` | `5080` | |
| `ApertoInRete` | `false` | `false`: risponde solo a questo PC. `true`: anche a tablet e TV della rete (serve una regola nel firewall). I conti viaggiano in chiaro |
| `CartellaDisplay` | `display` | La build del display (`display/dist`), accanto all'exe |
| `ChiusuraVenditaDopoSecondi` | `120` | Conto aperto, senza `PAGATO`, e nessun file per 2 minuti: si chiude e il display torna alle slide. Nelle registrazioni il pagamento più lento è arrivato 80 s dopo l'ultimo file (QR Satispay a video): con i 20 s del vecchio monitor il totale sarebbe sparito mentre il cliente pagava in 6 conti su 9. All'avvio, un file più vecchio di così si toglie senza mostrarlo |
| `CartellaLog`, `GiorniLog` | `log`, `14` | Un file al giorno |

Dalla riga di comando: `--file`, `--porta`, `--replay`, `--velocita`, `--ripeti`.

## Indirizzi

| | |
|---|---|
| `/` | Il display. Finché la cartella `display` non c'è, una pagina che lo dice |
| `/display` | Rimanda a `/` (indirizzo predefinito del Player) |
| `/ws` | WebSocket: alla connessione lo stato completo (`sync`), poi ogni cambiamento |
| `/diagnostica` | Il conto come lo vedrà il cliente, il QR, gli ultimi messaggi: per le prove e l'assistenza |
| `/api/stato` | File letti e tolti, problemi, display collegati, ultimi 50 messaggi, vecchio monitor aperto |

## Replay: senza cassa

```bat
BluetechDisplayAgent.exe --replay C:\Registrazioni\2026-10-20_075632 --velocita 2 --ripeti true
```

Rigioca una registrazione del registratore con i suoi tempi (le pause oltre 10 s si accorciano). Serve per le demo in
showroom e per provare il display senza cassa. Il file della cassa non viene toccato.

## Come interpreta il file

Le regole vengono dalle [registrazioni](../samples/registrazioni) (30 e 48 colonne):

| Nel file | Al display |
|---|---|
| Riga articolo nuova in fondo | `sale.line` con un id nuovo |
| Quantità o importo cambiati (`2 pz X 12,00 Euro`) | `sale.line` con lo **stesso id** |
| Riga sparita, le altre uguali invariate | storno: `sale.line` `void` con `voidsId` |
| Righe uguali unite in "dettagli conto", sconti spostati sotto le righe, ordine cambiato | `sync`, lo stato completo: niente storni finti |
| `TOT. DA PAGARE` | `sale.total`, in centesimi (il display non somma mai) |
| `PAGATO …:` e `Resto:` | `sale.close` con `paid: { method, amount, change }`: Grazie con le righe, poi le slide |
| Conto dopo un `PAGATO`, o senza nessuna riga in comune con quello a video | `sale.open`: vendita nuova |
| QR in Base64 | `pay.qr` con l'ultimo totale; il conto successivo lo toglie (`pay.clear`) |
| Intestazione del locale, `SUBTOT.`, `EURO` | niente |
| Versione identica alla precedente | niente |
| Accenti | UTF-8 o Windows-1252, riconosciuti da soli |

**Aggiunte al protocollo**, da implementare nel display (M5):

- `sale.line` con un id già ricevuto **aggiorna** quella riga;
- `{ "t": "hb" }` ogni 3 s. Con una cassa a file il silenzio è normale: senza battito il display mostrerebbe
  "connessione assente" dopo 8 s;
- `sale.close` può portare `paid`, per mostrare metodo e resto nello stato "Grazie".

## Cosa manca

- Il display non ha ancora il collegamento WebSocket (M5): oggi si vede tutto in `/diagnostica`.
- Annullo, reso e pagamento Satispay vero non sono nelle registrazioni: se Passepartout scrive righe che l'Agent non
  riconosce, arrivano al display come note e finiscono nel log.
- L'installer (Fase 5) e la firma digitale dell'exe.

## Sviluppo

```bash
dotnet test agent/Bluetech.Display.Agent.Tests
dotnet run --project agent/Bluetech.Display.Agent -- --replay samples/registrazioni/2026-10-20_075632 --velocita 4
dotnet publish agent/Bluetech.Display.Agent -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

I test rigiocano le registrazioni vere attraverso l'Agent e un display di prova che applica i messaggi: dopo ogni
versione il display deve mostrare esattamente il conto della cassa (righe, quantità, totale).
