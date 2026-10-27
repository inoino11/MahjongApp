using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace MahjongApp.E2E;

// 画面を実際に操作して確かめるシナリオ。Worker は本物のコードを代役のサーバーで動かす（AppFixture）
[Collection("app")]
public class ScenarioTests
{
    private readonly AppFixture _app;

    public ScenarioTests(AppFixture app) => _app = app;

    // --- 補助 ---

    private static async Task<IPage> OpenAsync(IBrowserContext context, string path = "")
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await WaitForAppAsync(page);
        return page;
    }

    private static async Task WaitForAppAsync(IPage page)
    {
        await page.WaitForSelectorAsync(".mud-main-content .mud-container", new() { State = WaitForSelectorState.Attached, Timeout = 30000 });
        await page.WaitForTimeoutAsync(800);
    }

    private static async Task ReloadAsync(IPage page)
    {
        await page.ReloadAsync();
        await WaitForAppAsync(page);
    }

    private static Task ClickAsync(IPage page, string text) =>
        page.GetByRole(AriaRole.Button, new() { Name = text, Exact = true }).First.ClickAsync();

    private static Task EvalAsync(IPage page, string script) => page.EvaluateAsync(script);

    private static async Task<JsonElement> HistoryAsync(IPage page) =>
        JsonDocument.Parse(await page.EvaluateAsync<string>("JSON.stringify(e2e.history())")).RootElement;

    private static double[] Doubles(JsonElement array) => array.EnumerateArray().Select(e => e.GetDouble()).ToArray();

    private static void AssertClose(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-6, $"[{i}] 期待 {expected[i]} / 実際 {actual[i]}");
    }

    private static async Task SetPlayersAsync(IPage page, params string[] names)
    {
        var all = names.Concat(Enumerable.Repeat("", 7 - names.Length));
        await EvalAsync(page, $"localStorage.setItem('currentPlayers', JSON.stringify({JsonSerializer.Serialize(all)}))");
    }

    private static Task<string> SnackbarTextAsync(IPage page, string contains) =>
        page.Locator(".mud-snackbar", new() { HasTextString = contains }).First.InnerTextAsync(new() { Timeout = 10000 });

    // --- 1・2. 記録と修正 ---

    [E2EFact]
    public async Task 素点を入れて記録し記録した半荘を修正できる()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await OpenAsync(device);
        await SetPlayersAsync(page, "あ", "い", "う", "え");
        await ReloadAsync(page);

        // 3人分を入れると、残りの1人は合計が合うように自動で入る（1000 - 452 - 281 - 175 = 92）
        await EvalAsync(page, "(async () => { await e2e.typeScore(0, 452); await e2e.typeScore(1, 281); await e2e.typeScore(2, 175); })()");
        var inputs = await page.EvaluateAsync<string[]>("e2e.inputs()");
        Assert.Equal("92", inputs[3]);

        // 四麻 25000持ち30000返し・ウマ10-20・レート50: +55.2 / +8.1 / -22.5 / -40.8
        await EvalAsync(page, "e2e.press('記録')");
        await page.WaitForTimeoutAsync(800);
        var game = (await HistoryAsync(page))[0];
        AssertClose(new[] { 55.2, 8.1, -22.5, -40.8 }, Doubles(game.GetProperty("Points")));
        Assert.Equal(new[] { 2760, 405, -1125, -2040 }, game.GetProperty("Yen").EnumerateArray().Select(e => e.GetInt32()));

        // 行をタップして修正を始め、キャンセルすると途中の入力（空欄）に戻る
        var row = page.Locator(".sync-table-scroll tbody tr").First.Locator("td").Nth(1);
        await row.ClickAsync();
        await page.GetByText("第1戦を修正中").WaitForAsync();
        Assert.Equal("452", (await page.EvaluateAsync<string[]>("e2e.inputs()"))[0]);
        await page.Locator(".mud-alert").GetByRole(AriaRole.Button, new() { Name = "キャンセル" }).ClickAsync();
        await page.WaitForTimeoutAsync(500);
        Assert.All((await page.EvaluateAsync<string[]>("e2e.inputs()")).Take(4), v => Assert.Equal("", v));

        // もう一度修正して保存すると、同じ半荘が置き換わる
        await row.ClickAsync();
        await page.GetByText("第1戦を修正中").WaitForAsync();
        await EvalAsync(page, "(async () => { await e2e.typeScore(0, 400); await e2e.typeScore(1, 333); await e2e.press('保存'); })()");
        await page.WaitForTimeoutAsync(800);
        var history = await HistoryAsync(page);
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(new int?[] { 400, 333, 175, 92 }, history[0].GetProperty("RawScores").EnumerateArray().Select(e => (int?)e.GetInt32()));
    }

    // --- 3. 抜け番と焼き鳥 ---

    [E2EFact]
    public async Task 五人で抜け番と焼き鳥のある半荘を記録できる()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await OpenAsync(device);
        await EvalAsync(page, """
            localStorage.setItem('rules', JSON.stringify({ Name: 'テスト', PlayerCount: 4, StartingPoints: 25000, ReturnPoints: 30000, Uma1: 10, Uma2: 20, Rate: 50, ChipRate: 100, IsYakiEnabled: true }));
            localStorage.setItem('activeParticipantCount', '5');
            """);
        await SetPlayersAsync(page, "あ", "い", "う", "え", "お");
        await ReloadAsync(page);

        // 2人目を抜け番、4人目を焼き鳥にする
        await page.Locator("button[title='参加中']").Nth(1).ClickAsync();
        await page.Locator("button[title='焼き鳥ではありません']").Nth(3).ClickAsync();
        await EvalAsync(page, "(async () => { await e2e.typeScore(0, 520); await e2e.typeScore(2, 300); await e2e.typeScore(3, 185); })()");
        Assert.Equal("-5", (await page.EvaluateAsync<string[]>("e2e.inputs()"))[4]);
        await EvalAsync(page, "e2e.press('記録')");
        await page.WaitForTimeoutAsync(800);

        // 基本: +62 / +10 / -21.5 / -50.5。焼き鳥の 10 を3人で 3.4 / 3.3 / 3.3（上位から）
        var game = (await HistoryAsync(page))[0];
        Assert.Equal(new int?[] { 520, null, 300, 185, -5 }, game.GetProperty("RawScores").EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? (int?)null : e.GetInt32()));
        AssertClose(new[] { 65.4, 0, 13.3, -31.5, -47.2 }, Doubles(game.GetProperty("Points")));
        Assert.Equal(new[] { 3270, 0, 665, -1575, -2360 }, game.GetProperty("Yen").EnumerateArray().Select(e => e.GetInt32()));
    }

    // --- 4. 成績に保存 ---

    [E2EFact]
    public async Task 成績に保存すると通算成績に反映される()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await OpenAsync(device);
        await EvalAsync(page, "e2e.seed()");
        await ReloadAsync(page);

        await ClickAsync(page, "成績に保存");
        await ClickAsync(page, "記録してリセット");
        await page.WaitForTimeoutAsync(1500);
        var counts = await page.EvaluateAsync<JsonElement>("e2e.dbCounts()");
        Assert.Equal(3, counts.GetProperty("sessions").GetInt32());
        Assert.Equal(6, counts.GetProperty("games").GetInt32());
        Assert.Equal(0, (await HistoryAsync(page)).GetArrayLength());

        await page.GotoAsync("dashboard");
        await WaitForAppAsync(page);
        // 長い名前は省略表示されるため、短い名前で確かめる
        await page.GetByText("はなこ").First.WaitForAsync();
    }

    // --- 5. ファイルでの統合 ---

    [E2EFact]
    public async Task 同じバックアップを二回統合してもルールが増えない()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await OpenAsync(device, "settings");
        // 初期の Mリーグルールは、順位ごとのウマが揃っていないまま保存されている
        await EvalAsync(page, """
            (() => {
              const P = (name, over) => Object.assign({ Name: name, PlayerCount: 4, StartingPoints: 25000, ReturnPoints: 30000, Uma1: 10, Uma2: 20, Rate: 50, ChipRate: 100, CustomUma4_1: 20, CustomUma4_2: 10, CustomUma4_3: -10, CustomUma4_4: -20, CustomUma3_1: 30, CustomUma3_2: -10, CustomUma3_3: -20 }, over);
              localStorage.setItem('rulePresets', JSON.stringify([P('一般的な四麻'), P('Mリーグルール', { Uma2: 30, Rate: 0, ChipRate: 0, TieBreaker: 1 }), P('いつもの', { Memo: '赤3枚' })]));
            })()
            """);
        string backup = await page.EvaluateAsync<string>("""
            (() => {
              const camel = (o) => Object.fromEntries(Object.entries(o).map(([k, v]) => [k[0].toLowerCase() + k.slice(1), v]));
              const presets = JSON.parse(localStorage.getItem('rulePresets')).map(camel);
              presets.push(camel(Object.assign({}, JSON.parse(localStorage.getItem('rulePresets'))[2], { Memo: '赤4枚' })));
              return JSON.stringify({ players: [], sessions: [], games: [], rulePresets: presets });
            })()
            """);

        async Task<string> ImportAsync()
        {
            await page.SetInputFilesAsync("#backup-upload", new FilePayload { Name = "backup.json", MimeType = "application/json", Buffer = System.Text.Encoding.UTF8.GetBytes(backup) });
            await ClickAsync(page, "はい、実行します");
            string message = await SnackbarTextAsync(page, "統合しました");
            await page.WaitForURLAsync(_app.BaseUrl, new() { Timeout = 15000 });
            await WaitForAppAsync(page);
            return message;
        }

        Assert.Contains("1件追加、3件は同じ内容のため省略", await ImportAsync());
        await page.GotoAsync("settings");
        await WaitForAppAsync(page);
        Assert.Contains("4件は同じ内容のため省略", await ImportAsync());
        Assert.Equal(4, await page.EvaluateAsync<int>("JSON.parse(localStorage.getItem('rulePresets')).length"));
    }

    // --- 6. クラウドバックアップ ---

    [E2EFact]
    public async Task クラウドバックアップを別の端末で復元すると元の端末は止まる()
    {
        await using var deviceA = await _app.NewDeviceAsync();
        var pageA = await OpenAsync(deviceA);
        await EvalAsync(pageA, "e2e.seed()");
        await pageA.GotoAsync("settings");
        await WaitForAppAsync(pageA);

        await ClickAsync(pageA, "クラウドバックアップを始める");
        var keyText = await pageA.Locator(".mud-dialog .mud-paper").Filter(new() { HasTextRegex = new Regex("^[A-Za-z0-9_-]{43}$") }).First.InnerTextAsync();
        string recoveryKey = keyText.Trim();
        await ClickAsync(pageA, "控えました");
        // パスキーへの保存の案内は、このシナリオでは断る
        await ClickAsync(pageA, "あとで");

        await using var deviceB = await _app.NewDeviceAsync();
        var pageB = await OpenAsync(deviceB, "settings");
        await ClickAsync(pageB, "クラウドから復元");
        await pageB.Locator(".mud-dialog input").First.FillAsync(recoveryKey);
        await ClickAsync(pageB, "バックアップを探す");
        await ClickAsync(pageB, "この時点に戻す");
        await ClickAsync(pageB, "はい、実行します");
        await pageB.WaitForURLAsync(_app.BaseUrl, new() { Timeout = 15000 });
        await WaitForAppAsync(pageB);
        var counts = await pageB.EvaluateAsync<JsonElement>("e2e.dbCounts()");
        Assert.Equal((5, 2, 4), (counts.GetProperty("players").GetInt32(), counts.GetProperty("sessions").GetInt32(), counts.GetProperty("games").GetInt32()));

        // 元の端末から送ろうとすると、引き継がれたことが分かって止まる
        await ClickAsync(pageA, "今すぐバックアップ");
        await pageA.GetByText("別の端末にバックアップが引き継がれた", new() { Exact = false }).First.WaitForAsync();
    }

    // --- 7. パスキー ---

    [E2EFact]
    public async Task パスキーに保存しサイトデータを消してもパスキーで復元できる()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await OpenAsync(device);
        var cdp = await device.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable");
        await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new { protocol = "ctap2", ctap2Version = "ctap2_1", transport = "internal", hasResidentKey = true, hasUserVerification = true, isUserVerified = true, hasPrf = true, automaticPresenceSimulation = true }
        });
        await EvalAsync(page, "e2e.seed()");
        await page.GotoAsync("settings");
        await WaitForAppAsync(page);

        await ClickAsync(page, "クラウドバックアップを始める");
        await ClickAsync(page, "控えました");
        await ClickAsync(page, "保存する");
        await page.GetByText("復元キーはパスキーにも保存済みです").WaitForAsync(new() { Timeout = 15000 });

        // サイトデータを消す（パスキーは認証器に残る）
        await EvalAsync(page, """
            (async () => {
              mahjongDb.db?.close(); mahjongDb.db = null;
              await new Promise(r => { const q = indexedDB.deleteDatabase('MahjongScoreDb'); q.onsuccess = q.onerror = q.onblocked = r; });
              localStorage.clear();
            })()
            """);
        await page.GotoAsync("settings");
        await WaitForAppAsync(page);
        Assert.Equal(0, (await page.EvaluateAsync<JsonElement>("e2e.dbCounts()")).GetProperty("games").GetInt32());

        await ClickAsync(page, "クラウドから復元");
        await ClickAsync(page, "パスキーで復元");
        await ClickAsync(page, "この時点に戻す");
        await ClickAsync(page, "はい、実行します");
        await page.WaitForURLAsync(_app.BaseUrl, new() { Timeout = 15000 });
        await WaitForAppAsync(page);
        Assert.Equal(4, (await page.EvaluateAsync<JsonElement>("e2e.dbCounts()")).GetProperty("games").GetInt32());
    }

    // --- 8. 観戦URL ---

    [E2EFact]
    public async Task 観戦URLを発行すると別の端末で見られ停止できる()
    {
        await using var host = await _app.NewDeviceAsync();
        var page = await OpenAsync(host);
        await EvalAsync(page, "e2e.seed()");
        await ReloadAsync(page);

        await ClickAsync(page, "観戦URL");
        await page.WaitForFunctionAsync("localStorage.getItem('liveShare') !== null", null, new() { Timeout = 15000 });
        var info = JsonDocument.Parse(await page.EvaluateAsync<string>("localStorage.getItem('liveShare')")).RootElement;
        string id = info.GetProperty("Id").GetString()!, key = info.GetProperty("Key").GetString()!;

        await using var viewer = await _app.NewDeviceAsync();
        var view = await viewer.NewPageAsync();
        await view.GotoAsync($"shared?live={id}#{key}");
        await view.GetByText("はなこ").First.WaitForAsync(new() { Timeout = 30000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "LIVE" }).ClickAsync();
        await page.GetByText("配信を停止").ClickAsync();
        await ClickAsync(page, "停止する");
        await page.WaitForTimeoutAsync(1000);
        var kv = await page.EvaluateAsync<JsonElement>("e2e.kv()");
        Assert.Equal("stopped", JsonDocument.Parse(kv.GetProperty("live:" + id).GetString()!).RootElement.GetProperty("status").GetString());
    }

    // --- 9. 問い合わせ ---

    [E2EFact]
    public async Task 問い合わせは暗号化されて届き運営者の秘密鍵で読める()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await OpenAsync(device, "privacy");
        // テスト用の鍵の組に差し替える（本番の秘密鍵はテストでも使わない）
        await EvalAsync(page, """
            (async () => {
              const pair = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
              feedbackCrypto.publicKey = feedbackCrypto.toB64(await crypto.subtle.exportKey('raw', pair.publicKey));
              window.__testPrivateKey = await crypto.subtle.exportKey('jwk', pair.privateKey);
            })()
            """);
        await page.Locator("#feedback textarea").FillAsync("記録ボタンが押せません");
        await page.Locator("#feedback .mud-input-control", new() { HasTextString = "連絡先" }).Locator("input").FillAsync("taro@example.com");
        await ClickAsync(page, "送信する");
        await page.GetByText("送信しました").WaitForAsync();

        var result = await page.EvaluateAsync<JsonElement>("""
            (async () => {
              const kv = await e2e.kv();
              const key = Object.keys(kv).filter(k => k.startsWith('feedback:')).sort().at(-1);
              const plain = JSON.parse(await feedbackCrypto.decrypt(JSON.parse(kv[key]).encrypted, window.__testPrivateKey));
              return { stored: kv[key], message: plain.message, contact: plain.contact, hasEnvironment: !!plain.environment };
            })()
            """);
        string stored = result.GetProperty("stored").GetString()!;
        Assert.DoesNotContain("taro", stored);
        Assert.DoesNotContain("記録ボタン", stored);
        Assert.Equal("記録ボタンが押せません", result.GetProperty("message").GetString());
        Assert.Equal("taro@example.com", result.GetProperty("contact").GetString());
        Assert.True(result.GetProperty("hasEnvironment").GetBoolean());
    }

    [E2EFact]
    public async Task 公開された読み取りページで貼り付けた秘密鍵で問い合わせを読める()
    {
        await using var device = await _app.NewDeviceAsync();
        var page = await device.NewPageAsync();
        await page.GotoAsync("tools/feedback-reader.html");
        await page.WaitForFunctionAsync("() => window.feedbackCrypto");
        // テスト用の鍵の組で、アプリと同じ形の KV の値を作る
        var keys = await page.EvaluateAsync<JsonElement>("""
            (async () => {
              const pair = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
              const pub = feedbackCrypto.toB64(await crypto.subtle.exportKey('raw', pair.publicKey));
              const plain = JSON.stringify({ category: '要望', message: '出先で読みたい', contact: 'taro@example.com' });
              return {
                jwk: JSON.stringify(await crypto.subtle.exportKey('jwk', pair.privateKey)),
                value: JSON.stringify({ encrypted: await feedbackCrypto.encrypt(plain, pub), receivedAt: '2026-10-27T00:00:00.000Z' }),
              };
            })()
            """);

        await page.Locator("#keyText").FillAsync(keys.GetProperty("jwk").GetString()!);
        await ClickAsync(page, "鍵を読み込む");
        await page.GetByText("秘密鍵を読み込みました").WaitForAsync();
        Assert.Equal("", await page.Locator("#keyText").InputValueAsync());

        await page.Locator("#input").FillAsync(keys.GetProperty("value").GetString()!);
        await ClickAsync(page, "読む");
        var item = page.Locator(".item");
        await item.GetByText("出先で読みたい").WaitForAsync();
        Assert.Contains("taro@example.com", await item.InnerTextAsync());
    }

    // --- 10. 説明のページと初回の案内 ---

    [E2EFact]
    public async Task 初回の案内は閉じたら出ず説明のページが開ける()
    {
        await using var device = await _app.NewDeviceAsync();
        var external = new List<string>();
        device.Request += (_, r) => { if (r.Url.Contains("fonts.googleapis.com") || r.Url.Contains("fonts.gstatic.com")) external.Add(r.Url); };
        var page = await OpenAsync(device);
        // 見出しの書体は自前で配り、Google Fonts とは通信しない
        Assert.True(await page.EvaluateAsync<bool>("document.fonts.load(\"bold 16px 'Kaisei Decol'\", '素点集計').then(f => f.length > 0)"));
        Assert.Empty(external);
        var welcome = page.GetByText("はじめての方へ");
        await welcome.WaitForAsync();
        await page.Locator(".mud-alert", new() { HasTextString = "はじめての方へ" }).Locator("button").First.ClickAsync();
        await ReloadAsync(page);
        Assert.Equal(0, await welcome.CountAsync());

        await page.GotoAsync("about");
        await page.GetByText("TaGAMΣ とは").WaitForAsync();
        await page.GotoAsync("privacy");
        await page.GetByText("運営者").First.WaitForAsync();
    }
}
