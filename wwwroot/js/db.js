window.mahjongDb = {
    db: null,
    _opening: null,
    // データベースを開く（無ければ作成する）。接続済み・接続中なら同じ結果を返す
    init: function () {
        if (this.db) return Promise.resolve(true);
        if (this._opening) return this._opening;
        this._opening = new Promise((resolve, reject) => {
            // 第2引数はスキーマのバージョン。ストアやインデックスを変えるときに上げる
            const request = indexedDB.open("MahjongScoreDb", 1);
            // 初回、またはバージョンが上がったときだけ実行される
            request.onupgradeneeded = (e) => {
                const db = e.target.result;
                // 名簿
                if (!db.objectStoreNames.contains('players')) {
                    db.createObjectStore('players', { keyPath: 'id' });
                }
                // セッション（1日分の対局のまとめ）
                if (!db.objectStoreNames.contains('sessions')) {
                    db.createObjectStore('sessions', { keyPath: 'sessionId' });
                }
                // 局ごとの成績
                if (!db.objectStoreNames.contains('games')) {
                    const gameStore = db.createObjectStore('games', { keyPath: 'id' });
                    gameStore.createIndex('by_session', 'sessionId', { unique: false });
                }
            };
            request.onsuccess = (e) => {
                const db = e.target.result;
                // iOSでは長時間バックグラウンドにすると接続が切られるため、次回の操作時に開き直す
                db.onclose = () => { if (this.db === db) this.db = null; };
                db.onversionchange = () => { db.close(); if (this.db === db) this.db = null; };
                this.db = db;
                resolve(true);
            };
            request.onerror = (e) => {
                console.error("IndexedDB Open Error:", e.target.error);
                reject(e.target.error);
            };
        }).finally(() => { this._opening = null; });
        return this._opening;
    },
    // 接続を確保してから処理を実行する。接続が閉じていた（InvalidStateError）場合は1回だけ開き直して再試行する
    // トランザクションは fn の中で同期的に作ること（await を挟むと自動コミットされるため）
    _run: async function (fn) {
        await this.init();
        try {
            return await fn(this.db);
        } catch (e) {
            if (!e || e.name !== 'InvalidStateError') throw e;
            this.db = null;
            await this.init();
            return await fn(this.db);
        }
    },
    // セッションの確定保存。名簿・セッション・各局を1つのトランザクションでまとめて書き込む
    saveSession: function (playersToUpdate, sessionRecord, gameRecords) {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['players', 'sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
            const playerStore = tx.objectStore('players');
            const sessionStore = tx.objectStore('sessions');
            const gameStore = tx.objectStore('games');
            playersToUpdate.forEach(player => {
                playerStore.put(player);
            });
            sessionStore.put(sessionRecord);
            gameRecords.forEach(game => {
                gameStore.put(game);
            });
        }));
    },
    // 全データの読み込み（成績集計・履歴・名簿で使う）
    getAllData: function () {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['players', 'sessions', 'games'], 'readonly');
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
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
        }));
    },
    // 名簿の追加・更新
    updatePlayer: function (player) {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['players'], 'readwrite');
            tx.objectStore('players').put(player);
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
        }));
    },
    // 名簿から削除する（対局履歴がない人だけが対象。履歴がある人は mergePlayers で「削除済み」に統合する）
    deletePlayer: function (playerId) {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['players'], 'readwrite');
            tx.objectStore('players').delete(playerId);
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
        }));
    },
    // sourceId の人を targetId の人に統合する（チップを合算し、全履歴のIDを付け替える）
    mergePlayers: function (sourceId, targetId) {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['players', 'sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
            const pStore = tx.objectStore('players');
            const sStore = tx.objectStore('sessions');
            const gStore = tx.objectStore('games');
            pStore.get(targetId).onsuccess = (e) => {
                let targetPlayer = e.target.result;
                pStore.get(sourceId).onsuccess = (e2) => {
                    let sourcePlayer = e2.target.result;
                    if (targetPlayer && sourcePlayer) {
                        targetPlayer.cumulativeChips = (targetPlayer.cumulativeChips || 0) + (sourcePlayer.cumulativeChips || 0);
                        pStore.put(targetPlayer);
                        pStore.delete(sourceId);
                    }
                };
            };
            gStore.getAll().onsuccess = (e) => {
                e.target.result.forEach(game => {
                    let changed = false;
                    const ids = game.playerIds || [];
                    for (let i = 0; i < ids.length; i++) {
                        if (ids[i] === sourceId) {
                            ids[i] = targetId;
                            changed = true;
                        }
                    }
                    if (changed) gStore.put(game);
                });
            };
            sStore.getAll().onsuccess = (e) => {
                e.target.result.forEach(session => {
                    let changed = false;
                    const pIds = session.participantPlayerIds || [];
                    for (let i = 0; i < pIds.length; i++) {
                        if (pIds[i] === sourceId) {
                            pIds[i] = targetId;
                            changed = true;
                        }
                    }
                    if (changed) sStore.put(session);
                });
            };
        }));
    },
    // 全データを消去する
    clearAllData: function () {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['players', 'sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
            tx.objectStore('players').clear();
            tx.objectStore('sessions').clear();
            tx.objectStore('games').clear();
        }));
    },
    // ファイルをダウンロードさせる
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
    // バックアップJSONで復元する（isMerge が false なら既存データを消してから入れる）
    restoreData: function (jsonData, isMerge) {
        return this._run(db => new Promise((resolve, reject) => {
            let tx = null;
            try {
                const data = JSON.parse(jsonData);
                tx = db.transaction(['players', 'sessions', 'games'], 'readwrite');
                tx.oncomplete = () => resolve(true);
                tx.onerror = (e) => reject(e.target.error);
                tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
                if (!isMerge) {
                    tx.objectStore('players').clear();
                    tx.objectStore('sessions').clear();
                    tx.objectStore('games').clear();
                }
                if (data.players) data.players.forEach(p => tx.objectStore('players').put(p));
                if (data.sessions) data.sessions.forEach(s => tx.objectStore('sessions').put(s));
                if (data.games) data.games.forEach(g => tx.objectStore('games').put(g));
            } catch (e) {
                // 接続切れでトランザクションを作れなかった場合は、_run に開き直してもらう
                if (!tx && e && e.name === 'InvalidStateError') return reject(e);
                // 途中で失敗した場合は clear() も含めて全て取り消す（中途半端な状態で確定させない）
                if (tx) {
                    try { tx.abort(); } catch { /* 既に終了している場合は無視 */ }
                }
                reject("JSON Parsing or DB Error: " + e.message);
            }
        }));
    },
    // セッションと、その局をまとめて削除する
    deleteSession: function(sessionId) {
        return this._run(db => new Promise((resolve, reject) => {
            const tx = db.transaction(['sessions', 'games'], 'readwrite');
            tx.oncomplete = () => resolve(true);
            tx.onerror = (e) => reject(e.target.error);
            tx.onabort = () => reject(tx.error || new Error("Transaction aborted"));
            tx.objectStore('sessions').delete(sessionId);
            // 古いデータはキーが SessionId（大文字始まり）のことがあるため両方見る
            const gameStore = tx.objectStore('games');
            gameStore.openCursor().onsuccess = (e) => {
                const cursor = e.target.result;
                if (cursor) {
                    const currentSessionId = cursor.value.SessionId || cursor.value.sessionId;
                    if (currentSessionId === sessionId) {
                        cursor.delete();
                    }
                    cursor.continue();
                }
            };
        }));
    }
};