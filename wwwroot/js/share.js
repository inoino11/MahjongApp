// 要素を画像にして共有する。スマホは共有シート → PC はクリップボード → どちらも無理ならダウンロード
// 戻り値: SHARED / COPIED / DOWNLOADED / ERROR: 〜
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
                if (isMobile && navigator.canShare && navigator.canShare({ files: [file] })) {
                    navigator.share({
                        files: [file]
                    }).catch(e => console.log("Share aborted", e));
                    resolve("SHARED");
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

// URLを共有する。スマホは共有シート、PC はクリップボード
// 戻り値: SHARED / COPIED / ERROR: 〜
window.shareUrlNative = async (title, url) => {
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    if (isMobile && navigator.share) {
        navigator.share({
            title: title,
            url: url
        }).catch(err => console.log("Share aborted", err));
        return "SHARED";
    } else {
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
