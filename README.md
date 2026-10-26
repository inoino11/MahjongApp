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
```

- `main` に push すると、テストが通った場合だけ GitHub Pages に反映されます（[.github/workflows/deploy.yml](.github/workflows/deploy.yml)）。
- 計算の中心は [Services/MahjongCalculatorService.cs](Services/MahjongCalculatorService.cs) です。テストの期待値は手計算で求め、計算の過程をコメントに残しています。
- Worker は Cloudflare のダッシュボードで管理しています。変更したら、[cloudflare/mahjong-share-worker.js](cloudflare/mahjong-share-worker.js) の全文をダッシュボードのエディタに貼り付けてデプロイしてください（KV バインディング名は `MAHJONG_KV`）。
