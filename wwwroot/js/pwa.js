// 「アプリとして追加」ボタン用。ブラウザのインストール案内を後から出せるよう保持する
window.pwaInstall = {
    deferredPrompt: null,
    dotNetHelper: null,
    init: function (helper) {
        this.dotNetHelper = helper;
        // 先にインストール可能になっていた場合も通知する
        if (this.deferredPrompt) {
            this.dotNetHelper.invokeMethodAsync('OnInstallAvailable');
        }
    },
    // インストール案内を表示し、追加されたら true を返す
    prompt: async function () {
        if (!this.deferredPrompt) {
            return false;
        }
        this.deferredPrompt.prompt();
        const { outcome } = await this.deferredPrompt.userChoice;
        this.deferredPrompt = null;
        return outcome === 'accepted';
    }
};
// OS と、ホーム画面から起動しているか（PWA）を判定する
window.pwaHelper = {
    getEnvironmentInfo: function () {
        const ua = window.navigator.userAgent;
        // iPadOS はデスクトップの Mac として名乗るため、タッチ対応かどうかで見分ける
        const isIOS = /iPad|iPhone|iPod/.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
        const isAndroid = /Android/.test(ua);
        const isWindows = /Windows/.test(ua);
        const isPwa = window.navigator.standalone === true || window.matchMedia('(display-mode: standalone)').matches;
        return {
            isIOS: isIOS,
            isAndroid: isAndroid,
            isWindows: isWindows,
            isPwa: isPwa
        };
    }
};
// ブラウザがインストール可能と判定したとき
window.addEventListener('beforeinstallprompt', (e) => {
    // 標準の案内バーは出さず、アプリのボタンから prompt() を呼べるよう保持する
    e.preventDefault();
    window.pwaInstall.deferredPrompt = e;
    if (window.pwaInstall.dotNetHelper) {
        window.pwaInstall.dotNetHelper.invokeMethodAsync('OnInstallAvailable');
    }
});
// インストールが完了したとき
window.addEventListener('appinstalled', () => {
    window.pwaInstall.deferredPrompt = null;
    if (window.pwaInstall.dotNetHelper) {
        window.pwaInstall.dotNetHelper.invokeMethodAsync('OnInstalled');
    }
});

// 新しいバージョンがないか手動で確認する（見つかれば index.html の更新処理が動く）
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
