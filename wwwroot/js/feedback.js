// 問い合わせの暗号化。運営者の公開鍵で端末の中で暗号化し、サーバーには暗号文だけを送る（サーバーでは読めない）。
// 読むには運営者の秘密鍵が必要で、それは運営者の手元にだけある（tools/feedback-reader.html で読む）。
// 方式: 使い捨ての鍵ペアと運営者の公開鍵で ECDH（P-256）→ HKDF-SHA256 → AES-GCM（256bit）
window.feedbackCrypto = {
    // 運営者の公開鍵（P-256 の非圧縮形式 65 バイトを URL Safe Base64 にしたもの）。鍵を作り直したらここを差し替える
    publicKey: "",

    version: 1,
    hkdfSalt: new TextEncoder().encode("TaGAMΣ-feedback-v1"),

    toB64: function (bytes) {
        let binary = "";
        new Uint8Array(bytes).forEach(b => binary += String.fromCharCode(b));
        return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    },

    fromB64: function (text) {
        let b64 = text.replace(/-/g, "+").replace(/_/g, "/");
        while (b64.length % 4) b64 += "=";
        const binary = atob(b64);
        return Uint8Array.from(binary, c => c.charCodeAt(0));
    },

    // 共有の秘密から AES-GCM の鍵を導く（info に使い捨ての公開鍵を入れ、鍵ペアごとに別の鍵になるようにする）
    deriveAesKey: async function (sharedBits, ephemeralPublicRaw, usage) {
        const base = await crypto.subtle.importKey("raw", sharedBits, "HKDF", false, ["deriveKey"]);
        return crypto.subtle.deriveKey(
            { name: "HKDF", hash: "SHA-256", salt: this.hkdfSalt, info: ephemeralPublicRaw },
            base, { name: "AES-GCM", length: 256 }, false, [usage]);
    },

    // 平文を暗号化し、送信用の文字列（JSON）を返す。publicKeyB64 を省略すると運営者の公開鍵を使う
    encrypt: async function (plainText, publicKeyB64) {
        const recipient = await crypto.subtle.importKey("raw", this.fromB64(publicKeyB64 || this.publicKey), { name: "ECDH", namedCurve: "P-256" }, false, []);
        const ephemeral = await crypto.subtle.generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"]);
        const ephemeralPublicRaw = new Uint8Array(await crypto.subtle.exportKey("raw", ephemeral.publicKey));
        const shared = await crypto.subtle.deriveBits({ name: "ECDH", public: recipient }, ephemeral.privateKey, 256);
        const key = await this.deriveAesKey(shared, ephemeralPublicRaw, "encrypt");
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const cipher = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, new TextEncoder().encode(plainText));
        return JSON.stringify({ v: this.version, epk: this.toB64(ephemeralPublicRaw), iv: this.toB64(iv), ct: this.toB64(cipher) });
    },

    // 問い合わせに添える端末の種類（利用者が「一緒に送る」を選んだときだけ使う）
    getEnvironment: function () {
        const standalone = window.matchMedia && window.matchMedia("(display-mode: standalone)").matches;
        return navigator.userAgent + (standalone ? " / ホーム画面から起動" : "");
    },

    isReady: function () {
        return !!this.publicKey;
    },

    // 秘密鍵（JWK）で復号する。運営者の手元の読み取りページとテストで使う
    decrypt: async function (envelopeJson, privateJwk) {
        const envelope = JSON.parse(envelopeJson);
        if (envelope.v !== this.version) throw new Error("対応していない形式です（v=" + envelope.v + "）");
        const privateKey = await crypto.subtle.importKey("jwk", privateJwk, { name: "ECDH", namedCurve: "P-256" }, false, ["deriveBits"]);
        const ephemeralPublicRaw = this.fromB64(envelope.epk);
        const ephemeral = await crypto.subtle.importKey("raw", ephemeralPublicRaw, { name: "ECDH", namedCurve: "P-256" }, false, []);
        const shared = await crypto.subtle.deriveBits({ name: "ECDH", public: ephemeral }, privateKey, 256);
        const key = await this.deriveAesKey(shared, ephemeralPublicRaw, "decrypt");
        const plain = await crypto.subtle.decrypt({ name: "AES-GCM", iv: this.fromB64(envelope.iv) }, key, this.fromB64(envelope.ct));
        return new TextDecoder().decode(plain);
    }
};
