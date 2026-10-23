// 共有URL・観戦URL用の暗号化（AES-GCM）。鍵はURLの # 以降にだけ載せ、サーバーには送らない
window.blazorCrypto = {
    bufferToBase64: function (buffer) {
        let binary = '';
        const bytes = new Uint8Array(buffer);
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    },
    base64ToBuffer: function (base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    },
    encrypt: async function (plainText) {
        const encoder = new TextEncoder();
        const data = encoder.encode(plainText);

        // 128bit の鍵にして、URLに載せる鍵の長さを22文字に抑える
        const key = await crypto.subtle.generateKey(
            { name: "AES-GCM", length: 128 },
            true,
            ["encrypt", "decrypt"]
        );

        const iv = crypto.getRandomValues(new Uint8Array(12));
        const cipherBuffer = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv },
            key,
            data
        );

        const exportedKey = await crypto.subtle.exportKey("raw", key);

        // 送信形式は「IV(12バイト) + 暗号文」を Base64 にしたもの
        const combined = new Uint8Array(iv.length + cipherBuffer.byteLength);
        combined.set(iv, 0);
        combined.set(new Uint8Array(cipherBuffer), iv.length);

        const combinedBase64 = this.bufferToBase64(combined);

        // 鍵はURLに含めるため URL Safe Base64 にする
        const keyBase64 = this.bufferToBase64(exportedKey)
            .replace(/\+/g, '-')
            .replace(/\//g, '_')
            .replace(/=+$/, '');

        return { combinedBase64: combinedBase64, keyUrlSafeBase64: keyBase64 };
    },
    decrypt: async function (combinedBase64, keyUrlSafeBase64) {
        // URL Safe Base64 を通常の Base64 に戻す
        let keyBase64 = keyUrlSafeBase64.replace(/-/g, '+').replace(/_/g, '/');
        while (keyBase64.length % 4) { keyBase64 += '='; }

        const keyRaw = this.base64ToBuffer(keyBase64);
        const combined = this.base64ToBuffer(combinedBase64);

        const iv = combined.slice(0, 12);
        const cipherBytes = combined.slice(12);

        const key = await crypto.subtle.importKey(
            "raw",
            keyRaw,
            "AES-GCM",
            true,
            ["encrypt", "decrypt"]
        );

        const plainBuffer = await crypto.subtle.decrypt(
            { name: "AES-GCM", iv: iv },
            key,
            cipherBytes
        );

        const decoder = new TextDecoder();
        return decoder.decode(plainBuffer);
    },
    // 観戦URL用: 発行時の鍵で再暗号化する（IVは毎回新しく作る）
    encryptWithKey: async function (plainText, keyUrlSafeBase64) {
        let keyBase64 = keyUrlSafeBase64.replace(/-/g, '+').replace(/_/g, '/');
        while (keyBase64.length % 4) { keyBase64 += '='; }
        const key = await crypto.subtle.importKey("raw", this.base64ToBuffer(keyBase64), "AES-GCM", false, ["encrypt"]);

        const iv = crypto.getRandomValues(new Uint8Array(12));
        const cipherBuffer = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv },
            key,
            new TextEncoder().encode(plainText)
        );

        // encrypt と同じ「IV + 暗号文」の形式にする
        const combined = new Uint8Array(iv.length + cipherBuffer.byteLength);
        combined.set(iv, 0);
        combined.set(new Uint8Array(cipherBuffer), iv.length);
        return this.bufferToBase64(combined);
    }
};
