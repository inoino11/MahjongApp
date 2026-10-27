// 問い合わせの暗号化（wwwroot/js/feedback.js）のテスト。実行: node --test tests/js/feedback.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// ブラウザ用のスクリプトなので、window を用意して読み込む
globalThis.window = globalThis;
const source = readFileSync(fileURLToPath(new URL("../../wwwroot/js/feedback.js", import.meta.url)), "utf8");
new Function(source)();
const fc = globalThis.feedbackCrypto;

async function newKeyPair() {
  const pair = await crypto.subtle.generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"]);
  return {
    publicKey: fc.toB64(await crypto.subtle.exportKey("raw", pair.publicKey)),
    privateJwk: await crypto.subtle.exportKey("jwk", pair.privateKey),
  };
}

test("公開鍵で暗号化したものを、対応する秘密鍵で戻せる", async () => {
  const keys = await newKeyPair();
  const plain = JSON.stringify({ category: "不具合", message: "記録ボタンが押せない🀄", contact: "taro@example.com" });
  const envelope = await fc.encrypt(plain, keys.publicKey);
  assert.equal(await fc.decrypt(envelope, keys.privateJwk), plain);
});

test("暗号文に平文の内容が含まれない", async () => {
  const keys = await newKeyPair();
  const envelope = await fc.encrypt(JSON.stringify({ contact: "taro@example.com" }), keys.publicKey);
  assert.ok(!envelope.includes("taro"));
  assert.deepEqual(Object.keys(JSON.parse(envelope)).sort(), ["ct", "epk", "iv", "v"]);
});

test("同じ内容でも、毎回違う暗号文になる", async () => {
  const keys = await newKeyPair();
  assert.notEqual(await fc.encrypt("同じ", keys.publicKey), await fc.encrypt("同じ", keys.publicKey));
});

test("別の秘密鍵では戻せない", async () => {
  const keys = await newKeyPair();
  const other = await newKeyPair();
  const envelope = await fc.encrypt("秘密", keys.publicKey);
  await assert.rejects(fc.decrypt(envelope, other.privateJwk));
});

test("書き換えられた暗号文は戻せない", async () => {
  const keys = await newKeyPair();
  const envelope = JSON.parse(await fc.encrypt("秘密", keys.publicKey));
  const ct = fc.fromB64(envelope.ct);
  ct[0] ^= 1;
  envelope.ct = fc.toB64(ct);
  await assert.rejects(fc.decrypt(JSON.stringify(envelope), keys.privateJwk));
});
