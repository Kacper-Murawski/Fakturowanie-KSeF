// Service worker aplikacji (PWA, D45). Przechowuje WYŁĄCZNIE pliki aplikacji (Blazor, CSS, ikony) z listy
// wygenerowanej przy publikacji - nigdy odpowiedzi API (/api/...): dane faktur, kontrahentów i sesji zawsze
// pochodzą z serwera, a po wylogowaniu nic nie zostaje w pamięci podręcznej przeglądarki.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    // Pobranie plików aplikacji z kontrolą integralności (skrót SHA-256 z manifestu)
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    // Usunięcie pamięci podręcznej poprzednich wersji aplikacji
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        const url = new URL(event.request.url);
        // API (dane, PDF, XML, UPO) i Swagger - zawsze z sieci, nigdy z pamięci podręcznej
        const isApi = url.origin === self.origin && (url.pathname.startsWith('/api/') || url.pathname.startsWith('/swagger'));
        // Nawigacja w aplikacji (np. /faktury/5) - strona startowa z pamięci, routing robi Blazor
        const shouldServeIndexHtml = event.request.mode === 'navigate' && !isApi && !manifestUrlList.some(u => u === event.request.url);
        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        if (!isApi) {
            const cache = await caches.open(cacheName);
            cachedResponse = await cache.match(request);
        }
    }
    return cachedResponse || fetch(event.request);
}
