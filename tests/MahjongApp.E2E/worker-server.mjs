// 画面テスト用の Worker の代役。本物の Worker のコード（cloudflare/mahjong-share-worker.js）を、メモリ上の KV で動かす HTTP サーバー。
// 実行: node worker-server.mjs <Worker のパス> <ポート>
// 端末をまたぐテスト（別のブラウザで復元・観戦）のため、テストの間は同じ KV を使い続ける
import http from "node:http";
import { pathToFileURL } from "node:url";

const worker = (await import(pathToFileURL(process.argv[2]).href)).default;
const port = Number(process.argv[3] ?? 8787);
const store = new Map();
const env = {
  MAHJONG_KV: {
    async get(k) { return store.has(k) ? store.get(k) : null; },
    async put(k, v) { store.set(k, v); },
    async delete(k) { store.delete(k); },
  },
};

http.createServer(async (req, res) => {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const body = Buffer.concat(chunks);
  // テストから KV の中身を確かめるための覗き窓
  if (req.url === "/__kv") {
    res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
    res.end(JSON.stringify(Object.fromEntries(store)));
    return;
  }
  const request = new Request(`http://127.0.0.1:${port}${req.url}`, {
    method: req.method,
    headers: req.headers,
    body: ["GET", "HEAD"].includes(req.method) ? undefined : body,
  });
  const response = await worker.fetch(request, env, {});
  res.writeHead(response.status, Object.fromEntries(response.headers));
  res.end(Buffer.from(await response.arrayBuffer()));
}).listen(port, "127.0.0.1", () => console.log(`listening ${port}`));
