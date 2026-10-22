// PWAインストール制御用のグローバルオブジェクト
window.pwaInstall = {
    deferredPrompt: null,
    dotNetHelper: null,
    // Blazorコンポーネントからの参照を受け取る
    init: function (helper) {
        this.dotNetHelper = helper;
        // 既にイベントが発火して待機状態であれば、Blazor側に通知
        if (this.deferredPrompt) {
            this.dotNetHelper.invokeMethodAsync('OnInstallAvailable');
        }
    },
    // インストールプロンプトを表示
    prompt: async function () {
        if (!this.deferredPrompt) {
            return false;
        }
        // ダイアログを表示
        this.deferredPrompt.prompt();
        // ユーザーの選択結果（インストールしたか、キャンセルしたか）を待つ
        const { outcome } = await this.deferredPrompt.userChoice;
        this.deferredPrompt = null;
        return outcome === 'accepted';
    }
};
// OSとPWAを判定
window.pwaHelper = {
    getEnvironmentInfo: function () {
        const ua = window.navigator.userAgent;
        // OS判定
        const isIOS = /iPad|iPhone|iPod/.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
        const isAndroid = /Android/.test(ua);
        const isWindows = /Windows/.test(ua);
        // PWA判定
        const isPwa = window.navigator.standalone === true || window.matchMedia('(display-mode: standalone)').matches;
        return {
            isIOS: isIOS,
            isAndroid: isAndroid,
            isWindows: isWindows,
            isPwa: isPwa
        };
    }
};
// ブラウザが「インストール可能」と判定した時に発火するイベント
window.addEventListener('beforeinstallprompt', (e) => {
    // デフォルトのミニインフォバー表示を防止
    e.preventDefault();
    // イベントを保存しておく（後でprompt()を呼ぶため）
    window.pwaInstall.deferredPrompt = e;
    // Blazor側へ「インストール可能になった」ことを通知
    if (window.pwaInstall.dotNetHelper) {
        window.pwaInstall.dotNetHelper.invokeMethodAsync('OnInstallAvailable');
    }
});
// インストールが完了した時に発火するイベント
window.addEventListener('appinstalled', () => {
    window.pwaInstall.deferredPrompt = null;
    if (window.pwaInstall.dotNetHelper) {
        window.pwaInstall.dotNetHelper.invokeMethodAsync('OnInstalled');
    }
});

// PWAの手動アップデートチェック関数
window.checkForUpdate = async function () {
    if ('serviceWorker' in navigator) {
        try {
            const registration = await navigator.serviceWorker.getRegistration();
            if (registration) {
                await registration.update();
                return "CHECKED";
            } else {
                return "NOT_REGISTERED";
            }
        } catch (err) {
            console.error("更新チェックエラー:", err);
            return "ERROR";
        }
    }
    return "UNSUPPORTED";
};
