// 入力欄がテンキーに隠れないよう、テンキーのすぐ上までスクロールする
window.scrollToInput = (elementId) => {
    const el = document.getElementById(elementId);
    const keypad = document.querySelector('.custom-keypad-container');
    if (el) {
        const rect = el.getBoundingClientRect();
        const keypadHeight = keypad ? keypad.offsetHeight : 320;
        const buttonHeight = 60;
        const keypadTop = window.innerHeight - keypadHeight;
        const targetY = window.scrollY + rect.bottom - buttonHeight - keypadTop;
        window.scrollTo({
            top: targetY,
            behavior: 'smooth'
        });
    }
};
// 要素を、テンキーより上の見える範囲の中央にスクロールする
window.scrollToCenter = (elementId) => {
    const el = document.getElementById(elementId);
    const keypad = document.querySelector('.custom-keypad-container');
    if (el) {
        const rect = el.getBoundingClientRect();
        const keypadHeight = keypad ? keypad.offsetHeight : 320;
        const safeAreaHeight = window.innerHeight - keypadHeight;
        const elementCenterY = rect.top + (rect.height / 2);
        const safeAreaCenterY = safeAreaHeight / 2;
        const targetY = window.scrollY + elementCenterY - safeAreaCenterY;
        window.scrollTo({
            top: targetY,
            behavior: 'smooth'
        });
    }
};

// メニューなど入力エリアの外を触ったらテンキーを閉じる（Blazor 側の閉じる処理は隠しボタン経由で呼ぶ）
['click', 'touchstart'].forEach(eventType => {
    document.addEventListener(eventType, (e) => {
        const keypad = document.querySelector('.custom-keypad-container.open');
        if (keypad) {
            const clickedInsideKeypad = e.target.closest('.custom-keypad-container');
            const clickedInsideInput = e.target.closest('#score-input-section');
            if (!clickedInsideKeypad && !clickedInsideInput) {
                const btn = document.getElementById('hidden-close-keypad-btn');
                if (btn && !btn.disabled) {
                    btn.disabled = true; // touchstart と click が続けて来ても1回だけ処理する
                    btn.click();
                    setTimeout(() => btn.disabled = false, 300);
                }
            }
        }
    }, { passive: true });
});
