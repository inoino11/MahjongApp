// トースト通知をスワイプで消せるようにする
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
            // 消すときのCSSで狙えるようIDを付ける
            if (!currentSnackbar.id) {
                currentSnackbar.id = 'toast-' + Date.now() + '-' + Math.floor(Math.random() * 1000);
            }
            currentSnackbar.style.transition = 'none';
        }
    }, { passive: true });
    document.addEventListener('touchmove', (e) => {
        if (!isDragging || !currentSnackbar) return;
        const deltaX = e.touches[0].clientX - startX;
        const deltaY = e.touches[0].clientY - startY;
        currentSnackbar.style.transform = `translate(${deltaX}px, ${deltaY}px)`;
    }, { passive: true });
    document.addEventListener('touchend', (e) => {
        if (!isDragging || !currentSnackbar) return;
        const deltaX = e.changedTouches[0].clientX - startX;
        const deltaY = e.changedTouches[0].clientY - startY;
        const threshold = 50; // これ以上動かしたら消す（px）
        if (Math.abs(deltaX) > threshold || Math.abs(deltaY) > threshold) {
            let escapeX = 0;
            let escapeY = 0;
            // 大きく動かした方向へ飛ばして消す
            if (Math.abs(deltaX) > Math.abs(deltaY)) {
                escapeX = deltaX > 0 ? 200 : -200;
            } else {
                escapeY = deltaY > 0 ? 200 : -200;
            }
            const toastId = currentSnackbar.id;
            // 要素の style を直接変えると Blazor の再描画で戻ってしまうため、CSS を差し込んで消す
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
            // トーストが Blazor 側で破棄された頃に CSS を片付ける
            setTimeout(() => {
                styleEl.remove();
            }, 3000);
        } else {
            // 動かした量が少なければ元の位置に戻す
            currentSnackbar.style.transition = 'transform 0.25s ease-out';
            currentSnackbar.style.transform = 'translate(0px, 0px)';
        }
        isDragging = false;
        currentSnackbar = null;
    });
});
