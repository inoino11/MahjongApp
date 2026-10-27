// ビルド時に自動生成されるファイル一覧（ハッシュ付き）を読み込む
self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    if (event.data === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.wasm/, /\.html/, /\.js/, /\.json/, /\.css/, /\.woff/, /\.png/, /\.jpe?g/, /\.gif/, /\.ico/, /\.blat/, /\.dat/];
// 書体のファイル（数MB）は最初にまとめて保存せず、画面に出た分だけ fontCacheName に保存する
const offlineAssetsExclude = [/^service-worker\.js$/, /^fonts\/.*\.woff2$/];
const fontCacheName = 'font-cache-v1'; // 版ごとに作り直さない（書体は変わらないため）

async function onInstall(event) {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method === 'GET' && /\/fonts\/.*\.woff2$/.test(new URL(event.request.url).pathname)) {
        const cache = await caches.open(fontCacheName);
        const hit = await cache.match(event.request);
        if (hit) return hit;
        const response = await fetch(event.request);
        if (response.ok) await cache.put(event.request, response.clone());
        return response;
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // 運営者用のページ（tools/）はアプリの画面ではないので、index.html に差し替えない
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !new URL(event.request.url).pathname.includes('/tools/');
        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }
    return cachedResponse || fetch(event.request);
}