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
    public int TemporaryTotalFee { get; private set; } = 0;
    public int TemporaryFeeRule { get; private set; } = 1;

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
        var savedRules = await SafeGetAsync<RuleSet>(StorageKeys.Rules);
        if (savedRules != null) Rules = savedRules;

        // 2. 参加人数の復元
        var savedActiveCount = await SafeGetAsync<int?>(StorageKeys.ActiveParticipantCount);
        ActiveParticipantCount = Math.Clamp(savedActiveCount ?? Rules.PlayerCount, 3, 7);

        // 3. プレイヤー名の復元
        var savedPlayers = await SafeGetAsync<List<string>>(StorageKeys.CurrentPlayers);
        if (savedPlayers != null)
        {
            for (int i = 0; i < savedPlayers.Count && i < 7; i++)
            {
                CurrentPlayerNames[i] = savedPlayers[i] ?? string.Empty;
            }
        }

        // 4. 局履歴の復元
        var savedHistory = await SafeGetAsync<List<GameResult>>(StorageKeys.ActiveHistory);
        if (savedHistory != null) ActiveHistory = savedHistory;

        // 5. チップ情報の復元（古いList型形式からの救済ロジックも内包）
        try
        {
            var savedChipsDict = await SafeGetAsync<Dictionary<string, int>>(StorageKeys.ActiveSessionChips);
            if (savedChipsDict != null)
            {
                ActiveSessionChips = savedChipsDict;
            }
            else
            {
                var savedChipsList = await SafeGetAsync<List<int>>(StorageKeys.ActiveSessionChips);
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
        var backupBuffers = await SafeGetAsync<string[]>(StorageKeys.BackupScoreBuffers);
        if (backupBuffers != null && backupBuffers.Length == 7)
        {
            if (backupBuffers.Any(b => !string.IsNullOrEmpty(b)))
            {
                ScoreInputBuffers = backupBuffers;
            }
        }

        // 7. 一時場代の復元
        var savedTempFee = await SafeGetAsync<int?>(StorageKeys.TempTotalFee);
        if (savedTempFee.HasValue) TemporaryTotalFee = savedTempFee.Value;

        // 8. 一時支払いルールの復元
        var savedTempRule = await SafeGetAsync<int?>(StorageKeys.TempFeeRule);
        if (savedTempRule.HasValue) TemporaryFeeRule = savedTempRule.Value;
    }

    // 読み込みに失敗した値（保存形式の変更・破損など）は初期値扱いにして、起動を止めない。
    // 救済できる可能性を残すため、読めなかったキーは削除しない。
    private async Task<T?> SafeGetAsync<T>(string key)
    {
        try
        {
            return await _localStorage.GetItemAsync<T>(key);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"localStorage '{key}' の読み込みに失敗しました: {ex.Message}");
            return default;
        }
    }

    // --- 状態変更 兼 永続化メソッド群 ---

    public async Task SaveRulesAsync(RuleSet newRules)
    {
        Rules = newRules;
        await _localStorage.SetItemAsync(StorageKeys.Rules, Rules);
    }

    public async Task SaveActiveParticipantCountAsync(int count)
    {
        ActiveParticipantCount = count;
        await _localStorage.SetItemAsync(StorageKeys.ActiveParticipantCount, ActiveParticipantCount);
    }

    public async Task UpdatePlayerNameAsync(int index, string name)
    {
        if (index >= 0 && index < 7)
        {
            CurrentPlayerNames[index] = name;
            await _localStorage.SetItemAsync(StorageKeys.CurrentPlayers, CurrentPlayerNames);
        }
    }

    public async Task AddGameResultAsync(GameResult result)
    {
        ActiveHistory.Add(result);
        await _localStorage.SetItemAsync(StorageKeys.ActiveHistory, ActiveHistory);
    }

    public async Task RemoveGameResultAsync(GameResult result)
    {
        ActiveHistory.Remove(result);
        await _localStorage.SetItemAsync(StorageKeys.ActiveHistory, ActiveHistory);
    }

    public async Task UpdateChipValueAsync(string name, int value)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            ActiveSessionChips[name] = value;
            await _localStorage.SetItemAsync(StorageKeys.ActiveSessionChips, ActiveSessionChips);
        }
    }

    public async Task SaveScoreBuffersAsync(string[] buffers)
    {
        ScoreInputBuffers = buffers;
        await _localStorage.SetItemAsync(StorageKeys.BackupScoreBuffers, ScoreInputBuffers);
    }

    public async Task ClearScoreBuffersAsync()
    {
        ScoreInputBuffers = Enumerable.Repeat(string.Empty, 7).ToArray();
        await _localStorage.RemoveItemAsync(StorageKeys.BackupScoreBuffers);
    }

    public async Task SaveTemporaryFeeAsync(int fee)
    {
        TemporaryTotalFee = fee;
        await _localStorage.SetItemAsync(StorageKeys.TempTotalFee, TemporaryTotalFee);
    }

    public async Task SaveTemporaryFeeRuleAsync(int rule)
    {
        TemporaryFeeRule = rule;
        await _localStorage.SetItemAsync(StorageKeys.TempFeeRule, TemporaryFeeRule);
    }

    /// <summary>
    /// 記録の完全リセット
    /// </summary>
    public async Task ClearSessionAsync()
    {
        ActiveHistory.Clear();
        ActiveSessionChips.Clear();
        ScoreInputBuffers = Enumerable.Repeat(string.Empty, 7).ToArray();

        await _localStorage.SetItemAsync(StorageKeys.ActiveHistory, ActiveHistory);
        await _localStorage.SetItemAsync(StorageKeys.ActiveSessionChips, ActiveSessionChips);
        await _localStorage.RemoveItemAsync(StorageKeys.BackupScoreBuffers);
    }
}