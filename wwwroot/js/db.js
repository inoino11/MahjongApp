window.mahjongDb = {
    db: null,
    // データベースの初期化（無ければ作成、あれば開く）
    init: function () {
        return new Promise((resolve, reject) => {
            // 第2引数はデータベースのバージョン。将来構造を変える時はここを上げます
            const request = indexedDB.open("MahjongScoreDb", 1);
            // 初回起動時、またはバージョンが上がった時に実行されるスキーマ定義
            request.onupgradeneeded = (e) => {
                const db = e.target.result;
                // 1. プレイヤー名簿ストア
                if (!db.objectStoreNames.contains('players')) {
                    db.createObjectStore('players', { keyPath: 'id' });
                }
                // 2. セッション総括ストア
                if (!db.objectStoreNames.contains('sessions')) {
                    db.createObjectStore('sessions', { keyPath: 'sessionId' });
                }
                // 3. 局ごとの成績ストア
                if (!db.objectStoreNames.contains('games')) {
                    const gameStore = db.createObjectStore('games', { keyPath: 'id' });
                    // セッション単位で高速検索するためのインデックス
                    gameStore.createIndex('by_session', 'sessionId', { unique: false });
                }
            };
            request.onsuccess = (e) => {
                this.db = e.target.result;
                resolve(true);
            };
            request.onerror = (e) => {
                console.error("IndexedDB Open Error:", e.target.error);
                reject(e.target.error);
            };
        });
    },
    // セッションデータの安全な一括保存（トランザクション処理）
    saveSession: function (playersToUpdate, sessionRecord, gameRecords) {
        return new Promise((resolve, reject) => {
            if (!this.db) {
                reject("Database not initialized");
                return;
            }

            // 3つのテーブルにまたがるトランザクションを開始
            const tx = this.db.transaction(['players', 'sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            const playerStore = tx.objectStore('players');
            const sessionStore = tx.objectStore('sessions');
            const gameStore = tx.objectStore('games');
            // 1. プレイヤー名簿の追加・更新（既存データは上書き）
            playersToUpdate.forEach(player => {
                playerStore.put(player);
            });
            // 2. セッション記録の保存
            sessionStore.put(sessionRecord);
            // 3. 各局の成績の保存
            gameRecords.forEach(game => {
                gameStore.put(game);
            });
        });
    },
    // ダッシュボード用の全データ読み込み
    getAllData: function () {
        return new Promise((resolve, reject) => {
            if (!this.db) {
                reject("Database not initialized");
                return;
            }
            const tx = this.db.transaction(['players', 'sessions', 'games'], 'readonly');
            const result = { players: [], sessions: [], games: [] };
            const reqP = tx.objectStore('players').getAll();
            const reqS = tx.objectStore('sessions').getAll();
            const reqG = tx.objectStore('games').getAll();
            let pending = 3;
            const checkDone = () => {
                pending--;
                if (pending === 0) resolve(result);
            };
            reqP.onsuccess = () => { result.players = reqP.result; checkDone(); };
            reqS.onsuccess = () => { result.sessions = reqS.result; checkDone(); };
            reqG.onsuccess = () => { result.games = reqG.result; checkDone(); };
            tx.onerror = (e) => reject(e.target.error);
        });
    }
};