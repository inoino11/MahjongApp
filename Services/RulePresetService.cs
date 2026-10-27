using Blazored.LocalStorage;
using MahjongApp.Models;

namespace MahjongApp.Services;

/// <summary>
/// 保存済みのルール（プリセット）の読み込みと保存。初回はよく使うルールを用意する
/// </summary>
public class RulePresetService
{
    private readonly ILocalStorageService _localStorage;
    private List<RuleSet>? _presets;

    public RulePresetService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    /// <summary>
    /// プリセットの一覧（画面間で同じリストを共有する。並べ替え・削除したら SaveAsync を呼ぶ）
    /// </summary>
    public async Task<List<RuleSet>> GetAsync()
    {
        if (_presets != null) return _presets;
        List<RuleSet>? saved;
        bool isUnreadable = false;
        try
        {
            saved = await _localStorage.GetItemAsync<List<RuleSet>>(StorageKeys.RulePresets);
        }
        catch (Exception ex)
        {
            // 読めない場合もデフォルトで起動を続ける（保存済みプリセットを上書きしないよう、保存はしない）
            Console.WriteLine($"プリセットの読み込みに失敗しました: {ex.Message}");
            saved = null;
            isUnreadable = true;
        }
        if (saved != null && saved.Count > 0)
        {
            _presets = saved;
            return _presets;
        }
        _presets = CreateDefaults();
        if (!isUnreadable) await _localStorage.SetItemAsync(StorageKeys.RulePresets, _presets);
        return _presets;
    }

    public async Task SaveAsync()
    {
        if (_presets == null) return;
        await _localStorage.SetItemAsync(StorageKeys.RulePresets, _presets);
    }

    /// <summary>
    /// 同じ名前のプリセットがあればその位置のまま差し替え、なければ末尾に加える
    /// </summary>
    public static void Upsert(List<RuleSet> presets, RuleSet rule)
    {
        int index = presets.FindIndex(p => p.Name == rule.Name);
        if (index >= 0) presets[index] = rule;
        else presets.Add(rule);
    }

    private static List<RuleSet> CreateDefaults()
    {
        var presets = new List<RuleSet>
        {
            new() { Name = "一般的な四麻", PlayerCount = 4, StartingPoints = 25000, ReturnPoints = 30000, Uma1 = 10, Uma2 = 20 },
            new() { Name = "一般的な三麻", PlayerCount = 3, StartingPoints = 35000, ReturnPoints = 40000, Uma1 = 10, Uma2 = 20 },
            new() { Name = "Mリーグルール", PlayerCount = 4, StartingPoints = 25000, ReturnPoints = 30000, Uma1 = 10, Uma2 = 30, Rate = 0, ChipRate = 0, TieBreaker = TieBreakerRule.Split },
        };
        presets.ForEach(p => p.SyncCustomUmaFromNormal());
        return presets;
    }
}
