using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// ルールプリセットの保存
public class RulePresetTests
{
    private static List<RuleSet> Presets() => new()
    {
        new() { Name = "四麻" },
        new() { Name = "三麻", PlayerCount = 3 },
        new() { Name = "Mリーグ", Uma2 = 30 },
    };

    [Fact]
    public void 同じ名前は元の位置のまま差し替える()
    {
        var presets = Presets();
        RulePresetService.Upsert(presets, new RuleSet { Name = "四麻", Rate = 100 });
        Assert.Equal(new[] { "四麻", "三麻", "Mリーグ" }, presets.Select(p => p.Name));
        Assert.Equal(100, presets[0].Rate);
    }

    [Fact]
    public void 新しい名前は末尾に加える()
    {
        var presets = Presets();
        RulePresetService.Upsert(presets, new RuleSet { Name = "四麻_1" });
        Assert.Equal(new[] { "四麻", "三麻", "Mリーグ", "四麻_1" }, presets.Select(p => p.Name));
    }

    // --- バックアップからの取り込み ---

    [Fact]
    public void 統合で同じ名前と中身のルールは複製しない()
    {
        var current = Presets();
        var result = RulePresetService.Merge(current, Presets());
        Assert.Equal((0, 3), (result.Added, result.Skipped));
        Assert.Equal(3, current.Count);
    }

    [Fact]
    public void 統合で中身が違えば番号を付けて加える()
    {
        var current = Presets();
        var result = RulePresetService.Merge(current, new[] { new RuleSet { Name = "四麻", Memo = "赤3枚" } });
        Assert.Equal(1, result.Added);
        Assert.Equal("四麻_1", current.Last().Name);
    }

    [Fact]
    public void 同じバックアップを何度統合しても増えない()
    {
        // 1回目で「四麻_1」として加わったものは、2回目には同じ中身があるので加えない
        var current = Presets();
        var backup = new[] { new RuleSet { Name = "四麻", Memo = "赤3枚" } };
        RulePresetService.Merge(current, backup.Select(p => p.DeepClone()));
        var second = RulePresetService.Merge(current, backup.Select(p => p.DeepClone()));
        Assert.Equal((0, 1), (second.Added, second.Skipped));
        Assert.Equal(new[] { "四麻", "三麻", "Mリーグ", "四麻_1" }, current.Select(p => p.Name));
    }

    [Fact]
    public void 番号付きがすでにあれば次の番号にする()
    {
        var current = new List<RuleSet> { new() { Name = "四麻" }, new() { Name = "四麻_1", Rate = 100 } };
        RulePresetService.Merge(current, new[] { new RuleSet { Name = "四麻", Rate = 30 } });
        Assert.Equal("四麻_2", current.Last().Name);
    }

    [Fact]
    public void 順位ごとのウマがずれた古いプリセットも同じ中身として扱う()
    {
        // 初期の Mリーグルールは、順位ごとのウマが既定値（+20/+10/-10/-20）のまま保存されていることがある
        var old = new RuleSet { Name = "Mリーグルール", Uma1 = 10, Uma2 = 30 };
        var synced = old.DeepClone();
        synced.SyncCustomUmaFromNormal();
        var current = new List<RuleSet> { synced };
        var result = RulePresetService.Merge(current, new[] { old });
        Assert.Equal((0, 1), (result.Added, result.Skipped));
    }

    [Fact]
    public void ファイル内の同じルールの重なりは1件にまとめる()
    {
        var presets = RulePresetService.Distinct(new[] { new RuleSet { Name = "四麻" }, new RuleSet { Name = "四麻" }, new RuleSet { Name = "四麻", Rate = 100 } });
        Assert.Equal(2, presets.Count);
    }

    [Fact]
    public void バックアップからルールの部分だけを読み出す()
    {
        // 成績側に今の型と合わない値（文字列の素点）があっても、ルールは読める。壊れた1件は数えて飛ばす
        string json = """
        {
          "players": [{ "id": "p1", "name": "あ" }],
          "games": [{ "id": "g1", "sessionId": "s1", "rawScores": ["abc"] }],
          "rulePresets": [
            { "name": "四麻", "playerCount": 4, "uma2": 20 },
            { "name": "壊れた", "playerCount": "four" },
            { "name": "三麻", "playerCount": 3 }
          ]
        }
        """;
        var (presets, unreadable) = RulePresetService.ParsePresets(json);
        Assert.Equal(new[] { "四麻", "三麻" }, presets.Select(p => p.Name));
        Assert.Equal(3, presets[1].PlayerCount);
        Assert.Equal(1, unreadable);
    }

    [Fact]
    public void ルールを含まない古いバックアップは0件()
    {
        var (presets, unreadable) = RulePresetService.ParsePresets("""{ "players": [], "sessions": [], "games": [] }""");
        Assert.Empty(presets);
        Assert.Equal(0, unreadable);
    }

    [Fact]
    public void 書き出したバックアップのルールをそのまま読み戻せる()
    {
        // DatabaseService.BuildBackupJsonAsync と同じ形式（camelCase）で書き出したもの
        var data = new AllDbData { RulePresets = Presets() };
        string json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        var (presets, unreadable) = RulePresetService.ParsePresets(json);
        Assert.Equal(0, unreadable);
        Assert.Equal(Presets().Select(p => p.ToComparableJson()), presets.Select(p => p.ToComparableJson()));
    }
}
