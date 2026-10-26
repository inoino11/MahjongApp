// 要素を画像にして共有する。スマホは共有シート → PC はクリップボード → どちらも無理ならダウンロード
// 戻り値: SHARED / COPIED / DOWNLOADED / CANCELED / ERROR: 〜
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
                const download = () => {
                    const url = URL.createObjectURL(blob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = 'mahjong_result.png';
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                };
                const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
                if (isMobile && navigator.canShare && navigator.canShare({ files: [file] })) {
                    try {
                        await navigator.share({ files: [file] });
                        resolve("SHARED");
                    } catch (e) {
                        if (e.name === 'AbortError') {
                            resolve("CANCELED");
                        } else {
                            // 画像の作成に時間がかかり、ボタンを押してからの猶予（Android の Chrome は約5秒）を
                            // 過ぎると共有画面を開けない（NotAllowedError）。その場合はダウンロードに切り替える
                            download();
                            resolve("DOWNLOADED");
                        }
                    }
                    return;
                }
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
                download();
                resolve("DOWNLOADED");
            }, 'image/png');
        });
    } catch (error) {
        return "ERROR: " + error.message;
    }
};

// URLを共有する。スマホは共有シート、PC はクリップボード
// 戻り値: SHARED / COPIED / CANCELED / RETRY / ERROR: 〜
// RETRY: URLの発行に時間がかかり、ボタンを押してからの猶予を過ぎて共有できなかった（もう一度押せば共有できる）
window.shareUrlNative = async (title, url) => {
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    if (isMobile && navigator.share) {
        try {
            await navigator.share({ title: title, url: url });
            return "SHARED";
        } catch (err) {
            if (err.name === 'AbortError') return "CANCELED";
            if (err.name === 'NotAllowedError') return "RETRY";
            return "ERROR: " + err.message;
        }
    } else {
        try {
            if (navigator.clipboard) {
                await navigator.clipboard.writeText(url);
                return "COPIED";
            } else {
                return "ERROR: Clipboard API not available";
            }
        } catch (err) {
            if (err.name === 'NotAllowedError') return "RETRY";
            return "ERROR: " + err.message;
        }
    }
};

// 文字列を共有する。スマホは共有シート（メモやパスワードのアプリに送れる）、PC はクリップボード
// 戻り値: SHARED / COPIED / CANCELED / RETRY / ERROR: 〜
window.shareTextNative = async (title, text) => {
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    if (isMobile && navigator.share) {
        try {
            await navigator.share({ title: title, text: text });
            return "SHARED";
        } catch (err) {
            if (err.name === 'AbortError') return "CANCELED";
            if (err.name === 'NotAllowedError') return "RETRY";
            return "ERROR: " + err.message;
        }
    }
    return await window.copyTextToClipboard(text);
};

// 文字列をクリップボードにコピーする。戻り値: COPIED / RETRY / ERROR: 〜
window.copyTextToClipboard = async (text) => {
    try {
        if (!navigator.clipboard) return "ERROR: Clipboard API not available";
        await navigator.clipboard.writeText(text);
        return "COPIED";
    } catch (err) {
        if (err.name === 'NotAllowedError') return "RETRY";
        return "ERROR: " + err.message;
    }
};

// バックアップファイルを共有またはダウンロードする
// 戻り値: SHARED / DOWNLOADED / CANCELED
window.shareOrDownloadFile = async (filename, contentType, content) => {
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
        const file = new File([content], filename, { type: contentType });
        const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
        if (isMobile && navigator.canShare && navigator.canShare({ files: [file] })) {
            try {
                await navigator.share({
                    files: [file]
                });
                return "SHARED";
            } catch (error) {
                // 共有画面を閉じただけ（AbortError）ならキャンセル扱い
                if (error.name !== 'AbortError') {
                    console.error('シェア処理に失敗しました。ダウンロードにフォールバックします:', error);
                    fallbackDownload();
                    return "DOWNLOADED";
                }
                return "CANCELED";
            }
        } else {
            fallbackDownload();
            return "DOWNLOADED";
        }
    } catch (e) {
        console.error("ファイル処理エラー:", e);
        // 想定外のエラーでもバックアップだけは残せるようダウンロードを試みる
        fallbackDownload();
        return "DOWNLOADED";
    }
};
