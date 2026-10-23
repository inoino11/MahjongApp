// 2つの要素の横スクロール位置を連動させる（チップ集計と結果表）
window.syncScroll = (id1, id2) => {
    const el1 = document.getElementById(id1);
    const el2 = document.getElementById(id2);
    if (!el1 || !el2) return;
    if (el1.dataset.isSyncing === "true") return;
    el1.dataset.isSyncing = "true";
    let isSyncing1 = false;
    let isSyncing2 = false;
    el1.addEventListener('scroll', () => {
        if (!isSyncing1) {
            isSyncing2 = true;
            el2.scrollLeft = el1.scrollLeft;
        }
        isSyncing1 = false;
    }, { passive: true });
    el2.addEventListener('scroll', () => {
        if (!isSyncing2) {
            isSyncing1 = true;
            el1.scrollLeft = el2.scrollLeft;
        }
        isSyncing2 = false;
    }, { passive: true });
};

// 表とグラフを切り替えても高さが変わらないよう、高い方に合わせる
window.syncContainerHeight = () => {
    const container = document.getElementById('result-container');
    const chart = document.getElementById('chart-wrapper');
    const table = document.getElementById('table-wrapper');
    if (!container || !chart || !table) return;
    const chartHeight = chart.offsetHeight;
    const tableHeight = table.offsetHeight;
    const maxHeight = Math.max(chartHeight, tableHeight);
    container.style.minHeight = maxHeight + 'px';
};
// 起動時のローディング画面をフェードアウトさせる
window.hideMahjongLoader = () => {
    const loader = document.getElementById('mahjong-loader');
    if (loader) {
        loader.style.opacity = '0';
        setTimeout(() => {
            loader.style.display = 'none';
        }, 800);
    }
};
// 横スクロールの続きがあれば右端に影を出す（スクロールバーを非表示にしているため）
// wrapperId: .scroll-hint-wrap を付けた要素のID / selector: その中のスクロール要素（省略時は最初の子要素）
window.attachScrollHint = (wrapperId, selector) => {
    const wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    const el = selector ? wrap.querySelector(selector) : wrap.firstElementChild;
    if (!el || el.dataset.scrollHint === "true") return;
    el.dataset.scrollHint = "true";
    const update = () => {
        wrap.classList.toggle('can-scroll-right', el.scrollLeft + el.clientWidth < el.scrollWidth - 2);
    };
    el.addEventListener('scroll', update, { passive: true });
    if (window.ResizeObserver) {
        const observer = new ResizeObserver(update);
        observer.observe(el);
        if (el.firstElementChild) observer.observe(el.firstElementChild);
    }
    update();
};

// 画面が表示中か（観戦ページは、裏に回っている間は自動更新しない）
window.getVisibilityState = () => document.visibilityState;
