using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MahjongApp.Services;

/// <summary>
/// 観戦URLの発行・自動更新・終了を担当する。
/// 対局データは端末内で暗号化して送り、暗号鍵はURLの # 以降にだけ含める（サーバーには送らない）
/// </summary>
public class LiveShareService
{
    public const string WorkerUrl = "https://api.inoino11.com";
    // 送信回数（KV の書き込み）を抑えるため、変化してから少し待ってまとめ、前回の送信からも間隔を空ける。
    // 観戦は数局に一度見て現状が分かれば足りるので、記録が反映されるまで最大1分ほど遅れてよい
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly ILocalStorageService _localStorage;
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigationManager;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private LiveShareInfo? _info;
    private Task? _loadTask;
    private string? _lastSentJson;
    private string? _pendingJson;
    private CancellationTokenSource? _debounceCts;
    private DateTime? _lastSentAt;
    private DotNetObjectReference<LiveShareService>? _selfRef;

    public bool IsActive => _info != null;
    public bool HasError { get; private set; }
    // 直前の送信が、サーバーの1日の上限で断られた
    public bool IsDailyLimited { get; private set; }
    public string? ShareUrl => _info == null ? null : $"{_navigationManager.BaseUri}shared?live={_info.Id}#{_info.Key}";

    // 配信状態・エラー状態が変わったときの通知（ボタン表示の更新に使う）
    public event Action? OnChanged;

    // 端末に保存する配信情報（アプリを再起動しても同じURLを更新し続けるため）
    public class LiveShareInfo
    {
        public string Id { get; set; } = "";
        public string Token { get; set; } = "";
        public string Key { get; set; } = "";
        // 最後に合計が ±0 だったチップ（入力途中のチップは送らない）
        public Dictionary<string, int>? BalancedChips { get; set; }
    }

    private class CreatedResponse
    {
        public string Id { get; set; } = "";
        public string Token { get; set; } = "";
    }

    private class CryptoResult
    {
        public string CombinedBase64 { get; set; } = "";
        public string KeyUrlSafeBase64 { get; set; } = "";
    }

    public LiveShareService(HttpClient http, ILocalStorageService localStorage, IJSRuntime jsRuntime, NavigationManager navigationManager)
    {
        _http = http;
        _localStorage = localStorage;
        _jsRuntime = jsRuntime;
        _navigationManager = navigationManager;
    }

