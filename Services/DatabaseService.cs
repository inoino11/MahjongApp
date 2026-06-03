using System.Text.Json;
using Microsoft.JSInterop;
using MahjongApp.Models;

namespace MahjongApp.Services;

public class DatabaseService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _isInitialized = false;
    public DatabaseService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// データベースの初期化を保証する内部メソッド
    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized)
        {
            await _jsRuntime.InvokeVoidAsync("mahjongDb.init");
            _isInitialized = true;
        }
    }

    /// その日の対局（セッション）を保存
    public async Task SaveSessionAsync(List<PlayerProfile> playersToUpdate, SavedSessionRecord sessionRecord, List<SavedGameRecord> gameRecords)
    {
        await EnsureInitializedAsync();
        // JSの saveSession 関数にデータを丸ごと渡す
        await _jsRuntime.InvokeVoidAsync("mahjongDb.saveSession", playersToUpdate, sessionRecord, gameRecords);
    }

    /// 成績ダッシュボード表示用に、データベースの全データを一括取得
    public async Task<AllDbData> GetAllDataAsync()
    {
        await EnsureInitializedAsync();
        return await _jsRuntime.InvokeAsync<AllDbData>("mahjongDb.getAllData");
    }

    public async Task UpdatePlayerAsync(PlayerProfile player)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.updatePlayer", player);
    }

    public async Task DeletePlayerAsync(string playerId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.deletePlayer", playerId);
    }

    public async Task MergePlayersAsync(string sourceId, string targetId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.mergePlayers", sourceId, targetId);
    }

    public async Task ClearAllDataAsync()
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.clearAllData");
    }

    public async Task<string> ExportDataAsync()
    {
        var data = await GetAllDataAsync();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        var json = System.Text.Json.JsonSerializer.Serialize(data, options);
        var filename = $"MahjongBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        return await _jsRuntime.InvokeAsync<string>("shareOrDownloadFile", filename, "application/json", json);
    }

    public async Task ImportDataAsync(string json, bool isMerge = false)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.restoreData", json, isMerge);
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.deleteSession", sessionId);
    }
}

/// ダッシュボード読み込み用のデータ受け皿クラス
public class AllDbData
{
    public List<PlayerProfile> Players { get; set; } = new();
    public List<SavedSessionRecord> Sessions { get; set; } = new();
    public List<SavedGameRecord> Games { get; set; } = new();
}