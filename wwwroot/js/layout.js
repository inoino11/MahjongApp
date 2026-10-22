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

// 表とグラフの高さ合わせ
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
// ロード画面のフェードアウト
window.hideMahjongLoader = () => {
    const loader = document.getElementById('mahjong-loader');
    if (loader) {
        loader.style.opacity = '0';
        setTimeout(() => {
            loader.style.display = 'none';
        }, 800);
    }
};
