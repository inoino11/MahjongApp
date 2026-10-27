using System.Text.Json;
using System.Text.RegularExpressions;
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

    // --- バックアップからの取り込み ---

    // 取り込みの結果（画面のメッセージに使う）
    public record ImportResult(int Added, int Skipped, int Unreadable, string? Error = null);

    /// <summary>
    /// バックアップのJSONから、ルールプリセットの部分だけを1件ずつ読み出す。
    /// 成績側に読めない値があってもプリセットは読めるよう、ファイル全体は型に当てはめない。読めない1件は数えて飛ばす
    /// </summary>
    public static (List<RuleSet> Presets, int Unreadable) ParsePresets(string backupJson)
    {
        var presets = new List<RuleSet>();
        int unreadable = 0;
        using var doc = JsonDocument.Parse(backupJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("rulePresets", out var array)
            || array.ValueKind != JsonValueKind.Array)
            return (presets, 0); // プリセットを含まない古いバックアップ
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        foreach (var element in array.EnumerateArray())
        {
            try
            {
                var preset = element.Deserialize<RuleSet>(options);
                if (preset == null || string.IsNullOrWhiteSpace(preset.Name)) { unreadable++; continue; }
                presets.Add(preset);
            }
            catch (JsonException)
            {
                unreadable++;
            }
        }
        return (presets, unreadable);
    }

    /// <summary>
    /// 名前も中身も同じプリセットの重なりを1件にまとめる
    /// </summary>
    public static List<RuleSet> Distinct(IEnumerable<RuleSet> presets)
    {
        var seen = new HashSet<string>();
        return presets.Where(p => seen.Add(p.ToComparableJson())).ToList();
    }

    /// <summary>
    /// 取り込んだプリセットを current に統合する。同じ中身のものがすでにあれば加えない。
    /// 同じ名前で中身が違うときは「名前_1」のように番号を付けて加える（その番号付きの中に同じ中身があれば、それも加えない）
    /// </summary>
    public static ImportResult Merge(List<RuleSet> current, IEnumerable<RuleSet> imported)
    {
        int added = 0, skipped = 0;
        foreach (var preset in imported)
        {
            // 同じ名前か、その番号付き（名前_1、名前_2…）の中に、同じ中身のものがあるか
            var family = new Regex($"^{Regex.Escape(preset.Name)}(_\\d+)?$");
            string content = preset.ToComparableJson(ignoreName: true);
            if (current.Any(p => family.IsMatch(p.Name) && p.ToComparableJson(ignoreName: true) == content))
            {
                skipped++;
                continue;
            }
            if (current.Any(p => p.Name == preset.Name))
            {
                int counter = 1;
                while (current.Any(p => p.Name == $"{preset.Name}_{counter}")) counter++;
                preset.Name = $"{preset.Name}_{counter}";
            }
            current.Add(preset);
            added++;
        }
        return new ImportResult(added, skipped, 0);
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
