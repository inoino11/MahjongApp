using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blazored.LocalStorage;
using Microsoft.JSInterop;

namespace MahjongApp.Services;

/// <summary>
/// クラウドバックアップの有効化・自動送信・復元・停止を担当する。
/// 成績は端末内で復元キーから導いた鍵で暗号化して送るため、サーバーは中身を読めない
/// </summary>
public class CloudBackupService
{
    private const string WorkerUrl = LiveShareService.WorkerUrl;
    // データが変わってから少し待ち、続けて変わった分はまとめて送る
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);
    // KV の書き込み回数（無料枠は1日1,000回）を抑えるため、送信の間隔を空ける
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(10);
    // 記録がなくてもサーバーの保存期限（365日）で消えないよう、この期間ごとに送り直す
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(30);

    private readonly HttpClient _http;
    private readonly ILocalStorageService _localStorage;
    private readonly IJSRuntime _jsRuntime;
    private readonly DatabaseService _database;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CloudBackupState _state = new();
    private bool _isLoaded = false;
    private int _changeVersion = 0;
    private CancellationTokenSource? _scheduleCts;
    // パスキーで包んだ復元キー。次の送信で一緒に送る
    private string? _pendingWrappedKey;

    public bool IsEnabled => !string.IsNullOrEmpty(_state.RecoveryKey);
    public bool IsPending => _state.Dirty;
    public bool IsSending { get; private set; }
    public bool HasError { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastSuccessAt => _state.LastSuccessAt;
    public bool PasskeyRegistered => _state.PasskeyRegistered;
    // 別の端末が復元して引き継いだため、この端末のバックアップを止めた
    public bool TakenOver => _state.TakenOver;
    public string? RecoveryKey => _state.RecoveryKey;

    // 状態が変わったときの通知（設定画面の表示更新に使う）
    public event Action? OnChanged;

    // 端末に保存する状態
    public class CloudBackupState
    {
        public string? RecoveryKey { get; set; }
        public string DeviceId { get; set; } = "";
        public bool PasskeyRegistered { get; set; }
        public DateTime? LastSuccessAt { get; set; }
        public bool Dirty { get; set; }
        public bool TakenOver { get; set; }
    }

    public class Generation
    {
        public DateTime At { get; set; }
        public string Data { get; set; } = "";
    }

    private class DerivedKeys
    {
        public string BackupId { get; set; } = "";
        public string Token { get; set; } = "";
    }

    // passkey.register / passkey.unlock の戻り値
    public class PasskeyResult
    {
        public string Status { get; set; } = "";
        public string? WrappedKey { get; set; }
        public string? RecoveryKey { get; set; }
        public string? Message { get; set; }
    }

    // サーバーにあるバックアップの一覧
    public class BackupListing
    {
        public bool HasWrappedKey { get; set; }
        public List<Generation> Generations { get; set; } = new();
    }

    public CloudBackupService(HttpClient http, ILocalStorageService localStorage, IJSRuntime jsRuntime, DatabaseService database)
    {
        _http = http;
        _localStorage = localStorage;
        _jsRuntime = jsRuntime;
        _database = database;
        _database.OnDataChanged += ScheduleBackup;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;
        try
        {
            _state = await _localStorage.GetItemAsync<CloudBackupState>(StorageKeys.CloudBackup) ?? new();
        }
        catch
        {
            _state = new();
        }
        _isLoaded = true;
    }

    /// <summary>
    /// 起動時に呼ぶ。送り残しがあるか、しばらく送っていなければ送る
    /// </summary>
    public async Task InitializeAsync()
    {
        await EnsureLoadedAsync();
        if (!IsEnabled) return;
        bool isStale = _state.LastSuccessAt == null || DateTime.Now - _state.LastSuccessAt.Value > RefreshInterval;
        if (_state.Dirty || isStale) ScheduleBackup();
    }

    /// <summary>
    /// クラウドバックアップを始める。最初の送信まで成功したら、利用者に控えてもらう復元キーを返す
    /// </summary>
    public async Task<string> EnableAsync()
    {
        await EnsureLoadedAsync();
        if (IsEnabled) return _state.RecoveryKey!;
        string recoveryKey = await _jsRuntime.InvokeAsync<string>("blazorCrypto.backup.newRecoveryKey");
        var previous = _state;
        _state = new CloudBackupState { RecoveryKey = recoveryKey, DeviceId = Guid.NewGuid().ToString("N"), Dirty = true };
        bool ok = await BackupNowAsync();
        if (!ok)
        {
            string message = LastError ?? "送信に失敗しました";
            _state = previous;
            HasError = false;
            LastError = null;
            OnChanged?.Invoke();
            throw new InvalidOperationException(message);
        }
        return recoveryKey;
    }

    /// <summary>
    /// 成績が変わったら呼ばれる。少し待ってから送る（前回の送信から間がなければ、間隔が空くまで待つ）
    /// </summary>
    public void ScheduleBackup()
    {
        if (!IsEnabled) return;
        _changeVersion++;
        if (!_state.Dirty)
        {
            _state.Dirty = true;
            _ = SaveStateAsync();
            OnChanged?.Invoke();
        }
        var delay = Debounce;
        if (_state.LastSuccessAt != null)
        {
            var untilAllowed = _state.LastSuccessAt.Value + MinInterval - DateTime.Now;
            if (untilAllowed > delay) delay = untilAllowed;
        }
        _scheduleCts?.Cancel();
        var cts = _scheduleCts = new CancellationTokenSource();
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
        await BackupNowAsync();
    }

    /// <summary>
    /// 今の成績をすぐに送る。force は復元した端末がバックアップを引き継ぐときに使う
    /// </summary>
    public async Task<bool> BackupNowAsync(bool force = false)
    {
        await EnsureLoadedAsync();
        if (!IsEnabled) return false;
        _scheduleCts?.Cancel();
        await _sendLock.WaitAsync();
        HttpStatusCode? statusCode = null;
        try
        {
            IsSending = true;
            OnChanged?.Invoke();
            int version = _changeVersion;
            string recoveryKey = _state.RecoveryKey!;
            string? wrappedKey = _pendingWrappedKey;
            string json = await _database.BuildBackupJsonAsync();
            string encrypted = await _jsRuntime.InvokeAsync<string>("blazorCrypto.backup.encrypt", recoveryKey, json);
            var keys = await DeriveAsync(recoveryKey);
            var request = new HttpRequestMessage(HttpMethod.Put, $"{WorkerUrl}/backup/{keys.BackupId}")
            {
                Content = JsonContent.Create(new { encryptedData = encrypted, deviceId = _state.DeviceId, force, wrappedKey })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keys.Token);
            var response = await _http.SendAsync(request);
            statusCode = response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                _state.LastSuccessAt = DateTime.Now;
                if (version == _changeVersion) _state.Dirty = false;
                if (wrappedKey != null)
                {
                    _state.PasskeyRegistered = true;
                    _pendingWrappedKey = null;
                }
                HasError = false;
                LastError = null;
                await SaveStateAsync();
                await _database.MarkBackedUpAsync();
                return true;
            }
            HasError = true;
            LastError = statusCode switch
            {
                HttpStatusCode.RequestEntityTooLarge => "データが大きすぎて送れませんでした",
                HttpStatusCode.TooManyRequests => WorkerErrors.DailyLimitMessage.Replace("\n", "。") + "。次に開いたときや記録が変わったときに送り直します",
                _ => $"送信に失敗しました（{(int)statusCode}）"
            };
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"クラウドバックアップの送信に失敗しました: {ex.Message}");
            HasError = true;
            LastError = "送信に失敗しました。通信状況を確認してください";
            return false;
        }
        finally
        {
            IsSending = false;
            _sendLock.Release();
            // 別の端末が復元して引き継いだ後は、この端末から上書きしないよう止める
            if (statusCode == HttpStatusCode.Conflict)
            {
                _state = new CloudBackupState { TakenOver = true };
                HasError = false;
                LastError = null;
                await SaveStateAsync();
            }
            OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// クラウドバックアップを止め、サーバー上の控えと端末の復元キーを消す
    /// </summary>
    public async Task DisableAsync()
    {
        await EnsureLoadedAsync();
        if (!IsEnabled) return;
        _scheduleCts?.Cancel();
        var keys = await DeriveAsync(_state.RecoveryKey!);
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{WorkerUrl}/backup/{keys.BackupId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keys.Token);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"削除に失敗しました（{(int)response.StatusCode}）");
        _state = new CloudBackupState();
        HasError = false;
        LastError = null;
        await SaveStateAsync();
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 全データの初期化の前に呼ぶ。送信予定を取り消し、端末の復元キーを忘れる（クラウド上の控えは残す）
    /// </summary>
    public void ForgetOnThisDevice()
    {
        _scheduleCts?.Cancel();
        _state = new CloudBackupState();
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 「引き継がれたため停止した」の表示を消す
    /// </summary>
    public async Task DismissTakenOverAsync()
    {
        _state.TakenOver = false;
        await SaveStateAsync();
        OnChanged?.Invoke();
    }

    public async Task<bool> IsPasskeySupportedAsync() =>
        await _jsRuntime.InvokeAsync<bool>("passkey.isSupported");

    /// <summary>
    /// パスキーを作り、それで包んだ復元キーをサーバーに置く。戻り値の Status は OK / UNSUPPORTED / CANCELED / ERROR
    /// </summary>
    public async Task<PasskeyResult> RegisterPasskeyAsync()
    {
        await EnsureLoadedAsync();
        if (!IsEnabled) return new PasskeyResult { Status = "ERROR", Message = "クラウドバックアップが有効ではありません" };
        var keys = await DeriveAsync(_state.RecoveryKey!);
        var result = await _jsRuntime.InvokeAsync<PasskeyResult>("passkey.register", keys.BackupId, _state.RecoveryKey);
        if (result.Status != "OK" || result.WrappedKey == null) return result;
        _pendingWrappedKey = result.WrappedKey;
        if (!await BackupNowAsync()) return new PasskeyResult { Status = "ERROR", Message = LastError };
        return result;
    }

    /// <summary>
    /// パスキーで復元キーを取り出す。戻り値の Status は OK / UNSUPPORTED / NOT_FOUND / CANCELED / ERROR
    /// </summary>
    public async Task<PasskeyResult> UnlockWithPasskeyAsync() =>
        await _jsRuntime.InvokeAsync<PasskeyResult>("passkey.unlock", WorkerUrl);

    public async Task<bool> IsValidRecoveryKeyAsync(string recoveryKey) =>
        await _jsRuntime.InvokeAsync<bool>("blazorCrypto.backup.isValidRecoveryKey", recoveryKey);

    /// <summary>
    /// 復元キーに対応するバックアップの一覧（世代は新しい順）。見つからなければ null
    /// </summary>
    public async Task<BackupListing?> GetListingAsync(string recoveryKey)
    {
        var keys = await DeriveAsync(recoveryKey);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{WorkerUrl}/backup/{keys.BackupId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keys.Token);
        var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BackupListing>();
    }

    /// <summary>
    /// 選んだ世代で成績を復元し、以後はこの端末がバックアップを引き継ぐ
    /// </summary>
    public async Task<RulePresetService.ImportResult> RestoreAsync(string recoveryKey, BackupListing listing, Generation generation, bool isMerge)
    {
        await EnsureLoadedAsync();
        string json = await _jsRuntime.InvokeAsync<string>("blazorCrypto.backup.decrypt", recoveryKey, generation.Data);
        _scheduleCts?.Cancel();
        _state = new CloudBackupState
        {
            RecoveryKey = recoveryKey,
            DeviceId = string.IsNullOrEmpty(_state.DeviceId) ? Guid.NewGuid().ToString("N") : _state.DeviceId,
            PasskeyRegistered = listing.HasWrappedKey,
            Dirty = true
        };
        await SaveStateAsync();
        var presetResult = await _database.ImportDataAsync(json, isMerge);
        await BackupNowAsync(force: true);
        return presetResult;
    }

    private async Task<DerivedKeys> DeriveAsync(string recoveryKey) =>
        await _jsRuntime.InvokeAsync<DerivedKeys>("blazorCrypto.backup.derive", recoveryKey);

    private async Task SaveStateAsync()
    {
        try
        {
            await _localStorage.SetItemAsync(StorageKeys.CloudBackup, _state);
        }
        catch { }
    }
}
