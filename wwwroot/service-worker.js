// 開発中はキャッシュするとコードの修正が反映されにくいため、何もしない（本番は service-worker.published.js）
self.addEventListener('fetch', () => { });
self.addEventListener('message', event => {
    if (event.data === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});