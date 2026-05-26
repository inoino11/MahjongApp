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
}

/// ダッシュボード読み込み用のデータ受け皿クラス
public class AllDbData
{
    public List<PlayerProfile> Players { get; set; } = new();
    public List<SavedSessionRecord> Sessions { get; set; } = new();
    public List<SavedGameRecord> Games { get; set; } = new();
}