// 画面テストの補助（すべてのページに読み込ませる）。Worker への通信を代役のサーバーへ向け、テンキー操作やデータの準備を手伝う
(() => {
  // --- Worker への通信を、代役のサーバーへ向ける（__MOCK_WORKER__ はテスト側で置き換える） ---
  const WORKER = "https://api.inoino11.com";
  const LOCAL = "__MOCK_WORKER__";
  const origFetch = window.fetch.bind(window);
  window.fetch = async (input, init) => {
    const url = typeof input === "string" ? input : (input && input.url) || String(input);
    if (url.startsWith(WORKER)) {
      const req = new Request(input, init);
      const body = ["GET", "HEAD"].includes(req.method) ? undefined : await req.text();
      return origFetch(LOCAL + url.slice(WORKER.length), { method: req.method, headers: req.headers, body });
    }
    return origFetch(input, init);
  };

  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  window.e2e = {
    sleep,
    mockWorker: LOCAL,

    // テンキーのキーを押す（アプリは pointerdown で反応する）
    press: async (label) => {
      const buttons = [...document.querySelectorAll(".custom-keypad-container button")];
      const btn = label === "-"
        ? buttons.find(b => b.classList.contains("op-key") && !/[＋+]/.test(b.textContent))
        : buttons.find(b => b.textContent.trim() === label);
      if (!btn) throw new Error("key not found: " + label);
      btn.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true, cancelable: true, button: 0, pointerType: "touch" }));
      await sleep(120);
    },

    // 素点欄 index を開き、value を入力する（C で消してから入れる）
    typeScore: async (index, value) => {
      document.getElementById("score-input-" + index).click();
      await sleep(300);
      await window.e2e.press("C");
      for (const k of String(value)) await window.e2e.press(k);
    },

    inputs: () => [...document.querySelectorAll(".score-input input")].map(e => e.value),

    history: () => JSON.parse(localStorage.getItem("ActiveHistory") || "[]"),

    dbCounts: async () => {
      const d = await mahjongDb.getAllData();
      return { players: d.players.length, sessions: d.sessions.length, games: d.games.length };
    },

    // 確定済みの成績（2セッション・4局）と、入力中の記録（3局）を入れる
    seed: async () => {
      const ids = ["p1", "p2", "p3", "p4", "p5"];
      const names = ["やまだたろう", "はなこ", "じろう", "さくら", "けんた"];
      const mk = (sid, n, pids, raws, pts) => ({ id: sid + "-g" + n, sessionId: sid, ruleName: "一般的な四麻", playerCount: 4, playerIds: pids, rawScores: raws, points: pts, yen: pts.map(p => Math.round(p * 50)), sinkPoints: [0, 0, 0, 0], yakiPoints: [0, 0, 0, 0], tobiPoints: [0, 0, 0, 0], coldPoints: [0, 0, 0, 0], timestamp: new Date(2026, 9, 10 + n).toISOString() });
      const games = [
        mk("s1", 1, ["p1", "p2", "p3", "p4"], [452, 281, 175, 92], [55.2, 8.1, -22.5, -40.8]),
        mk("s1", 2, ["p1", "p2", "p3", "p4"], [120, 380, 300, 200], [-38.0, 48.0, 10.0, -20.0]),
        mk("s1", 3, ["p1", "p2", "p3", "p4"], [-35, 512, 273, 250], [-63.5, 71.2, 7.3, -15.0]),
        mk("s2", 1, ["p1", "p2", "p5", "p4"], [300, 300, 250, 150], [40.0, 10.0, -15.0, -35.0]),
      ];
      const rules = { name: "一般的な四麻", playerCount: 4, startingPoints: 25000, returnPoints: 30000, uma1: 10, uma2: 20, rate: 50, chipRate: 100 };
      const sessions = [
        { sessionId: "s1", date: new Date(2026, 9, 12).toISOString(), rate: 50, chipRate: 100, participantPlayerIds: ["p1", "p2", "p3", "p4"], settledChips: [3, -2, 1, -2], rules },
        { sessionId: "s2", date: new Date(2026, 9, 18).toISOString(), rate: 50, chipRate: 100, participantPlayerIds: ["p1", "p2", "p5", "p4"], settledChips: [0, 5, -3, -2], rules },
      ];
      const players = ids.map((id, i) => ({ id, name: names[i], cumulativeChips: 0, createdAt: new Date(2026, 9, 1).toISOString() }));
      await mahjongDb.init();
      await mahjongDb.restoreData(JSON.stringify({ players, sessions, games }), false);
      const gr = (raws, pts) => ({ PlayerNames: names.slice(0, 4), RawScores: raws, Points: pts, Yen: pts.map(p => Math.round(p * 50)), Timestamp: new Date().toISOString(), YakiPlayers: [false, false, false, false], TobiPlayers: [false, false, false, false], TobiBonusRecipients: [false, false, false, false], SinkPoints: [0, 0, 0, 0], YakiPoints: [0, 0, 0, 0], TobiPoints: [0, 0, 0, 0], ColdPoints: [0, 0, 0, 0] });
      localStorage.setItem("ActiveHistory", JSON.stringify([gr([452, 281, 175, 92], [55.2, 8.1, -22.5, -40.8]), gr([120, 380, 300, 200], [-38, 48, 10, -20])]));
      localStorage.setItem("ActiveSessionChips", JSON.stringify({ "やまだたろう": 3, "はなこ": -2, "じろう": 1, "さくら": -2 }));
      localStorage.setItem("currentPlayers", JSON.stringify(["やまだたろう", "はなこ", "じろう", "さくら", "", "", ""]));
    },

    // 代役の Worker の KV の中身
    kv: async () => (await origFetch(LOCAL + "/__kv")).json(),
  };
})();
