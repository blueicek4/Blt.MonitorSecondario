// Il service worker del monitor cliente. Non tiene in cache i conti (arrivano dal WebSocket): quando l'Agent non
// risponde (riavvio, Wi-Fi caduto) mostra una pagina di attesa che riprova da sola, al posto dell'errore del browser.
"use strict";
const CACHE = "bluetech-monitor-1";
const OFFLINE = "/pwa/offline.html";

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.add(OFFLINE)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(caches.keys()
    .then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
    .then(() => self.clients.claim()));
});

self.addEventListener("fetch", (event) => {
  const path = new URL(event.request.url).pathname;
  if (event.request.mode !== "navigate" || !["/", "/cliente", "/auth"].includes(path))
    return;   // tutto il resto va in rete com'è
  event.respondWith(fetch(event.request).catch(() => caches.match(OFFLINE)));
});
