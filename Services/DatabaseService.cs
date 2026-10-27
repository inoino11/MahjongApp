using System.Text.Json;
using Microsoft.JSInterop;
using MahjongApp.Models;
using Blazored.LocalStorage;

namespace MahjongApp.Services;

/// <summary>
/// 確定済みの成績（名簿・セッション・各局）を IndexedDB に読み書きする。実処理は wwwroot/js/db.js
/// </summary>
public class DatabaseService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILocalStorageService _localStorage;
    private bool _isInitialized = false;
    public DatabaseService(IJSRuntime jsRuntime, ILocalStorageService localStorage)
    {
        _jsRuntime = jsRuntime;
        _localStorage = localStorage;
    }

    // 成績が変わったときの通知（クラウドバックアップの自動送信に使う）。
    // 全データの初期化では通知しない（空のデータでクラウドの控えを上書きしないため）
    public event Action? OnDataChanged;

    // 初回の呼び出し時に一度だけデータベースを開く
    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized)
        {
            await _jsRuntime.InvokeVoidAsync("mahjongDb.init");
            _isInitialized = true;
        }
    }

    /// <summary>
    /// セッションを確定保存する。名簿・セッション・各局を1つのトランザクションで書き込む
    /// </summary>
    public async Task SaveSessionAsync(List<PlayerProfile> playersToUpdate, SavedSessionRecord sessionRecord, List<SavedGameRecord> gameRecords)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.saveSession", playersToUpdate, sessionRecord, gameRecords);
        OnDataChanged?.Invoke();
    }

    /// <summary>
    /// 全データを一括で読み込む
    /// </summary>
    public async Task<AllDbData> GetAllDataAsync()
    {
        await EnsureInitializedAsync();
        return await _jsRuntime.InvokeAsync<AllDbData>("mahjongDb.getAllData");
    }

    public async Task UpdatePlayerAsync(PlayerProfile player)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.updatePlayer", player);
        OnDataChanged?.Invoke();
    }

    public async Task DeletePlayerAsync(string playerId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.deletePlayer", playerId);
        OnDataChanged?.Invoke();
    }

    public async Task MergePlayersAsync(string sourceId, string targetId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.mergePlayers", sourceId, targetId);
        OnDataChanged?.Invoke();
    }

    public async Task ClearAllDataAsync()
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.clearAllData");
    }

    /// <summary>
    /// バックアップの中身（名簿・セッション・各局・ルールプリセット）を JSON にする。ファイル保存とクラウドで共用する
    /// </summary>
    public async Task<string> BuildBackupJsonAsync()
    {
        var data = await GetAllDataAsync();
        var presets = await _localStorage.GetItemAsync<List<RuleSet>>(StorageKeys.RulePresets);
        data.RulePresets = presets ?? new List<RuleSet>();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        return System.Text.Json.JsonSerializer.Serialize(data, options);
    }

    public async Task<string> ExportDataAsync()
    {
        var json = await BuildBackupJsonAsync();
        var filename = $"MahjongBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var result = await _jsRuntime.InvokeAsync<string>("shareOrDownloadFile", filename, "application/json", json);
        if (result is "SHARED" or "DOWNLOADED") await MarkBackedUpAsync();
        return result;
    }

    /// <summary>
    /// 最後にバックアップした日時を記録する（バックアップ忘れの案内に使う）
    /// </summary>
    public async Task MarkBackedUpAsync()
    {
        try
        {
            await _localStorage.SetItemAsync(StorageKeys.LastBackupAt, DateTime.Now);
        }
        catch { }
    }

    /// <summary>
    /// 最後にバックアップした日時。一度もしていなければ null
    /// </summary>
    public async Task<DateTime?> GetLastBackupAtAsync()
    {
        try
        {
            return await _localStorage.GetItemAsync<DateTime?>(StorageKeys.LastBackupAt);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// バックアップから復元する。isMerge が false なら成績を消してから入れる。
    /// 戻り値はルールプリセットの取り込み結果（成績の復元は済んでいるので、プリセットだけ失敗しても例外にはしない）
    /// </summary>
    public async Task<RulePresetService.ImportResult> ImportDataAsync(string json, bool isMerge = false)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.restoreData", json, isMerge);
        RulePresetService.ImportResult result;
        try
        {
            var (imported, unreadable) = RulePresetService.ParsePresets(json);
            imported = RulePresetService.Distinct(imported);
            if (isMerge)
            {
                var current = await _localStorage.GetItemAsync<List<RuleSet>>(StorageKeys.RulePresets) ?? new List<RuleSet>();
                var merged = RulePresetService.Merge(current, imported);
                if (merged.Added > 0) await _localStorage.SetItemAsync(StorageKeys.RulePresets, current);
                result = merged with { Unreadable = unreadable };
            }
            else
            {
                // バックアップにプリセットが含まれないとき（プリセットを保存していなかった頃のファイルなど）は、今のものを残す
                if (imported.Count > 0) await _localStorage.SetItemAsync(StorageKeys.RulePresets, imported);
                result = new RulePresetService.ImportResult(imported.Count, 0, unreadable);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ルールプリセットの取り込みに失敗しました: {ex.Message}");
            result = new RulePresetService.ImportResult(0, 0, 0, ex.Message);
        }
        OnDataChanged?.Invoke();
        return result;
    }
    public async Task DeleteSessionAsync(string sessionId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.deleteSession", sessionId);
        OnDataChanged?.Invoke();
    }
}

/// <summary>
/// 全データの受け皿（バックアップファイルの形式も兼ねる）
/// </summary>
public class AllDbData
{
    public List<PlayerProfile> Players { get; set; } = new();
    public List<SavedSessionRecord> Sessions { get; set; } = new();
    public List<SavedGameRecord> Games { get; set; } = new();
    public List<RuleSet>? RulePresets { get; set; }
}