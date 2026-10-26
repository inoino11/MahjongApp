using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// 場代精算。期待値は手計算
public class SettlementCalculatorTests
{
    private static readonly Dictionary<string, string> Names = new() { ["A"] = "あ", ["B"] = "い", ["C"] = "う", ["D"] = "え", ["E"] = "お" };

    private static SavedGameRecord Game(string sessionId, string?[] ids, double[] points, int?[]? raws = null) => new()
    {
        SessionId = sessionId,
        PlayerIds = ids.ToList(),
        Points = points.ToList(),
        Yen = points.Select(p => (int)Math.Round(p * 50)).ToList(), // レート50
        RawScores = (raws ?? ids.Select(_ => (int?)250).ToArray()).ToList(),
    };

    // セッション s1（チップ100/枚）: 2局。チップ A+3 B-2 C+1 D-2
    // 局1: A +55.2 / B +8.1 / C -22.5 / D -40.8
    // 局2: A -40.8 / B +55.2 / C +8.1 / D -22.5
    // 収支: A 2760-2040+300=1020 / B 405+2760-200=2965 / C -1125+405+100=-620 / D -2040-1125-200=-3365
    private static SettlementSessionGroup Session1() => new()
    {
        Session = new SavedSessionRecord
        {
            SessionId = "s1",
            Rules = new RuleSet { ChipRate = 100 },
            ParticipantPlayerIds = new() { "A", "B", "C", "D" },
            SettledChips = new() { 3, -2, 1, -2 },
        },
        Games = new()
        {
            Game("s1", new[] { "A", "B", "C", "D" }, new[] { 55.2, 8.1, -22.5, -40.8 }),
            Game("s1", new[] { "A", "B", "C", "D" }, new[] { -40.8, 55.2, 8.1, -22.5 }),
        }
    };

    private static Dictionary<string, SettlementResult> ById(List<SettlementResult> results) => results.ToDictionary(r => r.Id);

    [Fact]
    public void 局の収支とチップを合計する()
    {
        var r = ById(SettlementCalculator.Calculate(new[] { Session1() }, 0, FeeRule.SplitEvenly, Names));
        Assert.Equal(1020, r["A"].TotalMahjongYen);
        Assert.Equal(2965, r["B"].TotalMahjongYen);
        Assert.Equal(-620, r["C"].TotalMahjongYen);
        Assert.Equal(-3365, r["D"].TotalMahjongYen);
        Assert.Equal(14.4, r["A"].TotalPt, 9);
        Assert.Equal(2, r["A"].GamesPlayed);
        Assert.Equal("あ", r["A"].Name);
    }

    [Fact]
    public void 同額払いと最終収支の順()
    {
        // 場代 4000 を4人で 1000 ずつ
        var results = SettlementCalculator.Calculate(new[] { Session1() }, 4000, FeeRule.SplitEvenly, Names);
        Assert.All(results, p => Assert.Equal(1000, p.Fee, 9));
        Assert.Equal(new[] { "B", "A", "C", "D" }, results.Select(p => p.Id));
        Assert.Equal(1965, results[0].FinalAmount, 9);
        // 全員の最終収支の合計は、場代の分だけマイナス
        Assert.Equal(-4000, results.Sum(p => p.FinalAmount), 6);
    }

    [Fact]
    public void 総額トップ払い()
    {
        // 合計ポイントのトップは B（+63.3）
        var r = ById(SettlementCalculator.Calculate(new[] { Session1() }, 4000, FeeRule.TopPays, Names));
        Assert.Equal(4000, r["B"].Fee, 9);
        Assert.Equal(0, r["A"].Fee, 9);
    }

    [Fact]
    public void 総額トップ払い_小数の誤差で同点を取りこぼさない()
    {
        // A は 0.1 + 0.2（= 0.30000000000000004）、B は 0.3。表示上は同点なので折半
        var group = new SettlementSessionGroup
        {
            Session = new SavedSessionRecord { SessionId = "t", Rules = new RuleSet(), ParticipantPlayerIds = new() { "A", "B", "C" } },
            Games = new()
            {
                Game("t", new[] { "A", "B", "C" }, new[] { 0.1, 0.0, -0.1 }),
                Game("t", new[] { "A", "B", "C" }, new[] { 0.2, 0.3, -0.5 }),
            }
        };
        var r = ById(SettlementCalculator.Calculate(new[] { group }, 1000, FeeRule.TopPays, Names));
        Assert.Equal(500, r["A"].Fee, 9);
        Assert.Equal(500, r["B"].Fee, 9);
    }

    [Fact]
    public void 各戦トップ払い()
    {
        // 局1のトップ A、局2のトップ B が、4000 ÷ 2局 = 2000 ずつ
        var r = ById(SettlementCalculator.Calculate(new[] { Session1() }, 4000, FeeRule.TopPerGame, Names));
        Assert.Equal(2000, r["A"].Fee, 9);
        Assert.Equal(2000, r["B"].Fee, 9);
        Assert.Equal(0, r["C"].Fee, 9);
    }

