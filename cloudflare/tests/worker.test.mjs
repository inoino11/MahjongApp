// Worker のテスト。KV はメモリ上の仮のものに差し替える。実行: node --test cloudflare/tests/
import { test } from "node:test";
import assert from "node:assert/strict";
import worker from "../mahjong-share-worker.js";

function createEnv() {
  const store = new Map();
  const puts = [];
  return {
    store,
    puts,
    MAHJONG_KV: {
      async get(k) { return store.has(k) ? store.get(k) : null; },
      async put(k, v, opts) { puts.push({ k, opts }); store.set(k, v); },
      async delete(k) { store.delete(k); },
    },
  };
}

async function call(env, method, path, { token, body } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (token) headers.Authorization = "Bearer " + token;
  const request = new Request("https://w.example" + path, {
    method,
    headers,
    body: body === undefined ? undefined : (typeof body === "string" ? body : JSON.stringify(body)),
  });
  const res = await worker.fetch(request, env, {});
  let json = null;
  try { json = await res.clone().json(); } catch { }
  return { status: res.status, json, headers: res.headers };
}

const id = "0123456789abcdef0123456789abcdef";

// --- クラウドバックアップ ---

test("バックアップ: トークンがなければ保存できない", async () => {
  const env = createEnv();
  const r = await call(env, "PUT", `/backup/${id}`, { body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal(r.status, 403);
});

test("バックアップ: 初回の保存でトークンを登録し、365日で期限を切る", async () => {
  const env = createEnv();
  const r = await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal(r.status, 200);
  assert.equal(env.puts.at(-1).opts.expirationTtl, 60 * 60 * 24 * 365);
});

test("バックアップ: 違うトークンでは保存も取得もできない", async () => {
  const env = createEnv();
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal((await call(env, "PUT", `/backup/${id}`, { token: "bad", body: { encryptedData: "B", deviceId: "d1" } })).status, 403);
  assert.equal((await call(env, "GET", `/backup/${id}`, { token: "bad" })).status, 403);
  assert.equal((await call(env, "DELETE", `/backup/${id}`, { token: "bad" })).status, 403);
});

test("バックアップ: 世代は新しい順に3つまで残し、トークンのハッシュは返さない", async () => {
  const env = createEnv();
  for (const d of ["A", "B", "C", "D"]) await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: d, deviceId: "d1" } });
  const r = await call(env, "GET", `/backup/${id}`, { token: "t1" });
  assert.equal(r.status, 200);
  assert.deepEqual(r.json.generations.map(g => g.data), ["D", "C", "B"]);
  assert.equal(r.json.hasWrappedKey, false);
  assert.ok(!JSON.stringify(r.json).includes("tokenHash"));
});

test("バックアップ: 別の端末からの上書きは断り、引き継ぎ（force）なら受け付ける", async () => {
  const env = createEnv();
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal((await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "B", deviceId: "d2" } })).status, 409);
  assert.equal((await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "B", deviceId: "d2", force: true } })).status, 200);
  // 引き継いだ後は、元の端末からの上書きを断る
  assert.equal((await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "C", deviceId: "d1" } })).status, 409);
});

test("バックアップ: パスキーで包んだ鍵は認証なしで取得でき、以後の保存でも残る", async () => {
  const env = createEnv();
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal((await call(env, "GET", `/backup/${id}/wrapped`)).status, 404);
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "B", deviceId: "d1", wrappedKey: "W" } });
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "C", deviceId: "d1" } });
  const r = await call(env, "GET", `/backup/${id}/wrapped`);
  assert.equal(r.status, 200);
  assert.equal(r.json.wrappedKey, "W");
});

test("バックアップ: 大きすぎる本文と欠けた本文を断る", async () => {
  const env = createEnv();
  const big = JSON.stringify({ encryptedData: "x".repeat(5 * 1024 * 1024 + 10), deviceId: "d1" });
  assert.equal((await call(env, "PUT", `/backup/${id}`, { token: "t1", body: big })).status, 413);
  assert.equal((await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { deviceId: "d1" } })).status, 400);
});

test("バックアップ: 削除したら取得できない", async () => {
  const env = createEnv();
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal((await call(env, "DELETE", `/backup/${id}`, { token: "t1" })).status, 200);
  assert.equal((await call(env, "GET", `/backup/${id}`, { token: "t1" })).status, 404);
});

test("バックアップ: 形式の違うIDや、共有用の入口からの取得は断る", async () => {
  const env = createEnv();
  await call(env, "PUT", `/backup/${id}`, { token: "t1", body: { encryptedData: "A", deviceId: "d1" } });
  assert.equal((await call(env, "GET", `/backup/not-a-valid-id`, { token: "t1" })).status, 404);
  assert.equal((await call(env, "GET", `/backup:${id}`)).status, 404);
});

// --- 観戦URL ---

test("観戦: 発行・更新・取得ができ、更新用の鍵が違えば断る", async () => {
  const env = createEnv();
  const created = await call(env, "POST", "/live", { body: { encryptedData: "L1" } });
  assert.equal(created.status, 200);
  const { id: liveId, token } = created.json;
  assert.equal((await call(env, "PUT", `/live/${liveId}`, { token: "bad", body: { encryptedData: "L2", status: "live" } })).status, 403);
  assert.equal((await call(env, "PUT", `/live/${liveId}`, { token, body: { encryptedData: "L2", status: "finished" } })).status, 200);
  const r = await call(env, "GET", `/live/${liveId}`);
  assert.equal(r.json.encryptedData, "L2");
  assert.equal(r.json.status, "finished");
  assert.ok(!JSON.stringify(r.json).includes("tokenHash"));
  assert.equal((await call(env, "GET", `/live:${liveId}`)).status, 404);
});

test("観戦: 存在しないIDは404", async () => {
  const env = createEnv();
  assert.equal((await call(env, "GET", "/live/nothing")).status, 404);
});

// --- 共有URL・共通 ---

test("共有: 保存した内容を短縮IDで取得できる", async () => {
  const env = createEnv();
  const created = await call(env, "POST", "/", { body: { encryptedData: "S" } });
  assert.equal(created.status, 200);
  const r = await call(env, "GET", `/${created.json.id}`);
  assert.equal(r.status, 200);
  assert.equal(r.json.encryptedData, "S");
});

test("CORS: プリフライトに応え、DELETE も許可する", async () => {
  const env = createEnv();
  const r = await call(env, "OPTIONS", `/backup/${id}`);
  assert.match(r.headers.get("Access-Control-Allow-Methods"), /DELETE/);
  assert.equal(r.headers.get("Access-Control-Allow-Origin"), "*");
});
