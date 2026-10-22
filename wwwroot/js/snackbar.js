// トーストのスワイプ消去ロジック
document.addEventListener('DOMContentLoaded', () => {
    let startX = 0;
    let startY = 0;
    let currentSnackbar = null;
    let isDragging = false;
    document.addEventListener('touchstart', (e) => {
        const snackbar = e.target.closest('.mud-snackbar');
        if (snackbar) {
            currentSnackbar = snackbar;
            startX = e.touches[0].clientX;
            startY = e.touches[0].clientY;
            isDragging = true;
            // 個別のIDがない場合は付与する（動的CSSでターゲットにするため）
            if (!currentSnackbar.id) {
                currentSnackbar.id = 'toast-' + Date.now() + '-' + Math.floor(Math.random() * 1000);
            }
            // 指の動きに即座に追従させる
            currentSnackbar.style.transition = 'none';
        }
    }, { passive: true });
    document.addEventListener('touchmove', (e) => {
        if (!isDragging || !currentSnackbar) return;
        const deltaX = e.touches[0].clientX - startX;
        const deltaY = e.touches[0].clientY - startY;
        // 制限を解除し、全方向に指へ完全に追従させる
        currentSnackbar.style.transform = `translate(${deltaX}px, ${deltaY}px)`;
    }, { passive: true });
    document.addEventListener('touchend', (e) => {
        if (!isDragging || !currentSnackbar) return;
        const deltaX = e.changedTouches[0].clientX - startX;
        const deltaY = e.changedTouches[0].clientY - startY;
        // 吹き飛ばす判定の閾値（全方向共通で50px）
        const threshold = 50;
        if (Math.abs(deltaX) > threshold || Math.abs(deltaY) > threshold) {
            // --- 閾値を超えたら吹き飛ばす ---
            let escapeX = 0;
            let escapeY = 0;
            // より大きく動かした方向（X軸かY軸か）へ一直線に飛ばす
            if (Math.abs(deltaX) > Math.abs(deltaY)) {
                escapeX = deltaX > 0 ? 200 : -200; // 左右
            } else {
                escapeY = deltaY > 0 ? 200 : -200; // 上下
            }
            const toastId = currentSnackbar.id;
            // Blazorの再描画によるチラつきを防ぐ動的CSS
            const styleEl = document.createElement('style');
            styleEl.innerHTML = `
                #${toastId} {
                    transform: translate(${escapeX}px, ${escapeY}px) !important;
                    opacity: 0 !important;
                    transition: transform 0.2s ease-out, opacity 0.2s ease-out !important;
                    pointer-events: none !important;
                }
            `;
            document.head.appendChild(styleEl);
            // Blazor側で要素が安全に破棄された頃合いにお掃除
            setTimeout(() => {
                styleEl.remove();
            }, 3000);
        } else {
            // --- 閾値未満なら元の位置にポヨンと戻す ---
            currentSnackbar.style.transition = 'transform 0.25s ease-out';
            currentSnackbar.style.transform = 'translate(0px, 0px)';
        }
        isDragging = false;
        currentSnackbar = null;
    });
});