    [Fact]
    public void 各戦トップ払い_同点トップは分け合う()
    {
        // 1局だけで A と B が同点トップ: 3000 を 1500 ずつ
        var group = new SettlementSessionGroup
        {
            Session = new SavedSessionRecord { SessionId = "t", Rules = new RuleSet(), ParticipantPlayerIds = new() { "A", "B", "C", "D" } },
            Games = new() { Game("t", new[] { "A", "B", "C", "D" }, new[] { 25.0, 25.0, -15.0, -35.0 }) }
        };
        var r = ById(SettlementCalculator.Calculate(new[] { group }, 3000, FeeRule.TopPerGame, Names));
        Assert.Equal(1500, r["A"].Fee, 9);
        Assert.Equal(1500, r["B"].Fee, 9);
    }

    [Fact]
    public void 対戦数比払い_抜け番の局は数えない()
    {
        // 5人で3局、毎局1人が抜け番: A 3局 / B 3局 / C 2局 / D 2局 / E 2局 = 12局
        // 場代 1200 → 1局あたり 100
        var ids = new[] { "A", "B", "C", "D", "E" };
        var group = new SettlementSessionGroup
        {
            Session = new SavedSessionRecord { SessionId = "t", Rules = new RuleSet(), ParticipantPlayerIds = ids.ToList() },
            Games = new()
            {
                Game("t", ids, new[] { 30.0, 10.0, -10.0, -30.0, 0 }, new int?[] { 400, 300, 200, 100, null }),
                Game("t", ids, new[] { 30.0, 10.0, -10.0, 0, -30.0 }, new int?[] { 400, 300, 200, null, 100 }),
                Game("t", ids, new[] { 30.0, 10.0, 0, -10.0, -30.0 }, new int?[] { 400, 300, null, 200, 100 }),
            }
        };
        var r = ById(SettlementCalculator.Calculate(new[] { group }, 1200, FeeRule.SplitByGames, Names));
        Assert.Equal(300, r["A"].Fee, 9);
        Assert.Equal(300, r["B"].Fee, 9);
        Assert.Equal(200, r["C"].Fee, 9);
        Assert.Equal(200, r["E"].Fee, 9);
        Assert.Equal(2, r["E"].GamesPlayed);
    }

    [Fact]
    public void 複数セッションはそれぞれのチップレートで計算する()
    {
        // s2（チップ50/枚）で C +2 / E -2 を追加
        var s2 = new SettlementSessionGroup
        {
            Session = new SavedSessionRecord { SessionId = "s2", Rules = new RuleSet { ChipRate = 50 }, ParticipantPlayerIds = new() { "C", "E" }, SettledChips = new() { 2, -2 } },
            Games = new() { Game("s2", new[] { "A", "B", "C", "E" }, new[] { 30.0, 10.0, -10.0, -30.0 }) }
        };
        var r = ById(SettlementCalculator.Calculate(new[] { Session1(), s2 }, 0, FeeRule.SplitEvenly, Names));
        // C: s1 -620、s2 は局 -500 + チップ 100 = -400
        Assert.Equal(-620, r["C"].SessionYen["s1"]);
        Assert.Equal(-400, r["C"].SessionYen["s2"]);
        Assert.Equal(-1020, r["C"].TotalMahjongYen);
        // E: 局 -1500 + チップ -100
        Assert.Equal(-1600, r["E"].TotalMahjongYen);
    }

    [Fact]
    public void 名簿にない人は不明と表示する()
    {
        var group = Session1();
        group.Session.ParticipantPlayerIds[3] = "X";
        group.Games.ForEach(g => g.PlayerIds[3] = "X");
        var r = ById(SettlementCalculator.Calculate(new[] { group }, 0, FeeRule.SplitEvenly, Names));
        Assert.Equal("不明", r["X"].Name);
    }

    [Fact]
    public void セッションを選んでいなければ結果は空()
    {
        Assert.Empty(SettlementCalculator.Calculate(Array.Empty<SettlementSessionGroup>(), 1000, FeeRule.SplitEvenly, Names));
    }

    [Fact]
    public void 同じ人の参加枠はチップを合算して1つにまとめる()
    {
        var session = new SavedSessionRecord
        {
            SessionId = "s",
            Rate = 50,
            ChipRate = 100,
            Rules = new RuleSet { Name = "r" },
            ParticipantPlayerIds = new() { "A", "B", "A", "C" },
            SettledChips = new() { 2, -1, 1, -2 },
        };
        var merged = SettlementCalculator.MergeDuplicateParticipants(session);
        Assert.Equal(new[] { "A", "B", "C" }, merged.ParticipantPlayerIds);
        Assert.Equal(new[] { 3, -1, -2 }, merged.SettledChips);
        Assert.Equal((50, 100, "r"), (merged.Rate, merged.ChipRate, merged.Rules!.Name));
    }
}
