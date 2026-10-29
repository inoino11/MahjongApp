// クラウドバックアップの復元キーをパスキーで守る（WebAuthn の PRF 拡張）
// パスキーから取り出せる秘密の値で復元キーを包み（暗号化し）、包んだものだけをサーバーに置く。
// パスキーは iCloud キーチェーンや Google パスワードマネージャーで同期されるので、サイトのデータを消しても機種を変えても残る。
// ログインの仕組みとしては使わない（サーバーで署名を確かめない）ため、challenge は端末で作った乱数でよい
window.passkey = {
    // PRF に渡す固定の値。変えると登録済みのパスキーから同じ秘密を取り出せなくなるので変更しないこと
    salt: new TextEncoder().encode("TaGAMΣ-backup-wrap-v1"),

    // inoino11.com とそのサブドメインでは、パスキーをドメイン全体に結び付ける（アプリを別のサブドメインに移しても使えるように）。
    // それ以外（localhost など）では指定せず、開いている住所に結び付ける
    rpId: function () {
        const host = location.hostname;
        return host === "inoino11.com" || host.endsWith(".inoino11.com") ? "inoino11.com" : undefined;
    },

    isSupported: function () {
        return !!(window.PublicKeyCredential && navigator.credentials && navigator.credentials.create);
    },

    hexToBytes: function (hex) {
        return new Uint8Array(hex.match(/../g).map(h => parseInt(h, 16)));
    },

    bytesToHex: function (bytes) {
        return [...new Uint8Array(bytes)].map(b => b.toString(16).padStart(2, "0")).join("");
    },

    wrapKeyFrom: function (prfOutput) {
        return blazorCrypto.hkdfBits(new Uint8Array(prfOutput), "backup-wrap", 256);
    },

    // 戻り値: { status: OK / UNSUPPORTED / CANCELED / ERROR, wrappedKey?, message? }
    // user.id に保存場所のIDを入れておき、復元時にパスキーを選ぶだけで場所が分かるようにする
    register: async function (backupId, recoveryKey) {
        try {
            const cred = await navigator.credentials.create({
                publicKey: {
                    rp: { name: "TaGAMΣ", id: this.rpId() },
                    user: { id: this.hexToBytes(backupId), name: "TaGAMΣ バックアップ", displayName: "TaGAMΣ バックアップ" },
                    challenge: crypto.getRandomValues(new Uint8Array(32)),
                    pubKeyCredParams: [{ type: "public-key", alg: -7 }, { type: "public-key", alg: -257 }],
                    authenticatorSelection: { residentKey: "required", requireResidentKey: true, userVerification: "required" },
                    extensions: { prf: { eval: { first: this.salt } } }
                }
            });
            const ext = cred.getClientExtensionResults();
            if (!ext.prf || ext.prf.enabled === false) return { status: "UNSUPPORTED" };
            let prfOutput = ext.prf.results && ext.prf.results.first;
            // 作成時には値を返さない端末（iOS など）があるため、その場合は続けて一度認証して取り出す
            if (!prfOutput) {
                const assertion = await navigator.credentials.get({
                    publicKey: {
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        rpId: this.rpId(),
                        allowCredentials: [{ type: "public-key", id: cred.rawId }],
                        userVerification: "required",
                        extensions: { prf: { eval: { first: this.salt } } }
                    }
                });
                const prf = assertion.getClientExtensionResults().prf;
                prfOutput = prf && prf.results && prf.results.first;
                if (!prfOutput) return { status: "UNSUPPORTED" };
            }
            const wrappedKey = await blazorCrypto.aesEncrypt(await this.wrapKeyFrom(prfOutput), recoveryKey);
            return { status: "OK", wrappedKey: wrappedKey };
        } catch (e) {
            if (e && e.name === "NotAllowedError") return { status: "CANCELED" };
            return { status: "ERROR", message: e && e.message ? e.message : String(e) };
        }
    },

    // 戻り値: { status: OK / UNSUPPORTED / NOT_FOUND / CANCELED / ERROR, recoveryKey?, message? }
    unlock: async function (workerUrl) {
        try {
            const assertion = await navigator.credentials.get({
                publicKey: {
                    challenge: crypto.getRandomValues(new Uint8Array(32)),
                    rpId: this.rpId(),
                    userVerification: "required",
                    extensions: { prf: { eval: { first: this.salt } } }
                }
            });
            const prf = assertion.getClientExtensionResults().prf;
            const prfOutput = prf && prf.results && prf.results.first;
            if (!prfOutput) return { status: "UNSUPPORTED" };
            const userHandle = assertion.response.userHandle;
            if (!userHandle || userHandle.byteLength !== 16) return { status: "NOT_FOUND" };
            const backupId = this.bytesToHex(userHandle);
            const res = await fetch(`${workerUrl}/backup/${backupId}/wrapped`);
            if (res.status === 404) return { status: "NOT_FOUND" };
            if (!res.ok) return { status: "ERROR", message: `HTTP ${res.status}` };
            const body = await res.json();
            const recoveryKey = await blazorCrypto.aesDecrypt(await this.wrapKeyFrom(prfOutput), body.wrappedKey);
            return { status: "OK", recoveryKey: recoveryKey };
        } catch (e) {
            if (e && e.name === "NotAllowedError") return { status: "CANCELED" };
            return { status: "ERROR", message: e && e.message ? e.message : String(e) };
        }
    }
};
