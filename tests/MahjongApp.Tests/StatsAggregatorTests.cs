using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// 通算成績（ランキング・相性）の集計。期待値は手計算
public class StatsAggregatorTests
{
    private static SavedGameRecord Game(string sessionId, int playerCount, string?[] ids, int?[] raws, double[] points) => new()
    {
        SessionId = sessionId,
        PlayerCount = playerCount,
        PlayerIds = ids.ToList(),
        RawScores = raws.ToList(),
        Points = points.ToList(),
        Yen = points.Select(p => (int)Math.Round(p * 50)).ToList(), // レート50
    };

    private static readonly List<PlayerProfile> Players = new[] { "A", "B", "C", "D", "E", "deleted" }
        .Select(id => new PlayerProfile { Id = id, Name = id == "deleted" ? "削除済み" : id.ToLower() }).ToList();

    // 四麻 s1（チップ100/枚）:
    //   局1: A 45200 / B 28100 / C 17500 / D 9200 → +55.2 / +8.1 / -22.5 / -40.8
    //   局2: A,B が 30000 で同点、席順で B が1着 → A +10 / B +40 / C -15 / D -35
    //   局3: 5人目の E は抜け番。2人目は名簿から削除された人 → A +30 / 削除済み +10 / C -10 / D -30
    //   チップ: A +1 / C -1
    // 三麻 s2（チップ50/枚）:
    //   局4: A 52000 / B 33000 / E 20000 → +57 / -17 / -40。チップ: A +2 / B -1 / E -1
    private static readonly List<SavedGameRecord> Games = new()
    {
        Game("s1", 4, new[] { "A", "B", "C", "D" }, new int?[] { 452, 281, 175, 92 }, new[] { 55.2, 8.1, -22.5, -40.8 }),
        Game("s1", 4, new[] { "A", "B", "C", "D" }, new int?[] { 300, 300, 250, 150 }, new[] { 10.0, 40.0, -15.0, -35.0 }),
        Game("s1", 4, new[] { "A", "deleted", "C", "D", "E" }, new int?[] { 400, 300, 200, 100, null }, new[] { 30.0, 10.0, -10.0, -30.0, 0 }),
        Game("s2", 3, new[] { "A", "B", "E" }, new int?[] { 520, 330, 200 }, new[] { 57.0, -17.0, -40.0 }),
    };

    private static readonly List<SavedSessionRecord> Sessions = new()
    {
        new() { SessionId = "s1", ChipRate = 100, ParticipantPlayerIds = new() { "A", "B", "C", "D" }, SettledChips = new() { 1, 0, -1, 0 } },
        new() { SessionId = "s2", ChipRate = 50, ParticipantPlayerIds = new() { "A", "B", "E" }, SettledChips = new() { 2, -1, -1 } },
    };

    [Fact]
    public void 四麻の順位とポイントと収支()
    {
        var (stats4, _) = StatsAggregator.Aggregate(Players, Games, Sessions);
        var a = stats4.Single(s => s.Id == "A");
        // 局1 1着、局2 2着（同点だが席順で B が上）、局3 1着
        Assert.Equal(3, a.TotalGames);
        Assert.Equal(new[] { 2, 1, 0, 0 }, a.RankCounts);
        Assert.Equal(95.2, a.TotalPoint, 9);
        // 収支 2760 + 500 + 1500 = 4760、チップ +1 × 100
        Assert.Equal(4760, a.TotalGameYen);
        Assert.Equal(100, a.TotalChipYen);
        Assert.Equal(4.0 / 3, a.AverageRank, 9);

        var c = stats4.Single(s => s.Id == "C");
        // 局3では削除済みの人も順位に数えるので、C は3着
        Assert.Equal(new[] { 0, 0, 3, 0 }, c.RankCounts);
    }

    [Fact]
    public void 同点の順位は特殊ルール分を除いたポイントで決める()
    {
        // 30000 の同点。A はコールド賞などで合計 +45 だが、順位ウマ込みの基本ポイントは B (+40) が上
        var game = Game("t", 4, new[] { "A", "B", "C", "D" }, new int?[] { 300, 300, 250, 150 }, new[] { 45.0, 5.0, -15.0, -35.0 });
        game.ColdPoints = new() { 35.0, -35.0, 0, 0 };
        var (stats4, _) = StatsAggregator.Aggregate(Players, new() { game }, new());
        Assert.Equal(1, stats4.Single(s => s.Id == "B").RankCounts[0]);
        Assert.Equal(1, stats4.Single(s => s.Id == "A").RankCounts[1]);
    }

    [Fact]
    public void 四麻と三麻は分けて集計する()
    {
        var (stats4, stats3) = StatsAggregator.Aggregate(Players, Games, Sessions);
        var a3 = stats3.Single(s => s.Id == "A");
        Assert.Equal(1, a3.TotalGames);
        Assert.Equal(57.0, a3.TotalPoint, 9);
        // チップは三麻のセッションのレートで: +2 × 50
        Assert.Equal(100, a3.TotalChipYen);
        Assert.Equal(-50, stats3.Single(s => s.Id == "E").TotalChipYen);
    }

    [Fact]
    public void 局のない人と削除済みは一覧に出さない()
    {
        var (stats4, _) = StatsAggregator.Aggregate(Players, Games, Sessions);
        // E は四麻では抜け番だけ
        Assert.DoesNotContain(stats4, s => s.Id == "E");
        Assert.DoesNotContain(stats4, s => s.Id == "deleted");
        // 収支（局 + チップ）の多い順: A 4860 / B 2405 / C -2475 / D -5290
        Assert.Equal(new[] { "A", "B", "C", "D" }, stats4.Select(s => s.Id));
    }

    [Fact]
    public void 相性は同卓した局だけで比べる()
    {
        var matchups = StatsAggregator.CalculateMatchups("A", 4, Games, Players);
        var vsB = matchups.Single(m => m.OpponentName == "b");
        // 局1・局2 で同卓。ポイント差 (55.2-8.1) + (10-40) = 17.1、局2 は同点なので勝ち負けに数えない
        Assert.Equal(2, vsB.GamesTogether);
        Assert.Equal(17.1, vsB.HeroNetPoint, 9);
        Assert.Equal((1, 0), (vsB.HeroWins, vsB.HeroLosses));
        Assert.Equal(855, vsB.HeroNetYen);

        var vsC = matchups.Single(m => m.OpponentName == "c");
        Assert.Equal(3, vsC.GamesTogether);
        Assert.Equal(142.7, vsC.HeroNetPoint, 9);

        // 削除済みと抜け番の E は相手に含めない
        Assert.DoesNotContain(matchups, m => m.OpponentName is "削除済み" or "e");
    }
}
