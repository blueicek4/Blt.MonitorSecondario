# Registrazione del 20/10/2026, 06:48–06:54: la prova A/B/A

Terza prova sul campo, stesso PC delle prime due (Windows 11, modello XML a 48 colonne, vecchio monitor chiuso),
con il registratore del commit `faaa6d3`. Un solo conto: 26 righe battute in quattro fasi, alternando `/consuma`,
poi "dettagli conto" e pagamento in contanti. Ogni file scritto da Passepartout è stato registrato (29 creazioni: 28
versioni e una riscrittura identica), e in ogni versione il totale è la somma delle righe.

**Anonimizzata** come quella delle 00:21: indirizzo e partita IVA dell'intestazione sostituiti con testo della stessa
lunghezza. In `eventi.jsonl` lo `sha256` è ricalcolato sui file anonimizzati.

## Il risultato

| Fase | `/consuma` | Versioni | Intervalli tra due creazioni del file | Eliminazioni fatte da Passepartout |
|---|---|---|---|---|
| A | no | #01–#05 | 3,31–3,69 s | 4 su 4 scritture |
| B | sì | #06–#12 | 0,23–0,47 s | nessuna |
| A2 | no | #13–#21 | 3,33–4,08 s | 8 su 8 scritture |
| B2 | sì | #22–#26 | 0,38–0,85 s | nessuna |

Poi, sempre con `/consuma`: #27 "dettagli conto", una riscrittura identica 0,32 s dopo, #28 `PAGATO CONTANTI: 450,00`
e `Resto: 110,50`.

**Conferma dell'ipotesi del 20/10**: se il file c'è ancora, Passepartout aspetta circa 3 s, lo elimina da sé e scrive
il nuovo: è il [modello a 48 colonne](../../../docs/reference/XMLMonitorCliente-48colonne.xml), con 3 tentativi da 1 s e
`SovrascriviSeEsistente=true` (con `false`, come il 19/10, rinuncerebbe). Se il file è già stato tolto, scrive subito. Senza `/consuma` le
battute arrivano una ogni 3,3 s qualunque sia il ritmo in cassa (A2: 9 versioni in 28 s). Con `/consuma` arrivano col
ritmo di chi batte. Tre cambi di modalità (due accensioni e uno spegnimento), tutti coerenti.

## Altro da notare

- **Stati intermedi persi senza `/consuma`**: #16 porta due righe nuove insieme. Mentre Passepartout aspettava, in cassa
  sono stati battuti due articoli, e il conto con uno solo dei due non è mai arrivato al file.
- **Tempi del registratore**: dalla creazione del file alla versione registrata 47–114 ms (mediana 71). È il ritardo
  che aggiungerà l'Agent, contro i 3 s di oggi.
- **"Dettagli conto"** (#27): come il 20/10, intestazione e righe uguali unite (`2 X 10,00 Euro` + `20,00`).
- **Descrizioni a 48 colonne**: `Caesar salad con salsa Vecchia Malga` arriva intera (36 caratteri).
- **File vuoto appena creato**: una lettura a 0 byte (dopo #02) messa da parte, come previsto.
