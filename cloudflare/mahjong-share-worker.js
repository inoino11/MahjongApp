// 麻雀集計ツールの共有用 Worker（Cloudflare ダッシュボードのエディタに全文を貼り付けてデプロイする）
// KV バインディング: MAHJONG_KV
//
// 入口
//   POST /            : 共有データを保存し、短縮IDを返す（従来の「URLシェア」）
//   GET  /{id}        : 共有データを返す
//   POST /live        : 観戦URLを発行し、IDと更新用の鍵を返す
//   PUT  /live/{id}   : 観戦データを更新する（Authorization: Bearer {更新用の鍵}）
//   GET  /live/{id}   : 観戦データを返す
//   PUT    /backup/{id}         : クラウドバックアップを保存する（Authorization: Bearer {トークン}）
//   GET    /backup/{id}         : クラウドバックアップの世代一覧を返す（同上）
//   GET    /backup/{id}/wrapped : パスキーで包んだ復元キーを返す（パスキーがないと開けないので認証なし）
//   DELETE /backup/{id}         : クラウドバックアップを削除する（同上）

// 保存期限（秒）。観戦URLは更新のたびに延長する
const KV_TTL = 60 * 60 * 24 * 90;

const LIVE_PREFIX = "live:";
const LIVE_STATUSES = ["live", "finished", "stopped", "discarded"];

// クラウドバックアップ。データは端末で暗号化済みで、ここでは中身を読めない
// 書き込みは1回で済むよう、世代も含めて1つの値にまとめる（KV の無料枠は書き込み1日1,000回のため）
const BACKUP_PREFIX = "backup:";
const BACKUP_TTL = 60 * 60 * 24 * 365;
const BACKUP_GENERATIONS = 3;
const BACKUP_MAX_BYTES = 5 * 1024 * 1024;

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
};

export default {
  async fetch(request, env, ctx) {
    // CORS のプリフライト
    if (request.method === "OPTIONS") {
      return new Response(null, { headers: CORS_HEADERS });
    }

    const url = new URL(request.url);

    if (url.pathname === "/live" || url.pathname.startsWith("/live/")) {
      return handleLive(request, env, url);
    }

    if (url.pathname.startsWith("/backup/")) {
      return handleBackup(request, env, url);
    }

    // POST /: 共有データを保存して短縮IDを返す
    if (request.method === "POST") {
      try {
        const body = await request.text();
        const shortId = generateShortId(6);

        await env.MAHJONG_KV.put(shortId, body, { expirationTtl: KV_TTL });

        return new Response(JSON.stringify({ id: shortId }), {
          headers: { "Content-Type": "application/json", ...CORS_HEADERS }
        });
      } catch (e) {
        return new Response("Error saving data", { status: 500, headers: CORS_HEADERS });
      }
    }

    // GET /{id}: 共有データを返す
    if (request.method === "GET") {
      const id = url.pathname.slice(1);
      // 観戦データ・バックアップ（鍵のハッシュを含む）はこの入口からは返さない
      if (!id || id.startsWith(LIVE_PREFIX) || id.startsWith(BACKUP_PREFIX)) {
        return new Response("Not Found", { status: 404, headers: CORS_HEADERS });
      }

      const data = await env.MAHJONG_KV.get(id);
      if (data === null) {
        return new Response("Not Found or Expired", { status: 404, headers: CORS_HEADERS });
      }

      return new Response(data, {
        headers: { "Content-Type": "application/json", ...CORS_HEADERS }
      });
    }

    return new Response("Method not allowed", { status: 405, headers: CORS_HEADERS });
  }
};

async function handleLive(request, env, url) {
  try {
    // POST /live: 発行。更新用の鍵はハッシュだけを保存する
    if (request.method === "POST" && url.pathname === "/live") {
      const body = await request.json();
      if (typeof body.encryptedData !== "string") return jsonResponse({ error: "bad request" }, 400);

      const id = randomString(10);
      const token = randomString(43);
      const record = {
        encryptedData: body.encryptedData,
        status: "live",
        updatedAt: new Date().toISOString(),
        tokenHash: await sha256(token),
      };
      await env.MAHJONG_KV.put(LIVE_PREFIX + id, JSON.stringify(record), { expirationTtl: KV_TTL });
      return jsonResponse({ id, token });
    }

    const id = url.pathname.slice("/live/".length);
    if (!id) return jsonResponse({ error: "not found" }, 404);
    const stored = await env.MAHJONG_KV.get(LIVE_PREFIX + id);
    if (stored === null) return jsonResponse({ error: "not found" }, 404);
    const record = JSON.parse(stored);

    // GET /live/{id}: 取得（鍵のハッシュは返さない）
    if (request.method === "GET") {
      return jsonResponse({ encryptedData: record.encryptedData, status: record.status, updatedAt: record.updatedAt });
    }

    // PUT /live/{id}: 更新（発行した端末だけが持つ鍵で認証する）
    if (request.method === "PUT") {
      const auth = request.headers.get("Authorization") || "";
      const token = auth.startsWith("Bearer ") ? auth.slice(7) : "";
      if (!token || !timingSafeEqual(await sha256(token), record.tokenHash)) {
        return jsonResponse({ error: "forbidden" }, 403);
      }
      const body = await request.json();
      const status = LIVE_STATUSES.includes(body.status) ? body.status : record.status;
      record.status = status;
      record.encryptedData = status === "discarded" ? "" : (typeof body.encryptedData === "string" ? body.encryptedData : record.encryptedData);
      record.updatedAt = new Date().toISOString();
      await env.MAHJONG_KV.put(LIVE_PREFIX + id, JSON.stringify(record), { expirationTtl: KV_TTL });
      return jsonResponse({ ok: true, updatedAt: record.updatedAt });
    }

    return jsonResponse({ error: "method not allowed" }, 405);
  } catch (e) {
    return jsonResponse({ error: "server error" }, 500);
  }
}

