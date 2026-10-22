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
// 要素をテンキー上部の安全なエリアの中央にスクロールする関数
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

// ハンバーガーメニューなど、入力エリア外を触った時にテンキーを閉じる
['click', 'touchstart'].forEach(eventType => {
    document.addEventListener(eventType, (e) => {
        const keypad = document.querySelector('.custom-keypad-container.open');
        if (keypad) {
            // タップされた場所が「テンキーの中」か「スコア入力枠（追加・削除ボタン含む）の中」か判定
            const clickedInsideKeypad = e.target.closest('.custom-keypad-container');
            const clickedInsideInput = e.target.closest('#score-input-section');
            // どちらでもない場所（上部のAppバーやメニュー等）を触った場合
            if (!clickedInsideKeypad && !clickedInsideInput) {
                const btn = document.getElementById('hidden-close-keypad-btn');
                if (btn && !btn.disabled) {
                    btn.disabled = true; // タッチとクリックの連続発火による二重処理を防止
                    btn.click();
                    setTimeout(() => btn.disabled = false, 300);
                }
            }
        }
    }, { passive: true });
});
