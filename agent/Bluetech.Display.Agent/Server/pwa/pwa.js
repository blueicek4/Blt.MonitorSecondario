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

  // Il cookie del dispositivo dura 400 giorni e si rinnova a ogni visita: una pagina aperta per mesi lo rinnova da sé.
  setInterval(function () {
    fetch("/api/abbinamento", { cache: "no-store", credentials: "same-origin" }).catch(function () {});
  }, 6 * 60 * 60 * 1000);

  // "Non ora" vale 30 giorni (SPEC §21): poi l'invito torna, una volta, sullo stesso dispositivo.
  var SILENCE = 30 * 24 * 60 * 60 * 1000;
  function dismissed() {
    try {
      var value = localStorage.getItem(KEY);
      return value === "installata" || (value !== null && Date.now() - Number(value) < SILENCE);
    } catch (e) { return false; }
  }
  function remember(installedNow) {
    try { localStorage.setItem(KEY, installedNow ? "installata" : String(Date.now())); } catch (e) { /* senza memoria si richiede la volta dopo */ }
  }

  if (installed || dismissed())
    return;

  function hide() {
    if (banner) banner.remove();
    banner = null;
  }

  // Una banda sopra lo schermo vero, che continua a funzionare sotto (Dispositivo-Pagine del design), con i colori del
  // tema del locale quando la pagina li ha.
  function show(text, action, run) {
    hide();
    var style = document.createElement("style");
    style.textContent =
      ".bt-pwa{position:fixed;left:50%;transform:translateX(-50%);bottom:max(16px,env(safe-area-inset-bottom));z-index:2147483647;" +
      "width:min(760px,calc(100% - 32px));display:flex;flex-wrap:wrap;align-items:center;gap:10px 16px;padding:14px 16px;" +
      "background:var(--c-surface-solid,#141b26);color:var(--c-text,#e6eaf2);border:1px solid var(--c-line,rgba(255,255,255,.14));border-radius:14px;" +
      "box-shadow:0 12px 40px rgba(0,0,0,.35);font:15px/1.4 var(--font-text,system-ui),'Segoe UI',sans-serif}" +
      ".bt-pwa .bt-icon{flex:none;width:40px;height:40px;border-radius:10px;border:1.5px solid var(--c-primary,#1f6feb);display:grid;place-items:center}" +
      ".bt-pwa .bt-icon svg{width:22px;height:22px}" +
      ".bt-pwa .bt-text{flex:1 1 240px;margin:0}.bt-pwa .bt-text b{display:block;font-weight:600}.bt-pwa .bt-text span{color:var(--c-muted,#9aa6ba);font-size:14px}" +
      ".bt-pwa button{font:600 15px/1 var(--font-text,system-ui),'Segoe UI',sans-serif;border-radius:10px;padding:11px 16px;min-height:44px;" +
      "border:1px solid var(--c-line,rgba(255,255,255,.14));background:transparent;color:inherit;cursor:pointer}" +
      ".bt-pwa button.bt-main{background:var(--c-primary,#1f6feb);border-color:var(--c-primary,#1f6feb);color:var(--c-on-primary,#fff)}";
    banner = document.createElement("div");
    banner.className = "bt-pwa";
    banner.setAttribute("role", "dialog");
    banner.setAttribute("aria-label", "Installa il monitor");
    var icon = document.createElement("span");
    icon.className = "bt-icon";
    icon.innerHTML = '<svg viewBox="0 0 34 34" aria-hidden="true"><path d="M11 24V10h6.5a4 4 0 0 1 0 8H11m0 0h7.5a4 4 0 0 1 0 6H11" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"></path></svg>';
    var message = document.createElement("p");
    message.className = "bt-text";
    var title = document.createElement("b");
    title.textContent = "Installa il monitor come app";
    var detail = document.createElement("span");
    detail.textContent = text;
    message.append(title, detail);
    banner.append(style, icon, message);
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
    later.addEventListener("click", function () { hide(); remember(false); });
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
    show("Parte a schermo intero e si riapre da solo dopo un riavvio.", "Installa", function () {
      var prompt = deferred;
      deferred = null;
      hide();
      prompt.prompt();
    });
  });
  window.addEventListener("appinstalled", function () {
    hide();
    remember(true);
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
