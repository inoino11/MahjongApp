// 開発中はキャッシュが効くとコードの修正が反映されにくくなるため、あえて何もしません。
self.addEventListener('fetch', () => { });
self.addEventListener('message', event => {
    if (event.data === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});