using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;

namespace MahjongApp.E2E;

/// <summary>
/// 画面テストの土台。公開済みのアプリを配るサーバー、Worker の代役、ブラウザを、テスト全体で1つずつ用意する。
/// 公開済みのアプリの場所は環境変数 E2E_WWWROOT で渡す（未設定ならテストを飛ばす）
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public static string? WwwRoot => Environment.GetEnvironmentVariable("E2E_WWWROOT");
    // アプリを配るパス。CI では公開用に base が /MahjongApp/ に書き換えられた公開物を試すので、本番と同じパスで配る
    private static string BasePath
    {
        get
        {
            var trimmed = (Environment.GetEnvironmentVariable("E2E_BASE_PATH") ?? "").Trim('/');
            return trimmed.Length == 0 ? "/" : $"/{trimmed}/";
        }
    }

    private WebApplication? _app;
    private Process? _worker;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    // WebAuthn（パスキー）は IP アドレスのページでは動かないため、localhost で開く
    public string BaseUrl { get; private set; } = "";
    public string WorkerUrl { get; private set; } = "";
    private string _helpers = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(WwwRoot)) return;
        BaseUrl = $"http://localhost:{await StartAppServerAsync(WwwRoot)}{BasePath}";
        WorkerUrl = $"http://127.0.0.1:{await StartWorkerAsync()}";
        _helpers = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "e2e-helpers.js")).Replace("__MOCK_WORKER__", WorkerUrl);

        _playwright = await Playwright.CreateAsync();
        // 手元では入っている Edge を使い（E2E_BROWSER_CHANNEL=msedge）、CI では Playwright の Chromium を使う
        var channel = Environment.GetEnvironmentVariable("E2E_BROWSER_CHANNEL");
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true, Channel = string.IsNullOrEmpty(channel) ? null : channel });
    }

    /// <summary>
    /// 新しい端末に見立てた、まっさらなブラウザのコンテキスト（スマホの画面幅）
    /// </summary>
    public async Task<IBrowserContext> NewDeviceAsync()
    {
        // E2E_ARTIFACTS を指定すると、失敗の調査用に操作を動画で残す（CI で失敗したときに成果物として上げる）
        var artifacts = Environment.GetEnvironmentVariable("E2E_ARTIFACTS");
        var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            Locale = "ja-JP",
            BaseURL = BaseUrl,
            RecordVideoDir = string.IsNullOrEmpty(artifacts) ? null : artifacts,
        });
        await context.AddInitScriptAsync(_helpers);
        return context;
    }

    // 公開済みのアプリを配る（存在しない URL は index.html を返す。オフライン用の service worker は配らない）
    private async Task<int> StartAppServerAsync(string root)
    {
        int port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        _app = builder.Build();
        var files = new PhysicalFileProvider(Path.GetFullPath(root));
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".dat"] = "application/octet-stream";
        types.Mappings[".blat"] = "application/octet-stream";
        if (BasePath != "/") _app.UsePathBase(BasePath.TrimEnd('/'));
        _app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.Value?.EndsWith("service-worker.js") == true) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.Headers.CacheControl = "no-store";
            await next();
        });
        _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        _app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types, ServeUnknownFileTypes = true });
        _app.MapFallback(async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.SendFileAsync(files.GetFileInfo("index.html"));
        });
        await _app.StartAsync();
        return port;
    }

    // 本物の Worker のコードを Node で動かす。Node は環境変数 E2E_NODE（なければ PATH の node）を使う
    private async Task<int> StartWorkerAsync()
    {
        int port = FreePort();
        string repoRoot = FindRepoRoot();
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("E2E_NODE") ?? "node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "worker-server.mjs"));
        psi.ArgumentList.Add(Path.Combine(repoRoot, "cloudflare", "mahjong-share-worker.js"));
        psi.ArgumentList.Add(port.ToString());
        // VS Code に同梱の Node を使う場合のため（node 本体なら影響しない）
        psi.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        _worker = Process.Start(psi)!;
        var ready = await _worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (ready == null || !ready.StartsWith("listening"))
            throw new InvalidOperationException("Worker の代役を起動できません: " + await _worker.StandardError.ReadToEndAsync());
        return port;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MahjongApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つかりません");
    }

    public async Task DisposeAsync()
    {
        if (Browser != null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_app != null) await _app.DisposeAsync();
        try { _worker?.Kill(entireProcessTree: true); } catch { }
    }
}

[CollectionDefinition("app")]
public class AppCollection : ICollectionFixture<AppFixture> { }

/// <summary>
/// 画面テスト。公開済みのアプリ（E2E_WWWROOT）がなければ飛ばす
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrEmpty(AppFixture.WwwRoot)) Skip = "E2E_WWWROOT（公開済みのアプリの wwwroot）が未設定のため、画面テストを飛ばします";
    }
}
