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
}
