using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// 通常のウマ（2←3 / 1←4 の差）と、計算に使う順位ごとのウマの対応
public class RuleSetTests
{
    [Fact]
    public void 四麻のウマ10_20は順位ごとに20_10_m10_m20()
    {
        var rules = new RuleSet { PlayerCount = 4, Uma1 = 10, Uma2 = 30 };
        rules.SyncCustomUmaFromNormal();
        Assert.Equal((30, 10, -10, -30), (rules.CustomUma4_1, rules.CustomUma4_2, rules.CustomUma4_3, rules.CustomUma4_4));
    }

    [Fact]
    public void 三麻は2位と3位の両方から1位が受け取る()
    {
        // ウマ10-20: 1位 +30 / 2位 -10 / 3位 -20
        var rules = new RuleSet { PlayerCount = 3, Uma1 = 10, Uma2 = 20 };
        rules.SyncCustomUmaFromNormal();
        Assert.Equal((30, -10, -20), (rules.CustomUma3_1, rules.CustomUma3_2, rules.CustomUma3_3));
    }

    [Fact]
    public void 個別設定中は手入力のウマを上書きしない()
    {
        var rules = new RuleSet { IsCustomUma = true, CustomUma4_1 = 40, CustomUma4_2 = 5, CustomUma4_3 = -15, CustomUma4_4 = -30, Uma1 = 10, Uma2 = 20 };
        rules.SyncCustomUmaFromNormal();
        Assert.Equal((40, 5, -15, -30), (rules.CustomUma4_1, rules.CustomUma4_2, rules.CustomUma4_3, rules.CustomUma4_4));
    }

    [Fact]
    public void 個別設定を解除したら通常のウマで計算される()
    {
        // 個別ウマ +30/+10/-10/-30 で計算していたルールを、画面の × で解除した状態
        var rules = new RuleSet { IsCustomUma = true, CustomUma4_1 = 30, CustomUma4_2 = 10, CustomUma4_3 = -10, CustomUma4_4 = -30, Uma1 = 10, Uma2 = 20 };
        rules.DisableCustomUma();

        // 画面に出ているウマ10-20で計算されること: 1着 45200 は +15.2 +20(オカ) +20 = +55.2
        var result = new MahjongCalculatorService().Calculate(rules, new() { 45200, 28100, 17500, 9200 }, new() { false, false, false, false }, new() { false, false, false, false }, new() { [0] = 0, [1] = 1, [2] = 2, [3] = 3 });
        Assert.False(rules.IsCustomUma);
        Assert.Equal(55.2, result.Points[0], 9);
        Assert.Equal(-40.8, result.Points[3], 9);
    }
}
