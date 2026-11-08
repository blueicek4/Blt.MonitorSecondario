# Registrazione dell'08/11/2026, 17:37–18:09: i sei flussi di pagamento

Prima registrazione con il percorso guidato del registratore 1.1.1 (solo il file, niente foto né rete). Modello a
**48 colonne**, Windows 11, file tolto dopo ogni lettura per tutta la sessione. 59 versioni e 12 riscritture identiche
in 32 minuti. Flussi fatti: 1 contanti, 2 POS, 4 Satispay, 5 scontrino digitale (due volte), 6 annullo; il 3 (Glory)
no. Chi ha registrato avverte di aver fatto "un po' di macello con i comandi": i passi segnati non sempre coincidono
con quello che è successo in cassa, quindi le versioni contano più dei passi.

**Anonimizzata**: le righe dell'intestazione (nome del locale, indirizzo, comune, partita IVA e codice fiscale) sono
sostituite con testo della stessa lunghezza (`BAR ESEMPIO`, `VIALE DELLE PROVE, 23`, `00000 COMUNE DI PROVA … XX`).
**I QR non sono quelli veri**: i due di Satispay (#35, #38) e i due dello scontrino digitale (#54, #58) sono rifatti con
la stessa geometria (PNG RGBA 197×197, versione 8, livello Q, 49 moduli da 4 pixel, niente quiet zone) e un contenuto
finto ma della stessa forma: `S6Y-PAY--00000000-0000-4000-8000-0000000000NN` e il link dello scontrino digitale con il
token `00000000-0000-4000-8000-0000000000NN` (NN = numero della versione). Lo stesso token finto è nel testo di #55 e
#59 e in `eventi.jsonl`; `sha256`, `byte` e `qr.lunghezza` sono ricalcolati sui file sostituiti. In `contesto` c'è il
modello XML del monitor usato quel giorno, com'era.

## I conti

| Versioni | Flusso | Da notare |
|---|---|---|
| #01–#12 | 1, contanti | Battuta con storni, "dettagli" (#10), **sconti ripartiti** sotto ogni riga fino a 0,10 (#11), `PAGATO CONTANTI` (#12) |
| #13–#23 | 1, contanti | Righe uguali unite in "dettagli" (`3 pz X 3,90 Euro`), `PAGATO` |
| #24–#28 | 1, contanti | Peso (`2 kg X 28,90 Euro`), `PAGATO CONTANTI: 10,00`, `Resto: 7,00` |
| #29–#30 | 2, POS | Due righe, poi **più niente**: nessun conto, nessun `PAGATO`. La vendita continua nel flusso 4 (#31 ha le stesse righe più una): il pagamento con il POS probabilmente non è andato a buon fine. Il 20/10 un POS riuscito aveva scritto `PAGATO C.CREDITO` |
| #31–#35 | 4, Satispay | Conto con sconto sul totale, poi **QR Satispay** (#35). Dopo il pagamento dall'app **nessun `PAGATO`**: la versione dopo (#36, 91 s più tardi) è già il conto successivo |
| #36–#38 | 4, Satispay | Secondo QR Satispay (#38), annullato o scaduto: anche qui niente |
| #39–#44 | 5, scontrino digitale | Due pagamenti in contanti senza nessun QR (probabilmente lo scontrino digitale non era ancora attivo: il QR compare solo dalle 18:06) |
| #45–#47 | 6, annullo | Scontrino pagato (#47). **L'annullo non scrive niente**: la versione dopo (#48) è 8 minuti più tardi |
| #48–#50 | 6 | Un altro conto pagato |
| #51 | 6 | `BUONO welcome20 Sconto 20,00`: la descrizione di un buono del 20% (15,50 → `SCONTO -3,10`), **non un articolo da 20 euro**. L'importo non è allineato a destra: la riga continua con spazi fino alla 48ª colonna |
| #52–#55 | 6 | **Scontrino digitale**: conto (#53), QR (#54), 0,3 s dopo lo scontrino pagato con il link in fondo (#55) |
| #56–#59 | 5 | Di nuovo lo scontrino digitale, stessa sequenza (#57, #58, #59) |

## Lo scontrino digitale

- **#54 / #58, il QR**: `SNF`, una riga di 48 spazi, `QRCODE` (non `QRCODESD` come il vecchio monitor 6.0), il PNG in
  Base64, la didascalia `Scarica il tuo scontrino digitale!`. Il QR porta il link intatto,
  `https://scontrinodigitalecloud.passepartout.cloud/v1/download?token=…`. Arriva **prima** dello scontrino pagato, a
  vendita aperta: prima delle correzioni dell'08/11 l'Agent lo mostrava come un QR di Satispay.
- **#55 / #59, lo scontrino pagato**, 0,31 s dopo: intestazione, righe, `PAGATO CONTANTI`, `Resto`, poi `QRCODE` e il link
  **come testo**, storpiato dal modello XML (`StampaQRCode`, `QRCODE` e a capo `{0}`): la barra diventa un trattino e il
  punto di domanda uno spazio, anche se il modello mette `/` tra i `CaratteriAmmessi`:
  `https:--scontrinodigitalecloud.passepartout.cloud-v1-download token=…`. Prima delle correzioni l'Agent leggeva
  questo file come vuoto e perdeva il `PAGATO`: la vendita restava aperta e si mescolava con quella dopo.
- **`ScontrinoDigitale.txt`**: Passepartout crea anche questo file nella stessa cartella, 5 s prima del QR (18:06:29,9;
  di nuovo alle 18:09:07). Il registratore 1.1.1 ha visto solo che c'era, non cosa conteneva: dalla 1.2 ne copia il
  contenuto in `altri\`.

## Cosa non scrive Passepartout

- **Durante** il pagamento con il POS, con la cassa automatica (dichiarato da chi ha registrato, flusso 3 non
  registrato) e con Satispay: niente. Il display resta sul conto, o sul QR di Satispay.
- **Dopo** un pagamento Satispay riuscito: nessun `PAGATO` (#35 → #36).
- **L'annullo** di uno scontrino: niente.
