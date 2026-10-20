# Registrazione del 20/10/2026, 00:21–00:35

Seconda prova sul campo con il [registratore](../../../tools/README.md): Passepartout Menu su un PC Windows 11
(un solo schermo), dispositivo monitor con il modello XML a **48 colonne**, nessun `/consuma`. 42 versioni
in 14 minuti, nessuna persa.

**Anonimizzata**: indirizzo e partita IVA dell'intestazione sostituiti con testo della stessa lunghezza
(`VIA DELLE PROVE NUM 20`, `P.I.00000000000`). Il resto è identico byte per byte a quanto ha scritto la cassa;
in `eventi.jsonl` lo `sha256` è ricalcolato sui file anonimizzati (e le righe sono LF, non CRLF).

## I conti

| Versioni | Cosa (dalle note fino a #27, poi dal contenuto) | Da notare |
|---|---|---|
| #01–#09 | Tavolo: 6 articoli, "dettagli conto", pagamento in contanti | #07 intestazione e righe uguali unite (`2 X 3,00 Euro` + `6,00`); #09 `PAGATO CONTANTI` |
| #10–#16 | Conto nuovo, dettagli, scontrino con POS | #10 ha una riga in comune col conto precedente; #16 `PAGATO C.CREDITO` |
| #17–#23 | Conto nuovo, dettagli, pagamento | **Nessuna versione col pagamento**: tra le note "pagamento in corso" e "finito pagamento" il file non cambia |
| #24–#27 | Conto nuovo; POS diretto fallito, poi contanti | Il POS fallito non scrive niente |
| #28–#38 | Conto nuovo con sconto, storno, quantità, pagamento | #31 `SCONTO -5,00` sul subtotale; #32–#33 righe tolte; #33 `2 pz X 12,00 Euro`; #36 e #38 sconto ripartito riga per riga |
| #39–#42 | Conto nuovo, dettagli | #41 e #42 uguali a #40 |

## Cosa dice sul protocollo

- **Scrittura**: Passepartout elimina il file e lo ricrea (0,25–0,27 s dopo). Il file nasce vuoto e viene
  riempito subito dopo (#10: lettura a 0 byte, messa da parte).
- **Ritmo**: mai prima di 3,06 s dalla scrittura precedente; 20 intervalli su 41 sono tra 3,06 e 3,14 s (13 articoli
  battuti di fila, 7 contenuti rimandati o "dettagli"). È `NumeroTentativi=3` × `SecondiAttesa=1` del
  [modello a 48 colonne](../../../docs/reference/XMLMonitorCliente-48colonne.xml), che ha `SovrascriviSeEsistente=true`:
  Passepartout aspetta fino a 3 s che il file sparisca, poi lo toglie da sé.
  Da verificare con `/consuma` (se è così, le versioni arrivano subito).
- **Stesso contenuto rimandato**: #08, #15, #22, #23, #35, #41, #42 sono uguali alla versione precedente.
- **Formato a 48 colonne**: descrizione fino a 40 caratteri (`Tortellini in crema di Sua Maesta' il Ne`), accenti
  resi con l'apostrofo (`Maesta'`), importo allineato a destra.
- **Intestazione** (solo in "dettagli conto" e sullo scontrino): righe centrate, una riga di `-` e `EURO`.
  La ragione sociale è `Da inserire`, cioè non compilata in anagrafica.
- **Quantità**: righe separate mentre si batte; in "dettagli" le righe uguali diventano una, preceduta da
  `2 X 3,00 Euro`. Cambiando la quantità mentre si batte: `2 pz X 12,00 Euro`.
- **Sconti**: `SCONTO` tra `SUBTOT.` e `TOT. DA PAGARE`; sullo scontrino ripartito sotto ogni riga.
- **Pagamento**: `PAGATO CONTANTI:` / `PAGATO C.CREDITO:` e `Resto:` dopo il totale.
- **Prodotti con ` x ` nel nome** (`Tagliere Emilia x 2`): il vecchio monitor scarta ogni riga che contiene ` x `,
  quindi questi articoli sul suo display non compaiono.
