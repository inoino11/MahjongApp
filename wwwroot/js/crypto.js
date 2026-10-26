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
    },
    toUrlSafe: function (base64) {
        return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    },
    fromUrlSafe: function (urlSafe) {
        let base64 = urlSafe.replace(/-/g, '+').replace(/_/g, '/');
        while (base64.length % 4) { base64 += '='; }
        return this.base64ToBuffer(base64);
    },
    // 秘密の値から用途ごとの値を HKDF で導く（同じ秘密・同じ用途なら毎回同じ値になる）
    hkdfBits: async function (secretBytes, info, bits) {
        const base = await crypto.subtle.importKey("raw", secretBytes, "HKDF", false, ["deriveBits"]);
        return new Uint8Array(await crypto.subtle.deriveBits(
            { name: "HKDF", hash: "SHA-256", salt: new TextEncoder().encode("TaGAMΣ"), info: new TextEncoder().encode(info) },
            base, bits));
    },
    aesEncrypt: async function (keyBytes, plainText) {
        const key = await crypto.subtle.importKey("raw", keyBytes, "AES-GCM", false, ["encrypt"]);
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const cipherBuffer = await crypto.subtle.encrypt({ name: "AES-GCM", iv: iv }, key, new TextEncoder().encode(plainText));
        const combined = new Uint8Array(iv.length + cipherBuffer.byteLength);
        combined.set(iv, 0);
        combined.set(new Uint8Array(cipherBuffer), iv.length);
        return this.bufferToBase64(combined);
    },
    aesDecrypt: async function (keyBytes, combinedBase64) {
        const key = await crypto.subtle.importKey("raw", keyBytes, "AES-GCM", false, ["decrypt"]);
        const combined = this.base64ToBuffer(combinedBase64);
        const plainBuffer = await crypto.subtle.decrypt({ name: "AES-GCM", iv: combined.slice(0, 12) }, key, combined.slice(12));
        return new TextDecoder().decode(plainBuffer);
    },
    // クラウドバックアップ用。復元キー（32バイトの乱数）から保存場所のID・認証用トークン・暗号鍵を導く
    // 暗号鍵は JS の中だけで使い、C# 側には渡さない
    backup: {
        newRecoveryKey: function () {
            return blazorCrypto.toUrlSafe(blazorCrypto.bufferToBase64(crypto.getRandomValues(new Uint8Array(32))));
        },
        isValidRecoveryKey: function (recoveryKey) {
            try {
                return typeof recoveryKey === 'string' && /^[A-Za-z0-9_-]{43}$/.test(recoveryKey) && blazorCrypto.fromUrlSafe(recoveryKey).length === 32;
            } catch {
                return false;
            }
        },
        derive: async function (recoveryKey) {
            const secret = blazorCrypto.fromUrlSafe(recoveryKey);
            const idBytes = await blazorCrypto.hkdfBits(secret, "backup-id", 128);
            const tokenBytes = await blazorCrypto.hkdfBits(secret, "backup-token", 256);
            return {
                backupId: [...idBytes].map(b => b.toString(16).padStart(2, "0")).join(""),
                token: blazorCrypto.toUrlSafe(blazorCrypto.bufferToBase64(tokenBytes))
            };
        },
        encrypt: async function (recoveryKey, plainText) {
            const encKey = await blazorCrypto.hkdfBits(blazorCrypto.fromUrlSafe(recoveryKey), "backup-enc", 256);
            return blazorCrypto.aesEncrypt(encKey, plainText);
        },
        decrypt: async function (recoveryKey, combinedBase64) {
            const encKey = await blazorCrypto.hkdfBits(blazorCrypto.fromUrlSafe(recoveryKey), "backup-enc", 256);
            return blazorCrypto.aesDecrypt(encKey, combinedBase64);
        }
    }
};
