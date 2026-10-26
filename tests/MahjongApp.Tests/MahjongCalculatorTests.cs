using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// 1局分の計算。期待値は手計算で求め、計算の過程をコメントに書く
public class MahjongCalculatorTests
{
    private readonly MahjongCalculatorService _calc = new();

    // 既定のルール: 四麻 25000持ち30000返し、ウマ10-20（+20/+10/-10/-20）、レート50、上家取り
    private static RuleSet FourPlayer() => new();

    private static RuleSet ThreePlayer() => new()
    {
        PlayerCount = 3,
        StartingPoints = 35000,
        ReturnPoints = 40000,
    };

    private CalculationResult Calc(RuleSet rules, int[] scores, bool[]? yaki = null, bool[]? tobiBonus = null, int[]? seatOrder = null)
    {
        int n = scores.Length;
        // seatOrder は同点時の順位（値が小さいほど上位）。省略時は入力順
        var priority = Enumerable.Range(0, n).ToDictionary(i => i, i => seatOrder?[i] ?? i);
        return _calc.Calculate(rules, scores.ToList(), (yaki ?? new bool[n]).ToList(), (tobiBonus ?? new bool[n]).ToList(), priority);
    }

    private static void AssertPoints(double[] expected, IList<double> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-9, $"[{i}] 期待 {expected[i]} / 実際 {actual[i]}");
    }

    // --- 基本形 ---

    [Fact]
    public void 四麻の基本形()
    {
        // オカ = (30000-25000)×4/1000 = 20 を1着へ
        // 1着 45200: +15.2 +20(オカ) +20(ウマ) = +55.2
        // 2着 28100: -1.9 +10 = +8.1
        // 3着 17500: -12.5 -10 = -22.5
        // 4着  9200: -20.8 -20 = -40.8
        var r = Calc(FourPlayer(), new[] { 45200, 28100, 17500, 9200 });
        AssertPoints(new[] { 55.2, 8.1, -22.5, -40.8 }, r.Points);
        // 収支 = ポイント × レート50
        Assert.Equal(new[] { 2760, 405, -1125, -2040 }, r.Yen);
    }

    [Fact]
    public void 席の並びが違っても同じ人には同じ結果()
    {
        var r = Calc(FourPlayer(), new[] { 17500, 45200, 9200, 28100 });
        AssertPoints(new[] { -22.5, 55.2, -40.8, 8.1 }, r.Points);
    }

    [Fact]
    public void 三麻の基本形()
    {
        // オカ = (40000-35000)×3/1000 = 15、ウマ10-20 は三麻で +30/-10/-20
        // 1着 52000: +12 +15 +30 = +57
        // 2着 33000: -7 -10 = -17
        // 3着 20000: -20 -20 = -40
        var r = Calc(ThreePlayer(), new[] { 52000, 33000, 20000 });
        AssertPoints(new[] { 57.0, -17.0, -40.0 }, r.Points);
        Assert.Equal(new[] { 2850, -850, -2000 }, r.Yen);
    }

    [Fact]
    public void オカなし()
    {
        // 30000持ち30000返し: オカ 0
        // +10+20 / +2+10 / -2-10 / -10-20
        var rules = new RuleSet { StartingPoints = 30000, ReturnPoints = 30000 };
        var r = Calc(rules, new[] { 40000, 32000, 28000, 20000 });
        AssertPoints(new[] { 30.0, 12.0, -12.0, -30.0 }, r.Points);
    }

    [Fact]
    public void カスタムウマ()
    {
        // ウマ +30/+10/-10/-30（Mリーグ型）
        // 1着 45200: +15.2 +20 +30 = +65.2 / 4着 9200: -20.8 -30 = -50.8
        var rules = new RuleSet { IsCustomUma = true, CustomUma4_1 = 30, CustomUma4_2 = 10, CustomUma4_3 = -10, CustomUma4_4 = -30 };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 });
        AssertPoints(new[] { 65.2, 8.1, -22.5, -50.8 }, r.Points);
    }

    [Fact]
    public void レート0なら収支は0()
    {
        var rules = new RuleSet { Rate = 0 };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 });
        Assert.Equal(new[] { 0, 0, 0, 0 }, r.Yen);
    }

    // --- 同点 ---

    [Fact]
    public void 上家取りは席順で上の人が上位()
    {
        // 30000 が2人。席順で上（seatOrder の値が小さい）の人が1着
        // 1着: 0 +20 +20 = +40 / 2着: 0 +10 = +10 / 3着 25000: -5 -10 = -15 / 4着 15000: -15 -20 = -35
        var scores = new[] { 30000, 30000, 25000, 15000 };
        var r1 = Calc(FourPlayer(), scores);
        AssertPoints(new[] { 40.0, 10.0, -15.0, -35.0 }, r1.Points);
        var r2 = Calc(FourPlayer(), scores, seatOrder: new[] { 1, 0, 2, 3 });
        AssertPoints(new[] { 10.0, 40.0, -15.0, -35.0 }, r2.Points);
    }

    [Fact]
    public void ポイント折半は同点者で順位点を分ける()
    {
        // 1着・2着の順位点 (20+20) + 10 = 50 を2人で 25 ずつ
        var rules = new RuleSet { TieBreaker = TieBreakerRule.Split };
        var r = Calc(rules, new[] { 30000, 30000, 25000, 15000 });
        AssertPoints(new[] { 25.0, 25.0, -15.0, -35.0 }, r.Points);
    }

    [Fact]
    public void ポイント折半で割り切れない端数は上位から配る()
    {
        // 3人同点: (40 + 10 - 10) / 3 = 13.33… → 13.3 ずつ、残り 0.1 は席順で上の人へ
        // 4着 10000: -20 -20 = -40
        var rules = new RuleSet { TieBreaker = TieBreakerRule.Split };
        var r = Calc(rules, new[] { 30000, 30000, 30000, 10000 });
        AssertPoints(new[] { 13.4, 13.3, 13.3, -40.0 }, r.Points);
        Assert.Equal(new[] { 670, 665, 665, -2000 }, r.Yen);
    }

    // --- 席順の入力が必要かの判定 ---

    [Fact]
    public void 同点がなければ席順は不要()
    {
        Assert.False(_calc.CheckIfTieBreakerNeeded(FourPlayer(), new() { 45200, 28100, 17500, 9200 }, new() { false, false, false, false }, new() { false, false, false, false }));
    }

    [Fact]
    public void 上家取りで同点なら席順が必要()
    {
        Assert.True(_calc.CheckIfTieBreakerNeeded(FourPlayer(), new() { 30000, 30000, 25000, 15000 }, new() { false, false, false, false }, new() { false, false, false, false }));
    }

    [Fact]
    public void 折半で結果が変わらない同点なら席順は不要()
    {
        var rules = new RuleSet { TieBreaker = TieBreakerRule.Split };
        Assert.False(_calc.CheckIfTieBreakerNeeded(rules, new() { 30000, 30000, 25000, 15000 }, new() { false, false, false, false }, new() { false, false, false, false }));
    }

    [Fact]
    public void 折半でも端数の行き先が変わる同点なら席順が必要()
    {
        // 3人同点で 0.1 の端数を誰が受け取るかが席順で変わる
        var rules = new RuleSet { TieBreaker = TieBreakerRule.Split };
        Assert.True(_calc.CheckIfTieBreakerNeeded(rules, new() { 30000, 30000, 30000, 10000 }, new() { false, false, false, false }, new() { false, false, false, false }));
    }

    [Fact]
    public void 折半でも焼き鳥の端数の行き先が変わる同点なら席順が必要()
    {
        // 4着の焼き鳥 10pt を3人で分けると 3.4/3.3/3.3。同点の2人のどちらが 3.4 かが席順で変わる
        var rules = new RuleSet { TieBreaker = TieBreakerRule.Split, IsYakiEnabled = true };
        Assert.True(_calc.CheckIfTieBreakerNeeded(rules, new() { 30000, 30000, 25000, 15000 }, new() { false, false, false, true }, new() { false, false, false, false }));
    }

    // --- 収支の端数 ---

    [Fact]
    public void 収支の端数は端数の大きい人から配って合計を0にする()
    {
        // レート15: 55.2→828 / 8.1→121.5 / -22.5→-337.5 / -40.8→-612
        // 切り捨てると 828 + 121 - 338 - 612 = -1。端数 0.5 が2人並ぶので、順位が上の2着に 1 を配る
        var rules = new RuleSet { Rate = 15 };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 });
        Assert.Equal(new[] { 828, 122, -338, -612 }, r.Yen);
    }

    // --- 沈みウマ ---

    [Fact]
    public void 沈みウマ_1着総取り()
    {
        // 25000未満の2人が10ずつ払い、1着が20を受け取る
        var rules = new RuleSet { IsSinkEnabled = true };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 });
        AssertPoints(new[] { 75.2, 8.1, -32.5, -50.8 }, r.Points);
        AssertPoints(new[] { 20.0, 0, -10.0, -10.0 }, r.SinkPoints);
    }

    [Fact]
    public void 沈みウマ_浮きの人で分配()
    {
        var rules = new RuleSet { IsSinkEnabled = true, SinkUmaDistribution = PenaltyDistribution.Split };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 });
        AssertPoints(new[] { 65.2, 18.1, -32.5, -50.8 }, r.Points);
    }

    [Fact]
    public void 沈みウマ_ちょうど持ち点を浮きとするか()
    {
        // 基本: +50 / +5 / -20 / -35（40000/25000/20000/15000）
        var scores = new[] { 40000, 25000, 20000, 15000 };
        var uki = Calc(new RuleSet { IsSinkEnabled = true, SinkJustIsUki = true }, scores);
        AssertPoints(new[] { 70.0, 5.0, -30.0, -45.0 }, uki.Points);
        var shizumi = Calc(new RuleSet { IsSinkEnabled = true, SinkJustIsUki = false }, scores);
        AssertPoints(new[] { 80.0, -5.0, -30.0, -45.0 }, shizumi.Points);
    }

    [Fact]
    public void 沈みウマ_基準点を変える()
    {
        // 基準 30000: 浮きは 45200 だけ。3人が10ずつ払う
        var rules = new RuleSet { IsSinkEnabled = true, IsSinkBaseCustom = true, SinkUmaBasePoint = 30000 };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 });
        AssertPoints(new[] { 85.2, -1.9, -32.5, -50.8 }, r.Points);
    }

    // --- 焼き鳥 ---

    [Fact]
    public void 焼き鳥_焼き鳥でない人で分配し端数は上位へ()
    {
        // 4着が焼き鳥: 10 を3人で 3.4/3.3/3.3
        var rules = new RuleSet { IsYakiEnabled = true };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 }, yaki: new[] { false, false, false, true });
        AssertPoints(new[] { 58.6, 11.4, -19.2, -50.8 }, r.Points);
        AssertPoints(new[] { 3.4, 3.3, 3.3, -10.0 }, r.YakiPoints);
    }

    [Fact]
    public void 焼き鳥_1着総取り()
    {
        var rules = new RuleSet { IsYakiEnabled = true, YakiDistribution = PenaltyDistribution.WinnerTakeAll };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 }, yaki: new[] { false, false, true, true });
        AssertPoints(new[] { 75.2, 8.1, -32.5, -50.8 }, r.Points);
    }

    [Fact]
    public void 焼き鳥_全員が焼き鳥なら発生しない()
    {
        var rules = new RuleSet { IsYakiEnabled = true };
        var r = Calc(rules, new[] { 45200, 28100, 17500, 9200 }, yaki: new[] { true, true, true, true });
        AssertPoints(new[] { 55.2, 8.1, -22.5, -40.8 }, r.Points);
    }

    // --- トビ賞 ---

    [Fact]
    public void トビ賞_トバした人が受け取る()
    {
        // 基本: +62 / +10 / -21.5 / -50.5（52000/30000/18500/-500）
        var rules = new RuleSet { IsTobiEnabled = true };
        var r = Calc(rules, new[] { 52000, 30000, 18500, -500 }, tobiBonus: new[] { true, false, false, false });
        AssertPoints(new[] { 72.0, 10.0, -21.5, -60.5 }, r.Points);
        Assert.Equal(new[] { false, false, false, true }, r.TobiPlayers);
    }

    [Fact]
    public void トビ賞_受け取る人が2人なら分ける()
    {
        var rules = new RuleSet { IsTobiEnabled = true };
        var r = Calc(rules, new[] { 52000, 30000, 18500, -500 }, tobiBonus: new[] { true, true, false, false });
        AssertPoints(new[] { 67.0, 15.0, -21.5, -60.5 }, r.Points);
    }

    [Fact]
    public void トビ賞_2人トビ()
    {
        // 基本: 60000→+70 / 40000→+20 / 0→-40 / 0→-50（同点は席順）
        var rules = new RuleSet { IsTobiEnabled = true };
        var r = Calc(rules, new[] { 60000, 40000, 0, 0 }, tobiBonus: new[] { true, false, false, false });
        AssertPoints(new[] { 90.0, 20.0, -50.0, -60.0 }, r.Points);
    }

    [Fact]
    public void トビ賞_ちょうど0点をトビとしないなら発生しない()
    {
        var rules = new RuleSet { IsTobiEnabled = true, TobiJustIsTobi = false };
        var r = Calc(rules, new[] { 55000, 30000, 15000, 0 }, tobiBonus: new[] { true, false, false, false });
        // 基本: +65 / +10 / -25 / -50
        AssertPoints(new[] { 65.0, 10.0, -25.0, -50.0 }, r.Points);
        Assert.Equal(new[] { false, false, false, false }, r.TobiPlayers);
    }

    // --- コールド賞 ---

    [Fact]
    public void コールド賞()
    {
        // 基本: 60000→+70 / 20000→0 / 15000→-25 / 5000→-45
        // 60000以上の1人が、他の3人から10ずつ受け取る
        var rules = new RuleSet { IsColdEnabled = true };
        var r = Calc(rules, new[] { 60000, 20000, 15000, 5000 });
        AssertPoints(new[] { 100.0, -10.0, -35.0, -55.0 }, r.Points);
        AssertPoints(new[] { 30.0, -10.0, -10.0, -10.0 }, r.ColdPoints);
    }

    [Fact]
    public void コールド賞_ちょうどを含めないなら発生しない()
    {
        var rules = new RuleSet { IsColdEnabled = true, ColdJustIsCold = false };
        var r = Calc(rules, new[] { 60000, 20000, 15000, 5000 });
        AssertPoints(new[] { 70.0, 0.0, -25.0, -45.0 }, r.Points);
    }

    // --- 丸め ---

    [Fact]
    public void 百点単位の素点では丸めで値が変わらない()
    {
        foreach (int returnPoints in new[] { 30000, 40000, 25000 })
            for (int score = -50000; score <= 100000; score += 100)
            {
                double raw = (score - returnPoints) / 1000.0;
                Assert.Equal(Math.Round(raw, 1), _calc.RoundToTenth(raw), 9);
            }
    }

    [Theory]
    [InlineData(1.25, 1.2)]
    [InlineData(1.26, 1.3)]
    [InlineData(1.24, 1.2)]
    public void 丸めは五捨六入(double value, double expected)
    {
        Assert.Equal(expected, _calc.RoundToTenth(value), 9);
    }

    // --- どの組み合わせでも成り立つこと ---

    [Fact]
    public void どの組み合わせでもポイントと収支の合計は0で順位どおりに並ぶ()
    {
        var random = new Random(20261026);
        int[] rates = { 0, 10, 15, 30, 50, 100 };
        for (int n = 0; n < 2000; n++)
        {
            bool sanma = random.Next(2) == 0;
            int count = sanma ? 3 : 4;
            int start = sanma ? 35000 : 25000;
            var rules = new RuleSet
            {
                PlayerCount = count,
                StartingPoints = start,
                ReturnPoints = start + random.Next(0, 3) * 5000,
                Uma1 = random.Next(0, 3) * 5,
                Rate = rates[random.Next(rates.Length)],
                TieBreaker = random.Next(2) == 0 ? TieBreakerRule.Kamicha : TieBreakerRule.Split,
                IsSinkEnabled = random.Next(3) == 0,
                SinkUmaDistribution = random.Next(2) == 0 ? PenaltyDistribution.WinnerTakeAll : PenaltyDistribution.Split,
                IsYakiEnabled = random.Next(3) == 0,
                YakiDistribution = random.Next(2) == 0 ? PenaltyDistribution.WinnerTakeAll : PenaltyDistribution.Split,
                IsTobiEnabled = random.Next(3) == 0,
                IsColdEnabled = random.Next(3) == 0,
            };
            // 順位ウマ（上位ほど大きい）
            int uma1 = random.Next(0, 3) * 5, uma2 = uma1 + random.Next(0, 3) * 5;
            (rules.CustomUma4_1, rules.CustomUma4_2, rules.CustomUma4_3, rules.CustomUma4_4) = (uma2, uma1, -uma1, -uma2);
            (rules.CustomUma3_1, rules.CustomUma3_2, rules.CustomUma3_3) = (uma1 + uma2, -uma1, -uma2);

            // 百点単位で、合計が持ち点×人数になる素点。半分は同点が出やすいよう 5000 点刻みにする
            var scores = new int[count];
            int remaining = start * count;
            for (int i = 0; i < count - 1; i++)
            {
                scores[i] = random.Next(2) == 0 ? random.Next(-200, 1000) * 100 : random.Next(-2, 12) * 5000;
                remaining -= scores[i];
            }
            scores[count - 1] = remaining;

            var yaki = Enumerable.Range(0, count).Select(_ => random.Next(4) == 0).ToArray();
            bool anyTobi = scores.Any(s => s <= rules.TobiPoint);
            var tobiBonus = Enumerable.Range(0, count).Select(i => anyTobi && scores[i] > rules.TobiPoint && random.Next(2) == 0).ToArray();
            var seatOrder = Enumerable.Range(0, count).OrderBy(_ => random.Next()).ToArray();

            var r = Calc(rules, scores, yaki, tobiBonus, seatOrder);
            string label = $"case {n}: [{string.Join(",", scores)}]";
            Assert.True(Math.Abs(r.Points.Sum()) < 0.05, $"{label} ポイント合計 {r.Points.Sum()}");
            Assert.True(r.Yen.Sum() == 0, $"{label} 収支合計 {r.Yen.Sum()}");

            // 特殊ルール分を除いたポイントは、素点の多い人ほど下がらない
            var basePoints = Enumerable.Range(0, count).Select(i => r.Points[i] - r.SinkPoints[i] - r.YakiPoints[i] - r.TobiPoints[i] - r.ColdPoints[i]).ToArray();
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    if (scores[i] > scores[j])
                        Assert.True(basePoints[i] >= basePoints[j] - 1e-9, $"{label} 素点 {scores[i]} の {basePoints[i]} < 素点 {scores[j]} の {basePoints[j]}");
        }
    }
}
