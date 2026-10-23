// 麻雀集計ツールの共有用 Worker（Cloudflare ダッシュボードのエディタに全文を貼り付けてデプロイする）
// KV バインディング: MAHJONG_KV
//
// 入口
//   POST /            : 共有データを保存し、短縮IDを返す（従来の「URLシェア」）
//   GET  /{id}        : 共有データを返す
//   POST /live        : 観戦URLを発行し、IDと更新用の鍵を返す
//   PUT  /live/{id}   : 観戦データを更新する（Authorization: Bearer {更新用の鍵}）
//   GET  /live/{id}   : 観戦データを返す

// 有効期限を90日（秒）に設定
const KV_TTL = 60 * 60 * 24 * 90;

const LIVE_PREFIX = "live:";
const LIVE_STATUSES = ["live", "finished", "stopped", "discarded"];

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
};

export default {
  async fetch(request, env, ctx) {
    // CORSのプリフライトリクエスト対応
    if (request.method === "OPTIONS") {
      return new Response(null, { headers: CORS_HEADERS });
    }

    const url = new URL(request.url);

    // 観戦URL（/live, /live/{id}）
    if (url.pathname === "/live" || url.pathname.startsWith("/live/")) {
      return handleLive(request, env, url);
    }

    // 【POST】データの保存と短縮IDの発行
    if (request.method === "POST") {
      try {
        const body = await request.text();
        const shortId = generateShortId(6);

        // KVにデータを保存（90日後に自動消去）
        await env.MAHJONG_KV.put(shortId, body, { expirationTtl: KV_TTL });

        return new Response(JSON.stringify({ id: shortId }), {
          headers: { "Content-Type": "application/json", ...CORS_HEADERS }
        });
      } catch (e) {
        return new Response("Error saving data", { status: 500, headers: CORS_HEADERS });
      }
    }

    // 【GET】短縮IDからデータを取得
    if (request.method === "GET") {
      const id = url.pathname.slice(1); // URLのパス部分（/xxxxxx）を取得
      // 観戦データ（更新用の鍵のハッシュを含む）はこの入口からは返さない
      if (!id || id.startsWith(LIVE_PREFIX)) {
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

// 観戦URLの処理
async function handleLive(request, env, url) {
  try {
    // 【POST /live】発行
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

    // 【GET /live/{id}】取得（鍵のハッシュは返さない）
    if (request.method === "GET") {
      return jsonResponse({ encryptedData: record.encryptedData, status: record.status, updatedAt: record.updatedAt });
    }

    // 【PUT /live/{id}】更新（発行した端末だけが持つ鍵で認証）
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
      // 更新のたびに期限を90日延長
      await env.MAHJONG_KV.put(LIVE_PREFIX + id, JSON.stringify(record), { expirationTtl: KV_TTL });
      return jsonResponse({ ok: true, updatedAt: record.updatedAt });
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

// 推測されにくいランダム文字列（観戦URLのIDと更新用の鍵）
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

// ランダムな英数字のIDを生成する関数
function generateShortId(length = 6) {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let result = '';
  for (let i = 0; i < length; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return result;
}