async function handleBackup(request, env, url) {
  try {
    const rest = url.pathname.slice("/backup/".length);
    const isWrapped = rest.endsWith("/wrapped");
    const id = isWrapped ? rest.slice(0, -"/wrapped".length) : rest;
    // ID は復元キーから導いた 32 桁の16進数
    if (!/^[0-9a-f]{32}$/.test(id)) return jsonResponse({ error: "not found" }, 404);
    const key = BACKUP_PREFIX + id;
    const stored = await env.MAHJONG_KV.get(key);
    const record = stored === null ? null : JSON.parse(stored);

    // GET /backup/{id}/wrapped: パスキー復元用。包んだ鍵だけを返す
    if (isWrapped) {
      if (request.method !== "GET" || !record || !record.wrappedKey) return jsonResponse({ error: "not found" }, 404);
      return jsonResponse({ wrappedKey: record.wrappedKey });
    }

    const auth = request.headers.get("Authorization") || "";
    const token = auth.startsWith("Bearer ") ? auth.slice(7) : "";
    if (!token) return jsonResponse({ error: "forbidden" }, 403);
    const tokenHash = await sha256(token);
    const authorized = record !== null && timingSafeEqual(tokenHash, record.tokenHash);

    // PUT /backup/{id}: 保存。初回はトークンのハッシュを登録する
    if (request.method === "PUT") {
      const text = await request.text();
      if (text.length > BACKUP_MAX_BYTES) return jsonResponse({ error: "too large" }, 413);
      const body = JSON.parse(text);
      if (typeof body.encryptedData !== "string" || typeof body.deviceId !== "string") return jsonResponse({ error: "bad request" }, 400);
      if (record !== null && !authorized) return jsonResponse({ error: "forbidden" }, 403);
      // 別の端末が復元して引き継いだ後は、元の端末からの上書きを断る
      if (record !== null && record.deviceId !== body.deviceId && body.force !== true) return jsonResponse({ error: "taken over" }, 409);

      const now = new Date().toISOString();
      const next = {
        tokenHash,
        deviceId: body.deviceId,
        updatedAt: now,
        wrappedKey: typeof body.wrappedKey === "string" ? body.wrappedKey : (record ? record.wrappedKey : undefined),
        generations: [{ at: now, data: body.encryptedData }, ...(record ? record.generations : [])].slice(0, BACKUP_GENERATIONS),
      };
      await env.MAHJONG_KV.put(key, JSON.stringify(next), { expirationTtl: BACKUP_TTL });
      return jsonResponse({ ok: true, updatedAt: now });
    }

    if (!authorized) return jsonResponse({ error: record === null ? "not found" : "forbidden" }, record === null ? 404 : 403);

    // GET /backup/{id}: 世代一覧（新しい順）
    if (request.method === "GET") {
      return jsonResponse({ updatedAt: record.updatedAt, hasWrappedKey: !!record.wrappedKey, generations: record.generations });
    }

    // DELETE /backup/{id}: 削除
    if (request.method === "DELETE") {
      await env.MAHJONG_KV.delete(key);
      return jsonResponse({ ok: true });
    }

    return jsonResponse({ error: "method not allowed" }, 405);
  } catch (e) {
    return jsonResponse({ error: "server error" }, 500);
  }
}

function jsonResponse(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store", ...CORS_HEADERS }
  });
}

// 推測されにくいランダム文字列（観戦URLのIDと更新用の鍵に使う）
function randomString(length) {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  let result = '';
  for (let i = 0; i < length; i++) {
    result += chars.charAt(bytes[i] % chars.length);
  }
  return result;
}

async function sha256(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

function timingSafeEqual(a, b) {
  if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

// 共有URLの短縮ID（従来の形式のまま）
function generateShortId(length = 6) {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let result = '';
  for (let i = 0; i < length; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return result;
}
