using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// セッション確定時の記録の組み立てと、ルール名の判定
public class SessionRecordBuilderTests
{
    private static GameResult Game(string[] names, int?[] raws, double[] points) => new()
    {
        PlayerNames = names.ToList(),
        RawScores = raws.ToList(),
        Points = points.ToList(),
        Yen = points.Select(p => (int)(p * 50)).ToList(),
        SinkPoints = new() { 0, 0, 0, 0, 0 },
        Timestamp = new DateTime(2026, 10, 26, 20, 0, 0),
    };

    // 5人で2局。局1は「お」が抜け番、局2は「い」が抜け番
    private static readonly List<GameResult> History = new()
    {
        Game(new[] { "あ", "い", "う", "え", "お" }, new int?[] { 452, 281, 175, 92, null }, new[] { 55.2, 8.1, -22.5, -40.8, 0 }),
        Game(new[] { "あ", "い", "う", "え", "お" }, new int?[] { 300, null, 300, 250, 150 }, new[] { 40.0, 0, 10.0, -15.0, -35.0 }),
    };

    [Fact]
    public void 素点の入った人を出てきた順に集める()
    {
        Assert.Equal(new[] { "あ", "い", "う", "え", "お" }, SessionRecordBuilder.CollectPlayerNames(History));
        // 一度も素点のない人と空欄は含めない
        var history = new List<GameResult> { Game(new[] { "あ", "", "う", "え", "お" }, new int?[] { 452, 281, 175, 92, null }, new double[5]) };
        Assert.Equal(new[] { "あ", "う", "え" }, SessionRecordBuilder.CollectPlayerNames(history));
    }

    [Fact]
    public void 名簿にない人は新しく作り名簿の人はIDを使う()
    {
        var master = new List<PlayerProfile> { new() { Id = "id-a", Name = "あ", CumulativeChips = 5 }, new() { Id = "id-u", Name = "う" } };
        var now = new DateTime(2026, 10, 26, 23, 0, 0);
        var r = SessionRecordBuilder.Build(History, new RuleSet { Name = "四麻" }, new() { ["あ"] = 3, ["お"] = -3 }, master, now);

        Assert.Equal(new[] { "い", "え", "お" }, r.NewPlayers.Select(p => p.Name));
        Assert.All(r.NewPlayers, p => Assert.Equal(now, p.CreatedAt));
        Assert.Equal(5, r.PlayersToUpdate.Count);
        // 名簿の人は既存の内容をそのまま保存し直す
        Assert.Equal(5, r.PlayersToUpdate.Single(p => p.Id == "id-a").CumulativeChips);

        var idOf = r.PlayersToUpdate.ToDictionary(p => p.Name, p => p.Id);
        Assert.Equal(new[] { idOf["あ"], idOf["い"], idOf["う"], idOf["え"], idOf["お"] }, r.Session.ParticipantPlayerIds);
        // チップは参加者の順。入力のない人は 0
        Assert.Equal(new[] { 3, 0, 0, 0, -3 }, r.Session.SettledChips);
    }

    [Fact]
    public void 各局はセッションとルールを引き継ぎ抜け番の枠も残す()
    {
        var rules = new RuleSet { Name = "四麻", Rate = 50, ChipRate = 100 };
        var r = SessionRecordBuilder.Build(History, rules, new(), new List<PlayerProfile>(), DateTime.Now);
        Assert.Equal(2, r.Games.Count);
        Assert.All(r.Games, g => Assert.Equal(r.Session.SessionId, g.SessionId));
        Assert.All(r.Games, g => Assert.Equal(("四麻", 4), (g.RuleName, g.PlayerCount)));
        Assert.Equal(new int?[] { 452, 281, 175, 92, null }, r.Games[0].RawScores);
        Assert.Equal(5, r.Games[0].PlayerIds.Count);
        Assert.Equal(History[1].Timestamp, r.Games[1].Timestamp);
        // 当時のルールは複製して残す（後でルールを変えても記録は変わらない）
        Assert.Equal((50, 100), (r.Session.Rate, r.Session.ChipRate));
        Assert.NotSame(rules, r.Session.Rules);
        Assert.Equal("四麻", r.Session.Rules!.Name);
    }

    [Fact]
    public void ルール名が空なら未設定にする()
    {
        Assert.Equal(SessionRecordBuilder.RuleNameIssue.Empty, SessionRecordBuilder.CheckRuleName(new RuleSet { Name = " " }, new List<RuleSet>()));
    }

    [Fact]
    public void プリセットから内容を変えていたら未設定にする()
    {
        var presets = new List<RuleSet> { new() { Name = "いつもの", Uma2 = 20 } };
        Assert.Equal(SessionRecordBuilder.RuleNameIssue.ChangedFromPreset, SessionRecordBuilder.CheckRuleName(new RuleSet { Name = "いつもの", Uma2 = 30 }, presets));
    }

    [Fact]
    public void 初期のMリーグルールを読み込んだだけなら変更扱いにしない()
    {
        // 初期のプリセットはウマ10-30だが、順位ごとのウマは既定の +20/+10/-10/-20 のまま保存されている
        var preset = new RuleSet { Name = "Mリーグルール", Uma1 = 10, Uma2 = 30, Rate = 0, ChipRate = 0, TieBreaker = TieBreakerRule.Split };
        // 読み込むと、順位ごとのウマが +30/+10/-10/-30 に揃えられる
        var rules = preset.DeepClone();
        rules.SyncCustomUmaFromNormal();
        Assert.Equal(SessionRecordBuilder.RuleNameIssue.None, SessionRecordBuilder.CheckRuleName(rules, new[] { preset }));
    }

    [Fact]
    public void メモだけの違いやプリセットにない名前はそのまま記録する()
    {
        var presets = new List<RuleSet> { new() { Name = "いつもの", Memo = "赤3枚" } };
        Assert.Equal(SessionRecordBuilder.RuleNameIssue.None, SessionRecordBuilder.CheckRuleName(new RuleSet { Name = "いつもの", Memo = "赤4枚" }, presets));
        Assert.Equal(SessionRecordBuilder.RuleNameIssue.None, SessionRecordBuilder.CheckRuleName(new RuleSet { Name = "新ルール", Uma2 = 30 }, presets));
    }
}
