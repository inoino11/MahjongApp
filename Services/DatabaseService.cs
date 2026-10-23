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
        var presets = await _localStorage.GetItemAsync<List<RuleSet>>(StorageKeys.RulePresets);
        data.RulePresets = presets ?? new List<RuleSet>();
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
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            var importedData = System.Text.Json.JsonSerializer.Deserialize<AllDbData>(json, options);
            if (importedData?.RulePresets != null && importedData.RulePresets.Count > 0)
            {
                if (isMerge)
                {
                    var currentPresets = await _localStorage.GetItemAsync<List<RuleSet>>(StorageKeys.RulePresets) ?? new List<RuleSet>();
                    foreach (var preset in importedData.RulePresets)
                    {
                        // 同名のプリセットがあれば「名前_連番」にして両方残す
                        if (currentPresets.Any(p => p.Name == preset.Name))
                        {
                            string baseName = preset.Name;
                            int counter = 1;
                            string newName = $"{baseName}_{counter}";
                            while (currentPresets.Any(p => p.Name == newName))
                            {
                                counter++;
                                newName = $"{baseName}_{counter}";
                            }
                            preset.Name = newName;
                        }
                        currentPresets.Add(preset);
                    }
                    await _localStorage.SetItemAsync(StorageKeys.RulePresets, currentPresets);
                }
                else
                {
                    await _localStorage.SetItemAsync(StorageKeys.RulePresets, importedData.RulePresets);
                }
            }
        }
        catch
        {
            // プリセット部分が読めなくても、DBの復元は済んでいるのでそのまま終える
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await EnsureInitializedAsync();
        await _jsRuntime.InvokeVoidAsync("mahjongDb.deleteSession", sessionId);
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