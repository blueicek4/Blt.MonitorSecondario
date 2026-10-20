// Il monitor cliente come app sui dispositivi mobili, aggiunto dall'Agent alla pagina /cliente (anche al display del
// design): l'invito a installarlo (schermo intero su Android, istruzioni su iPhone e iPad), il service worker per la
// pagina di attesa quando l'Agent non risponde e lo schermo sempre acceso finché la pagina è aperta.
(function () {
  "use strict";
  var KEY = "bluetech.monitor.installazione";
  var installed = matchMedia("(display-mode: fullscreen)").matches || matchMedia("(display-mode: standalone)").matches
    || navigator.standalone === true;
  var ios = /iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  var android = /Android/i.test(navigator.userAgent);
  var banner = null;
  var deferred = null;

  // Il service worker c'è solo su HTTPS (o su localhost): senza, l'app funziona lo stesso, solo senza pagina di attesa.
  if ("serviceWorker" in navigator && window.isSecureContext)
    navigator.serviceWorker.register("/sw.js").catch(function () {});

  // Schermo sempre acceso: un palmare o un tablet non deve spegnersi mentre mostra il conto (anche questo solo su HTTPS).
  var lock = null;
  function keepAwake() {
    if (!("wakeLock" in navigator) || document.visibilityState !== "visible" || lock)
      return;
    navigator.wakeLock.request("screen").then(function (sentinel) {
      lock = sentinel;
      sentinel.addEventListener("release", function () { lock = null; });
    }).catch(function () {});
  }
  document.addEventListener("visibilitychange", keepAwake);
  keepAwake();

  function dismissed() {
    try { return localStorage.getItem(KEY) === "no"; } catch (e) { return false; }
  }
  function remember() {
    try { localStorage.setItem(KEY, "no"); } catch (e) { /* senza memoria si richiede la volta dopo */ }
  }

  if (installed || dismissed())
    return;

  function hide() {
    if (banner) banner.remove();
    banner = null;
  }

  function show(text, action, run) {
    hide();
    var style = document.createElement("style");
    style.textContent =
      ".bt-pwa{position:fixed;left:50%;transform:translateX(-50%);bottom:max(16px,env(safe-area-inset-bottom));z-index:2147483647;" +
      "width:min(560px,calc(100% - 32px));display:flex;flex-wrap:wrap;align-items:center;gap:10px 14px;padding:14px 16px;" +
      "background:#141b26;color:#e6eaf2;border:1px solid rgba(255,255,255,.14);border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.45);" +
      "font:15px/1.4 system-ui,'Segoe UI',sans-serif}" +
      ".bt-pwa p{margin:0;flex:1 1 220px}.bt-pwa button{font:600 15px/1 system-ui,'Segoe UI',sans-serif;border-radius:10px;padding:11px 14px;" +
      "border:1px solid rgba(255,255,255,.14);background:#1a2331;color:#e6eaf2;cursor:pointer}" +
      ".bt-pwa button.bt-main{background:#3ddc97;border-color:#3ddc97;color:#062417}";
    banner = document.createElement("div");
    banner.className = "bt-pwa";
    banner.setAttribute("role", "dialog");
    banner.setAttribute("aria-label", "Installa il monitor");
    var message = document.createElement("p");
    message.textContent = text;
    banner.append(style, message);
    if (action) {
      var main = document.createElement("button");
      main.type = "button";
      main.className = "bt-main";
      main.textContent = action;
      main.addEventListener("click", run);
      banner.append(main);
    }
    var later = document.createElement("button");
    later.type = "button";
    later.textContent = action ? "Non ora" : "Ok";
    later.addEventListener("click", function () { hide(); remember(); });
    banner.append(later);
    document.body.append(banner);
  }

  function fullScreen() {
    var page = document.documentElement;
    var request = page.requestFullscreen || page.webkitRequestFullscreen;
    if (request) {
      try {
        var result = request.call(page, { navigationUI: "hide" });
        if (result && result.catch) result.catch(function () {});
      } catch (e) { /* non supportato: resta la pagina normale */ }
    }
    hide();
  }

  // Chrome, Edge e Samsung Internet su HTTPS: il browser offre l'installazione, il pulsante la apre.
  window.addEventListener("beforeinstallprompt", function (event) {
    event.preventDefault();
    deferred = event;
    show("Installa il monitor come app: si apre a schermo intero e resta nella schermata Home.", "Installa", function () {
      var prompt = deferred;
      deferred = null;
      hide();
      prompt.prompt();
    });
  });
  window.addEventListener("appinstalled", function () {
    hide();
    remember();
  });

  // Dove il browser non offre l'installazione: le istruzioni su iPhone e iPad, lo schermo intero altrove su Android.
  setTimeout(function () {
    if (deferred || banner)
      return;
    if (ios)
      show("Per vederlo a schermo intero: tocca Condividi e poi «Aggiungi alla schermata Home».", null, null);
    else if (android)
      show(window.isSecureContext
        ? "Questo browser non installa le app: puoi metterlo a schermo intero."
        : "Per installarlo come app serve l'indirizzo https dell'Agent. Intanto puoi metterlo a schermo intero.", "Schermo intero", fullScreen);
  }, 3000);
})();
