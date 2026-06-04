using Blazored.LocalStorage;
using MahjongApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MahjongApp.Services;

public class SessionStateService
{
    private readonly ILocalStorageService _localStorage;

    // --- 管理する状態（プロパティ） ---
    public RuleSet Rules { get; private set; } = new();
    public int ActiveParticipantCount { get; private set; } = 4;
    public List<string> CurrentPlayerNames { get; private set; } = Enumerable.Repeat(string.Empty, 7).ToList();
    public List<GameResult> ActiveHistory { get; private set; } = new();
    public Dictionary<string, int> ActiveSessionChips { get; private set; } = new();
    public string[] ScoreInputBuffers { get; private set; } = Enumerable.Repeat(string.Empty, 7).ToArray();

    // 内部で使用するキーの定数管理（タイポ防止）
    private const string KeyRules = "rules";
    private const string KeyCurrentPlayers = "currentPlayers";
    private const string KeyActiveCount = "activeParticipantCount";
    private const string KeyHistory = "ActiveHistory";
    private const string KeyChips = "ActiveSessionChips";
    private const string KeyBackupBuffers = "backup_score_buffers";

    public SessionStateService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    /// <summary>
    /// ローカルストレージから最新の状態をメモリ上に読み込みます
    /// </summary>
    public async Task InitializeStateAsync()
    {
        // 1. ルールの復元
        var savedRules = await _localStorage.GetItemAsync<RuleSet>(KeyRules);
        if (savedRules != null) Rules = savedRules;

        // 2. 参加人数の復元
        var savedActiveCount = await _localStorage.GetItemAsync<int?>(KeyActiveCount);
        ActiveParticipantCount = savedActiveCount ?? Rules.PlayerCount;

        // 3. プレイヤー名の復元
        var savedPlayers = await _localStorage.GetItemAsync<List<string>>(KeyCurrentPlayers);
        if (savedPlayers != null)
        {
            for (int i = 0; i < savedPlayers.Count && i < 7; i++)
            {
                CurrentPlayerNames[i] = savedPlayers[i] ?? string.Empty;
            }
        }

        // 4. 局履歴の復元
        var savedHistory = await _localStorage.GetItemAsync<List<GameResult>>(KeyHistory);
        if (savedHistory != null) ActiveHistory = savedHistory;

        // 5. チップ情報の復元（古いList型形式からの救済ロジックも内包）
        try
        {
            var savedChipsDict = await _localStorage.GetItemAsync<Dictionary<string, int>>(KeyChips);
            if (savedChipsDict != null)
            {
                ActiveSessionChips = savedChipsDict;
            }
            else
            {
                var savedChipsList = await _localStorage.GetItemAsync<List<int>>(KeyChips);
                if (savedChipsList != null)
                {
                    ActiveSessionChips = new Dictionary<string, int>();
                    for (int i = 0; i < savedChipsList.Count && i < CurrentPlayerNames.Count; i++)
                    {
                        var name = CurrentPlayerNames[i];
                        if (!string.IsNullOrWhiteSpace(name)) ActiveSessionChips[name] = savedChipsList[i];
                    }
                }
            }
        }
        catch
        {
            ActiveSessionChips = new Dictionary<string, int>();
        }

        // 6. 入力バッファの復元
        var backupBuffers = await _localStorage.GetItemAsync<string[]>(KeyBackupBuffers);
        if (backupBuffers != null && backupBuffers.Length == 7)
        {
            if (backupBuffers.Any(b => !string.IsNullOrEmpty(b)))
            {
                ScoreInputBuffers = backupBuffers;
            }
        }
    }

    // --- 状態変更 兼 永続化メソッド群 ---

    public async Task SaveRulesAsync(RuleSet newRules)
    {
        Rules = newRules;
        await _localStorage.SetItemAsync(KeyRules, Rules);
    }

    public async Task SaveActiveParticipantCountAsync(int count)
    {
        ActiveParticipantCount = count;
        await _localStorage.SetItemAsync(KeyActiveCount, ActiveParticipantCount);
    }

    public async Task UpdatePlayerNameAsync(int index, string name)
    {
        if (index >= 0 && index < 7)
        {
            CurrentPlayerNames[index] = name;
            await _localStorage.SetItemAsync(KeyCurrentPlayers, CurrentPlayerNames);
        }
    }

    public async Task AddGameResultAsync(GameResult result)
    {
        ActiveHistory.Add(result);
        await _localStorage.SetItemAsync(KeyHistory, ActiveHistory);
    }

    public async Task RemoveGameResultAsync(GameResult result)
    {
        ActiveHistory.Remove(result);
        await _localStorage.SetItemAsync(KeyHistory, ActiveHistory);
    }

    public async Task UpdateChipValueAsync(string name, int value)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            ActiveSessionChips[name] = value;
            await _localStorage.SetItemAsync(KeyChips, ActiveSessionChips);
        }
    }

    public async Task SaveScoreBuffersAsync(string[] buffers)
    {
        ScoreInputBuffers = buffers;
        await _localStorage.SetItemAsync(KeyBackupBuffers, ScoreInputBuffers);
    }

    public async Task ClearScoreBuffersAsync()
    {
        ScoreInputBuffers = Enumerable.Repeat(string.Empty, 7).ToArray();
        await _localStorage.RemoveItemAsync(KeyBackupBuffers);
    }

    /// <summary>
    /// 記録の完全リセット
    /// </summary>
    public async Task ClearSessionAsync()
    {
        ActiveHistory.Clear();
        ActiveSessionChips.Clear();
        ScoreInputBuffers = Enumerable.Repeat(string.Empty, 7).ToArray();

        await _localStorage.SetItemAsync(KeyHistory, ActiveHistory);
        await _localStorage.SetItemAsync(KeyChips, ActiveSessionChips);
        await _localStorage.RemoveItemAsync(KeyBackupBuffers);
    }
}