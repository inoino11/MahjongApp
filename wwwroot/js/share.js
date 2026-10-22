// 画像シェア機能
window.exportAndShareResult = async (elementId) => {
    const element = document.getElementById(elementId);
    if (!element) return "ERROR: Element not found";
    try {
        const canvas = await html2canvas(element, {
            backgroundColor: "#1a1a27",
            scale: 2,
            logging: false
        });
        return new Promise((resolve) => {
            canvas.toBlob(async (blob) => {
                if (!blob) {
                    resolve("ERROR: Blob creation failed");
                    return;
                }
                const file = new File([blob], 'mahjong_result.png', { type: 'image/png' });
                const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
                // 2. 【スマホ向け】モバイル端末 ＆ Web Share APIが使える場合
                if (isMobile && navigator.canShare && navigator.canShare({ files: [file] })) {
                    navigator.share({
                        files: [file]
                    }).catch(e => console.log("Share aborted", e));
                    resolve("SHARED");
                    return;
                }
                // 3. 【PC向け】クリップボードに画像を直接コピー
                try {
                    if (navigator.clipboard && window.ClipboardItem) {
                        await navigator.clipboard.write([
                            new ClipboardItem({ [file.type]: blob })
                        ]);
                        resolve("COPIED");
                        return;
                    }
                } catch (e) {
                    console.log("Clipboard error fallback", e);
                }
                // 4. 【最終手段】未対応ブラウザの場合はダウンロード
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = 'mahjong_result.png';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
                resolve("DOWNLOADED");
            }, 'image/png');
        });
    } catch (error) {
        return "ERROR: " + error.message;
    }
};

// URLシェア機能
window.shareUrlNative = async (title, url) => {
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    // 1. 【スマホ向け】モバイル端末 ＆ Web Share APIが使える場合
    if (isMobile && navigator.share) {
        navigator.share({
            title: title,
            url: url
        }).catch(err => console.log("Share aborted", err));
        return "SHARED";
    } else {
        // 2. 【PC向け】クリップボードにURLを直接コピー
        try {
            if (navigator.clipboard) {
                await navigator.clipboard.writeText(url);
                return "COPIED";
            } else {
                return "ERROR: Clipboard API not available";
            }
        } catch (err) {
            return "ERROR: " + err.message;
        }
    }
};

// JSONバックアップ
window.shareOrDownloadFile = async (filename, contentType, content) => {
    // 直接ダウンロード用のフォールバック関数
    const fallbackDownload = () => {
        const blob = new Blob([content], { type: contentType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    };
    try {
        // 文字列(JSON)からFileオブジェクトを生成
        const file = new File([content], filename, { type: contentType });
        const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
        // 端末がモバイルで、かつファイル共有に対応しているかチェック
        if (isMobile && navigator.canShare && navigator.canShare({ files: [file] })) {
            try {
                await navigator.share({
                    files: [file]
                });
                return "SHARED";
            } catch (error) {
                // ユーザーがシェア画面を閉じた場合 (AbortError) はキャンセル扱い
                if (error.name !== 'AbortError') {
                    console.error('シェア処理に失敗しました。ダウンロードにフォールバックします:', error);
                    fallbackDownload();
                    return "DOWNLOADED";
                }
                return "CANCELED";
            }
        } else {
            // PCブラウザなど非対応の場合は直接ダウンロード
            fallbackDownload();
            return "DOWNLOADED";
        }
    } catch (e) {
        console.error("ファイル処理エラー:", e);
        // 予期せぬエラー時も念のためダウンロードを試行
        fallbackDownload();
        return "DOWNLOADED";
    }
};