    // 複数の画面から同時に呼ばれても、読み込みと登録は1回だけにする
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _info = await _localStorage.GetItemAsync<LiveShareInfo>(StorageKeys.LiveShare);
        }
        catch
        {
            _info = null;
        }
        // 画面を閉じる・アプリを切り替えるときは、待っている送信をすぐ送る（スマホは画面を消すとタイマーが止まるため）
        try
        {
            _selfRef = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("registerVisibilityFlush", _selfRef, nameof(FlushPendingAsync));
        }
        catch { }
    }

    [JSInvokable]
    public async Task FlushPendingAsync()
    {
        if (_info == null || _pendingJson == null) return;
        _debounceCts?.Cancel();
        await UpdateNowAsync();
    }

    /// <summary>
    /// 観戦データに載せるチップ。合計が ±0 のときだけ今の値を採用し、入力途中は前に採用した値のままにする
    /// </summary>
    public Dictionary<string, int> ChipsToSend(Dictionary<string, int> current)
    {
        if (_info == null) return current;
        var chips = SelectChips(current, _info.BalancedChips);
        if (!ReferenceEquals(chips, _info.BalancedChips))
        {
            _info.BalancedChips = chips;
            _ = _localStorage.SetItemAsync(StorageKeys.LiveShare, _info).AsTask();
        }
        return chips;
    }

    public static Dictionary<string, int> SelectChips(Dictionary<string, int> current, Dictionary<string, int>? lastBalanced)
    {
        // まだ一度も採用していなければ、今の値をそのまま使う（配信開始時の状態を引き継ぐ）
        if (lastBalanced == null) return new Dictionary<string, int>(current);
        if (current.Values.Sum() != 0) return lastBalanced;
        if (lastBalanced != null && lastBalanced.Count == current.Count && lastBalanced.All(kv => current.TryGetValue(kv.Key, out var v) && v == kv.Value)) return lastBalanced;
        return new Dictionary<string, int>(current);
    }

    /// <summary>
    /// 観戦URLを発行する（発行済みならそのURLを返す）
    /// </summary>
    public async Task<string> StartAsync(string payloadJson)
    {
        await EnsureLoadedAsync();
        if (_info != null) return ShareUrl!;

        var encrypted = await _jsRuntime.InvokeAsync<CryptoResult>("blazorCrypto.encrypt", payloadJson);
        var response = await _http.PostAsJsonAsync($"{WorkerUrl}/live", new { encryptedData = encrypted.CombinedBase64 });
        if (WorkerErrors.IsDailyLimit(response.StatusCode)) throw new InvalidOperationException(WorkerErrors.DailyLimitMessage);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        if (created == null || string.IsNullOrEmpty(created.Id) || string.IsNullOrEmpty(created.Token))
            throw new InvalidOperationException("観戦URLの発行に失敗しました");

        _info = new LiveShareInfo { Id = created.Id, Token = created.Token, Key = encrypted.KeyUrlSafeBase64 };
        await _localStorage.SetItemAsync(StorageKeys.LiveShare, _info);
        _lastSentJson = payloadJson;
        _lastSentAt = DateTime.Now;
        _pendingJson = null;
        HasError = false;
        OnChanged?.Invoke();
        return ShareUrl!;
    }

    /// <summary>
    /// 表示内容が変わっていれば、少し待ってからまとめて送る（連続した変更は最後の1回だけ送る）
    /// </summary>
    public void ScheduleUpdate(string payloadJson)
    {
        if (_info == null || payloadJson == _lastSentJson || payloadJson == _pendingJson) return;
        _pendingJson = payloadJson;
        var delay = Debounce;
        if (_lastSentAt != null)
        {
            var untilAllowed = _lastSentAt.Value + MinInterval - DateTime.Now;
            if (untilAllowed > delay) delay = untilAllowed;
        }
        _debounceCts?.Cancel();
        var cts = _debounceCts = new CancellationTokenSource();
        _ = SendAfterDelayAsync(delay, cts.Token);
    }

    private async Task SendAfterDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        await UpdateNowAsync();
    }

    /// <summary>
    /// 未送信の変更（なければ最後に送った内容）をすぐに送る
    /// </summary>
    public async Task<bool> UpdateNowAsync()
    {
        var json = _pendingJson ?? _lastSentJson;
        if (_info == null || json == null) return false;
        bool ok = await PutAsync("live", json);
        if (ok)
        {
            _lastSentJson = json;
            if (_pendingJson == json) _pendingJson = null;
        }
        return ok;
    }

    /// <summary>
    /// 配信を終了する（finished: 確定 / stopped: 停止 / discarded: 記録の破棄）。終了後は端末の配信情報を消す
    /// </summary>
    public async Task EndAsync(string status, string? payloadJson)
    {
        await EnsureLoadedAsync();
        if (_info == null) return;
        _debounceCts?.Cancel();
        await PutAsync(status, payloadJson);
        await ClearAsync();
    }

    private async Task ClearAsync()
    {
        _info = null;
        _lastSentJson = null;
        _pendingJson = null;
        HasError = false;
        try
        {
            await _localStorage.RemoveItemAsync(StorageKeys.LiveShare);
        }
        catch { }
        OnChanged?.Invoke();
    }

    private async Task<bool> PutAsync(string status, string? payloadJson)
    {
        var info = _info;
        if (info == null) return false;
        HttpStatusCode? statusCode = null;
        await _sendLock.WaitAsync();
        try
        {
            string? encrypted = payloadJson == null ? null : await _jsRuntime.InvokeAsync<string>("blazorCrypto.encryptWithKey", payloadJson, info.Key);
            var request = new HttpRequestMessage(HttpMethod.Put, $"{WorkerUrl}/live/{info.Id}")
            {
                Content = JsonContent.Create(new { encryptedData = encrypted, status })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
            var response = await _http.SendAsync(request);
            statusCode = response.StatusCode;
            HasError = !response.IsSuccessStatusCode;
            IsDailyLimited = WorkerErrors.IsDailyLimit(statusCode);
            if (!HasError) _lastSentAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"観戦URLの更新に失敗しました: {ex.Message}");
            HasError = true;
        }
        finally
        {
            _sendLock.Release();
        }
        // URLが失効している（期限切れ・鍵不一致）場合は、配信情報を消して新しく発行できるようにする
        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            await ClearAsync();
            return false;
        }
        OnChanged?.Invoke();
        return !HasError;
    }
}
