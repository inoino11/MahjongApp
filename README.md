# TaGAMΣ - 麻雀集計 -

麻雀の点数計算と成績管理を、スマホひとつで済ませるための Web アプリです。
半荘ごとに素点を入れるだけで、ウマ・オカから焼き鳥・トビ賞まで自動で計算し、その日の精算と通算成績まで残せます。

**https://inoino11.github.io/MahjongApp/**

## できること

- **素点を入れるだけで自動計算**: ウマ・オカ、同点時の扱い（上家取り／ポイント折半）、沈みウマ・焼き鳥・トビ賞・コールド賞。最後の 1 人の素点は自動で入ります。
- **5〜7 人の卓、三人麻雀に対応**: 抜け番を切り替えながら記録を続けられます。
- **記録した半荘の修正**: 結果表の行をタップすると、入力欄に読み込んで直せます。
- **場代の精算**: 同額・総額トップ・各戦トップ・対戦数比の 4 つの払い方。チップも合わせて計算します。
- **成績の振り返り**: 対局履歴、通算の順位率・平均順位・収支、同卓相手との相性。
- **結果の共有**: 画像、閲覧用 URL、記録のたびに更新される観戦用 URL。
- **バックアップ**: ファイルへの保存と、端末で暗号化したクラウドバックアップ（パスキーまたは復元キーで復元）。

## なぜ TaGAMΣ なのか

- **インストールも登録も不要**: URL を開けばすぐ使えます。ホーム画面に追加すれば PWA としてアプリのように起動できます。
- **オフラインでも動く**: 一度開けば、電波の届かない場所でも集計できます。
- **記録は自分の端末の中**: 成績はブラウザの IndexedDB に保存します。共有データとクラウドバックアップは端末で AES-GCM により暗号化してから送り、鍵はサーバーに渡しません（共有 URL では `#` 以降にだけ含めます）。
- **無料・広告なし**。

データの扱いはアプリ内の「プライバシー」ページ（/privacy）にまとめています。不具合報告や要望も、そのページのフォームから送れます（運営者の公開鍵で暗号化して送ります）。

## 技術構成

| 部分 | 使っているもの |
|---|---|
| 画面 | Blazor WebAssembly（.NET 10）、MudBlazor |
| 保存 | IndexedDB（成績）、localStorage（入力中の状態・設定） |
| 配信 | GitHub Pages（GitHub Actions で自動デプロイ） |
| 共有・観戦・クラウドバックアップ | Cloudflare Workers + KV（[cloudflare/mahjong-share-worker.js](cloudflare/mahjong-share-worker.js)） |

## 開発

```sh
dotnet run                          # http://localhost:5260 で起動
dotnet test tests/MahjongApp.Tests  # 計算・精算・集計のテスト
node --test tests/js/feedback.test.mjs  # 問い合わせの暗号化のテスト

# 画面テスト（Playwright）: 公開物を作ってから、その wwwroot を指定して動かす
dotnet publish MahjongApp.csproj -c Release -o release
E2E_WWWROOT=release/wwwroot E2E_BROWSER_CHANNEL=msedge dotnet test tests/MahjongApp.E2E
```

- 画面テストは、記録・修正・抜け番・成績の保存・統合・クラウドバックアップ・パスキー・観戦・問い合わせ・説明のページを、実際のブラウザで操作して確かめます。Worker は本物のコードを Node で動かした代役に向けます（Node は `E2E_NODE` で指定でき、なければ PATH の `node`）。
- `E2E_BROWSER_CHANNEL` を省くと Playwright の Chromium を使います（初回は `pwsh tests/MahjongApp.E2E/bin/Debug/net10.0/playwright.ps1 install chromium` が必要）。

- `main` に push すると、テストが通った場合だけ GitHub Pages に反映されます（[.github/workflows/deploy.yml](.github/workflows/deploy.yml)）。
- 計算の中心は [Services/MahjongCalculatorService.cs](Services/MahjongCalculatorService.cs) です。テストの期待値は手計算で求め、計算の過程をコメントに残しています。
- Worker（[cloudflare/mahjong-share-worker.js](cloudflare/mahjong-share-worker.js)）は、`cloudflare/` を変えて `main` に push すると、テストが通った場合だけ自動でデプロイされます（[.github/workflows/deploy-worker.yml](.github/workflows/deploy-worker.yml)、設定は [cloudflare/wrangler.toml](cloudflare/wrangler.toml)）。ダッシュボードで直接編集すると、次のデプロイで上書きされます。
  - テストの実行: `cd cloudflare && node --test`
  - デプロイには、リポジトリの Secrets に `CLOUDFLARE_API_TOKEN` と `CLOUDFLARE_ACCOUNT_ID` が必要です。

## 運用

### 問い合わせを読む
アプリの「プライバシー」ページのフォームから届いた問い合わせは、端末で運営者の公開鍵（[wwwroot/js/feedback.js](wwwroot/js/feedback.js)）により暗号化され、KV に `feedback:<受信日時>:<乱数>` のキーで 180 日保存されます。Cloudflare 側では中身を読めません。

1. Cloudflare のダッシュボード → Storage & Databases → KV → 名前空間を開き、キーを `feedback:` で絞り込んで値をコピーします。
2. 手元で [tools/feedback-reader.html](tools/feedback-reader.html) をブラウザで開き、秘密鍵のファイルを選んで値を貼り付けます。

鍵の組は [tools/feedback-keys.html](tools/feedback-keys.html) で作ります。秘密鍵はリポジトリや Cloudflare には置かず、運営者の手元とパスワード管理アプリにだけ保管します。鍵を作り直したら、`feedback.js` の `publicKey` を差し替えます（それまでの問い合わせは古い秘密鍵でしか読めません）。

### 毎日の確認と通知
[.github/workflows/monitor.yml](.github/workflows/monitor.yml) が毎日 8:30（日本時間）に、KV の書き込み回数と問い合わせの新着を調べます。次のどちらかに当たると「運用通知」の Issue に書き込み、GitHub からメールが届きます（件数だけで、問い合わせの内容は載せません）。

- KV の書き込みが 1 日 700 回（無料枠の 70%）以上
- 過去 24 時間に問い合わせが届いた

Secrets の `CLOUDFLARE_MONITOR_TOKEN`（権限: Account Analytics: Read、Workers KV Storage: Read）が必要です。未設定なら確認を飛ばします。

### 有料プランに切り替える目安
- 無料枠: KV の書き込みは 1 日 1,000 回（UTC 0 時 = 日本時間 9 時に戻る）。超えると、その日は共有・観戦・バックアップ・問い合わせの保存が失敗し、アプリに「日本時間 9 時に回復します」と表示されます。
- Workers Paid: 月 5 ドルで、KV の書き込みが月 100 万回まで含まれます（超えた分は 100 万回あたり 5 ドル）。
- 次のどちらかに当たったら切り替えます。切り替えはダッシュボードでプランを変えるだけで、コードの変更は要りません。
  - 1 週間のうち 3 日以上、書き込みが 700 回を超えた
  - 上限に 1 回でも当たった

料金は変わることがあるので、切り替える前に [KV の料金](https://developers.cloudflare.com/kv/platform/pricing/) を確認してください。