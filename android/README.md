# Bluetech Display per Android

L'app del monitor cliente per i tablet Android: la pagina dell'Agent a schermo intero, come il
[Player](../player/README.md) su Windows. Al posto della pagina installata dal browser (PWA) non ha bisogno di HTTPS
per restare a schermo intero, tiene lo schermo acceso, non mostra barre né indirizzi e si riprende da sola se l'Agent
non risponde.

## Requisiti

- Android 8.0 o successivo, con Android System WebView (o Chrome) aggiornato.
- Il tablet nella **rete del personale**, mai in quella degli ospiti: deve raggiungere il PC dell'Agent
  (`http://<pc-dell-agent>:5080`).

## Installazione

1. Scarica `BluetechDisplay.apk` (artefatto `BluetechDisplayAndroid` della build) e aprilo sul tablet. Android chiede
   di consentire l'installazione da questa origine: si consente una volta, per il browser o il gestore file usato.
2. Al primo avvio: l'**indirizzo dell'Agent** (`192.168.1.20:5080`, senza `http://` viene aggiunto) e il **codice di
   accesso** (almeno 4 caratteri), che protegge configurazione e uscita.
3. Lo schermo mostra un codice a 6 cifre: si scrive nel back office dell'Agent, in **Schermi**, scegliendo la cassa e,
   per la copia di carta, la stampante. Il tablet passa da solo allo schermo del cliente e resta abbinato.
4. **Consigliato**: menu › "Scegli l'app della schermata Home" › Bluetech Display. Il tablet si accende direttamente sul
   monitor e il tasto Home ci torna. Per bloccarlo del tutto, Android ha anche il "Blocco app" (Fissa app) nelle
   impostazioni di sicurezza.

## Uso

| Azione | Come |
|---|---|
| Configurazione, ricarica, uscita | **cinque tocchi di fila nell'angolo in alto a sinistra**, poi il codice. Dopo 5 codici errati, 60 secondi di blocco |
| Cambiare l'Agent o il codice | menu › Indirizzo dell'Agent e codice |
| Tornare alla schermata Home di prima | menu › Scegli l'app della schermata Home |

- **Schermo intero e sempre acceso** mentre l'app è aperta; le barre di sistema compaiono solo scorrendo dal bordo.
- Il tasto Indietro non esce. Si aprono solo le pagine dell'Agent: un link verso un altro sito non si apre.
- Se l'Agent non risponde: "Mi ricollego alla cassa…" e un nuovo tentativo dopo 5 secondi, poi fino a 60.
- La copia di carta: il tablet è touch, quindi con una stampante assegnata in Schermi compare il pulsante "Voglio la
  copia di carta" sotto il QR dello scontrino digitale.
- HTTPS: con un certificato valido per il tablet funziona; con il certificato generato dall'Agent il tablet non si
  fida e l'app lo dice (mai accettato in silenzio). Nella rete del locale si usa `http://`.

## Firma e aggiornamenti

La build firma l'APK con la chiave nei segreti del repository (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`). Con la stessa chiave ogni nuova versione si installa sopra la vecchia e
l'abbinamento resta. **Finché i segreti non ci sono** l'APK è firmato con una chiave di prova diversa a ogni build: va
bene per provarlo, ma per aggiornarlo bisogna prima disinstallare la versione vecchia (e riabbinare il tablet).

Per creare la chiave, una volta, su un PC con Java (la si conserva come una password: chi la ha può firmare
aggiornamenti dell'app):

```bat
keytool -genkeypair -v -keystore bluetech-display.keystore -alias bluetech -keyalg RSA -keysize 2048 -validity 10000
certutil -encode bluetech-display.keystore keystore.b64
```

Poi in GitHub › Settings › Secrets and variables › Actions: il contenuto di `keystore.b64` senza le righe
`-----BEGIN/END CERTIFICATE-----` in `ANDROID_KEYSTORE_BASE64`, le due password e l'alias negli altri tre.

## Compilazione

Serve l'SDK Android (la build gira su GitHub Actions, job `android`):

```bash
gradle -p android assembleRelease   # Gradle 8.9, JDK 17
```
