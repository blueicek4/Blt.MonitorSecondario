# Brief: "Bluetech Display" — il monitor cliente della cassa, per ristoranti e bar

## Contesto
Progetta l'interfaccia del monitor rivolto al cliente collegato alla cassa di ristoranti, pizzerie, bar e
locali che usano il gestionale Passepartout Menu. Il software è di Bluetech, azienda italiana di sistemi
gestionali per la ristorazione di alto livello. Il monitor è una pagina web a schermo intero (browser in
modalità kiosk) che riceve i dati dalla cassa in tempo reale.

Il cliente guarda lo schermo da 0,5–2 metri, per pochi secondi, mentre paga. Deve capire subito tre cose:
cosa sta pagando, quanto paga, come paga. Tutto il resto — effetti, pubblicità, brand — è al servizio di
queste tre risposte.

Obiettivo: un'interfaccia spettacolare, cinematografica e premium, che faccia dire al ristoratore
"la voglio" alla prima demo, senza mai sacrificare la leggibilità.

## Chi personalizza cosa
- Il locale personalizza: logo, colori, font, sfondo, immagini e video pubblicitari, testo scorrevole.
- Bluetech è il fornitore: il logo Bluetech e la scritta "powered by Bluetech" sono SEMPRE visibili in basso,
  in ogni stato e con ogni tema. Discreti ma sempre leggibili. Il tema del locale può solo sceglierne la
  variante chiara o scura per il contrasto: non può nasconderli, spostarli o rimpicciolirli.

## Stati da progettare (una schermata per stato, più le transizioni tra stati)
1. ATTESA — nessun conto in corso. Pubblicità a tutto schermo (immagini e video in playlist con transizioni
   eleganti: dissolvenza, Ken Burns, parallasse), logo del locale, testo scorrevole opzionale. Deve sembrare
   digital signage di alto livello, non uno screensaver.
2. CONTO IN CORSO — lo scontrino si compone in tempo reale. Righe con descrizione, quantità (quando c'è) e
   importo; righe speciali: sconti e maggiorazioni (importo negativo, colore d'accento), storni (barrati),
   righe di testo libero. Il TOTALE è grande e dominante. L'ultima riga aggiunta entra con un'animazione.
   Con conti lunghi (30+ righe) la lista scorre mostrando le righe più recenti; il totale resta sempre visibile.
   Accanto, uno spazio ridotto per le promozioni del locale.
   Variante: se per l'articolo appena battuto esiste una foto, appare nello spazio laterale con una transizione.
3. MESSAGGIO DALLA CASSA — la cassa invia brevi messaggi da "visore" (es. "TOTALE € 23,50", "RESTO € 1,50",
   "BUONGIORNO"): un'area messaggi ben visibile che non copre lo scontrino.
4. PAGAMENTO SATISPAY — il QR code ricevuto dalla cassa (immagine PNG) diventa protagonista: grande, centrato,
   su fondo bianco con il suo margine di rispetto, mai deformato, mai animato, mai coperto da effetti.
   Intorno al QR (non sopra) un'animazione d'attesa elegante. Importo da pagare, istruzione "Inquadra il QR
   con l'app Satispay", logo Satispay secondo le sue linee guida ufficiali. Niente countdown e niente esito:
   la cassa non comunica in modo affidabile se il pagamento è riuscito.
5. GRAZIE — a fine vendita: piccola celebrazione nei colori del locale, "Grazie e arrivederci", spazio per un
   QR opzionale (recensione Google, fidelity o menu digitale), poi ritorno automatico all'Attesa.
6. CONNESSIONE ASSENTE — indicatore discreto quando il monitor perde il collegamento con la cassa: non deve
   allarmare il cliente; l'ultima schermata resta visibile.

## Sistema di branding (design token)
Progetta tutto a token, perché ogni locale li modifica da un pannello: colore primario, secondario, accento,
sfondo, superficie (card dello scontrino), testo, testo su primario; font dei titoli e font del testo (solo
font open source incorporabili: il monitor può lavorare offline); raggio degli angoli; stile dello sfondo
(tinta unita, gradiente, gradiente animato "mesh", immagine, video); intensità effetto vetro 0–100;
intensità animazioni (Piena / Leggera / Eco).

Mostra lo stesso layout con quattro temi molto diversi tra loro:
- "Bluetech" (default): neutro, moderno, tecnologico;
- "Trattoria": caldo, carta e terracotta, serif elegante;
- "Fine dining": scuro, oro, minimale, molto respiro;
- "Cocktail bar": notte, neon, gradienti vivi.

## Motion design
Specifica durata, curva e coreografia di: ingresso di una riga (molla), aggiornamento del totale (cifre che
scorrono come un contachilometri), cambio di stato (transizioni condivise tra scontrino e QR), playlist
pubblicitaria, celebrazione finale. Gli effetti devono reggere su PC cassa modesti con grafica integrata:
anima solo transform e opacity; blur pesanti e WebGL solo nel livello "Piena", con un equivalente "Eco"
statico ma comunque bello. Rispetta prefers-reduced-motion.

## Vincoli
- Formati: progetta in 1920×1080 (16:9) e verifica 1366×768, 1280×800 (16:10, tipico monitor cliente da
  10–15"), 1024×768 (4:3) e verticale 1080×1920. Il layout si adatta, non si limita a scalare.
- Leggibilità: totale leggibile a 2 metri, righe dello scontrino a 1 metro; contrasto almeno WCAG AA in
  tutti i temi.
- Lingua italiana; importi con virgola decimale e simbolo euro ("€ 8,50"); numeri tabellari allineati.
- Dati realistici, per esempio: "Pizza Margherita 8,50", "Coperto ×2 4,00", "Acqua naturale 0,75 l 2,50",
  "Tiramisù 6,00", "Sconto 10% −2,10", totale "€ 18,90".
- Immagini del locale solo come segnaposto: niente foto stock da licenziare.

## Cosa consegnare
1. Pagina del design system: token, tipografia, palette dei quattro temi, componenti (riga scontrino, totale,
   area QR, area messaggi, testo scorrevole, footer Bluetech, indicatore di connessione).
2. Le sei schermate in 16:9 e 4:3; Attesa e Pagamento anche in verticale.
3. Prototipo interattivo con comandi per simulare: aggiungi riga, applica sconto, messaggio dalla cassa,
   mostra QR Satispay, fine vendita, connessione persa, cambio tema, cambio intensità animazioni.
4. Note per lo sviluppo (React + TypeScript): struttura dei componenti, token come variabili CSS,
   specifiche delle animazioni.
