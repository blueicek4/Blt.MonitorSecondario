# Registrazione del 20/10/2026, 07:56–08:01: QR Satispay, fattura, promozione

Quarta prova sul campo, stesso PC, ma con il **modello a 30 colonne** e l'anagrafica di un bar. Il file viene tolto
dopo ogni lettura (`/consuma` attivato 5 s dopo l'avvio). 19 versioni e 3 riscritture identiche, tutte registrate.

**Anonimizzata**: le righe dell'intestazione (nome del locale, indirizzo, comune, partita IVA e codice fiscale) sono
sostituite con testo della stessa lunghezza. **Il QR non è quello vero**: era un codice di pagamento di un esercente
reale. #08 contiene un QR finto con la stessa geometria (PNG 197×197, 49 moduli da 4 pixel, niente quiet zone), non
leggibile. Il vero era lungo 6580 caratteri in Base64 (file da 6659 byte). In `eventi.jsonl` `sha256`, `byte` e
`lunghezza` sono ricalcolati sui file sostituiti.

## I conti

| Versioni | Cosa (dalle note) | Da notare |
|---|---|---|
| #01 | File rimasto da una prova precedente | Scritto 21 s prima dell'avvio |
| #02–#10 | Bar: 5 articoli, "dettagli", **QR Satispay**, pagamento simulato in contanti | #07 intestazione e riscrittura identica 2,7 s dopo; #08 QR; #09 di nuovo il conto (uguale a #07) dopo 80 s di QR a video; #10 `PAGATO CONTANTI` |
| #11–#15 | Conto e **fattura** | Al monitor arriva come uno scontrino normale: nessun dato del cliente |
| #16–#19 | Conto con **promozione** | Lo sconto compare solo sullo scontrino finale, ripartito sotto le righe (`SCONTO -0,41`, `SCONTO -0,19`): 5,80 → 5,20 |

## Il QR Satispay

- **File**: `SNF`, una riga di 30 spazi, `QRCODE`, il PNG in Base64 su una riga sola, una riga di 30 spazi.
- **Immagine**: PNG RGBA 197×197, solo nero e bianco. QR versione 8: 49×49 moduli da 4 pixel, **senza quiet zone**
  (il codice tocca il bordo in alto e a sinistra, più un pixel bianco a destra e in basso).
- Il vecchio monitor lo mostra a grandezza naturale, 197 pixel. Il nuovo deve ingrandirlo di un multiplo intero
  (pixel netti) e aggiungere la quiet zone, come prevede `QrStage`. L'Agent può anche ridurlo a 49×49, un pixel per
  modulo, come il QR finto della cassa finta del display.
- **Ordine**: "dettagli" (#07) → QR (#08, 4,9 s dopo) → di nuovo il conto (#09) → `PAGATO` (#10). Il conto rimandato
  dopo il QR è il segnale per toglierlo (`pay.clear`), come fa il vecchio monitor con qualunque file nuovo.
- **Ancora da vedere**: come compare un pagamento Satispay vero. Qui il pagamento è stato simulato in contanti.

## Altro da notare

- **"Dettagli" rimandato due volte**: dopo ogni intestazione arriva una riscrittura identica (2,7 s, 0,3 s, 0,3 s dopo).
- **Battute a raffica**: con il file tolto, versioni a 0,17–0,41 s l'una dall'altra.
- **Conto nuovo**: sempre la versione dopo un `PAGATO`.
