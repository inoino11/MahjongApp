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
    },
    // プレイヤー名の変更やチップの直接修正
    updatePlayer: function (player) {
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction(['players'], 'readwrite');
            tx.objectStore('players').put(player);
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
        });
    },
    // プレイヤーの削除（※対局履歴がない場合のみC#側から呼ばれる想定）
    deletePlayer: function (playerId) {
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction(['players'], 'readwrite');
            tx.objectStore('players').delete(playerId);
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
        });
    },
    // プレイヤーの統合
    mergePlayers: function (sourceId, targetId) {
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction(['players', 'sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            const pStore = tx.objectStore('players');
            const sStore = tx.objectStore('sessions');
            const gStore = tx.objectStore('games');
            // チップの合算と元データの削除
            pStore.get(targetId).onsuccess = (e) => {
                let targetPlayer = e.target.result;
                pStore.get(sourceId).onsuccess = (e2) => {
                    let sourcePlayer = e2.target.result;
                    if (targetPlayer && sourcePlayer) {
                        targetPlayer.cumulativeChips += sourcePlayer.cumulativeChips;
                        pStore.put(targetPlayer);
                        pStore.delete(sourceId);
                    }
                };
            };
            // レコードIDすげ替え
            gStore.getAll().onsuccess = (e) => {
                e.target.result.forEach(game => {
                    let changed = false;
                    for (let i = 0; i < game.playerIds.length; i++) {
                        if (game.playerIds[i] === sourceId) {
                            game.playerIds[i] = targetId;
                            changed = true;
                        }
                    }
                    if (changed) gStore.put(game);
                });
            };
            // セッションIDすげ替え
            sStore.getAll().onsuccess = (e) => {
                e.target.result.forEach(session => {
                    let changed = false;
                    for (let i = 0; i < session.participantPlayerIds.length; i++) {
                        if (session.participantPlayerIds[i] === sourceId) {
                            session.participantPlayerIds[i] = targetId;
                            changed = true;
                        }
                    }
                    if (changed) sStore.put(session);
                });
            };
        });
    },
    // 全データの完全初期化
    clearAllData: function () {
        return new Promise((resolve, reject) => {
            if (!this.db) {
                reject("Database not initialized");
                return;
            }
            const tx = this.db.transaction(['players', 'sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.objectStore('players').clear();
            tx.objectStore('sessions').clear();
            tx.objectStore('games').clear();
        });
    },
    // JSONファイルをブラウザからダウンロードさせる処理
    downloadFile: function (filename, contentType, content) {
        const file = new File([content], filename, { type: contentType });
        const exportUrl = URL.createObjectURL(file);
        const a = document.createElement("a");
        document.body.appendChild(a);
        a.href = exportUrl;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(exportUrl);
        a.remove();
    },
    // 読み込んだJSONデータでデータベースをまるごと復元または統合する処理
    restoreData: function (jsonData, isMerge) {
        return new Promise((resolve, reject) => {
            if (!this.db) return reject("Database not initialized");
            try {
                const data = JSON.parse(jsonData);
                const tx = this.db.transaction(['players', 'sessions', 'games'], 'readwrite');
                tx.oncomplete = () => resolve(true);
                tx.onerror = (e) => reject(e.target.error);
                if (!isMerge) {
                    tx.objectStore('players').clear();
                    tx.objectStore('sessions').clear();
                    tx.objectStore('games').clear();
                }
                if (data.players) data.players.forEach(p => tx.objectStore('players').put(p));
                if (data.sessions) data.sessions.forEach(s => tx.objectStore('sessions').put(s));
                if (data.games) data.games.forEach(g => tx.objectStore('games').put(g));
            } catch (e) {
                reject("JSON Parsing or DB Error: " + e.message);
            }
        });
    }
};