using MahjongApp.Models;
using MahjongApp.Services;

namespace MahjongApp.Tests;

// 素点入力欄の状態（自動入力・抜け番・修正の退避・記録の組み立て）
public class ScoreInputStateTests
{
    private readonly string[] _buffers = Enumerable.Repeat(string.Empty, ScoreInputState.MaxParticipants).ToArray();
    private readonly ScoreInputState _input;
    private readonly RuleSet _rules = new(); // 四麻 25000持ち（合計 1000 百点）

    public ScoreInputStateTests()
    {
        _input = new ScoreInputState(() => _buffers);
        string[] names = { "あ", "い", "う", "え", "お", "か", "き" };
        for (int i = 0; i < names.Length; i++) _input.Players[i].Name = names[i];
    }

    private void Type(int index, string value)
    {
        _buffers[index] = value;
        _input.EvaluateAutoFill(_rules, 4);
    }

    [Fact]
    public void 三人入れたら最後の一人は合計が合うように自動で入る()
    {
        Type(0, "452");
        Type(1, "281");
        Assert.Null(_input.AutoFilledIndex);
        Type(2, "175");
        // 1000 - (452 + 281 + 175) = 92
        Assert.Equal(3, _input.AutoFilledIndex);
        Assert.Equal("92", _buffers[3]);
        Assert.True(_input.IsTotalCorrect(_rules, 4));
    }

    [Fact]
    public void 手入力を直すと自動入力した欄も追従する()
    {
        Type(0, "452"); Type(1, "281"); Type(2, "175");
        Type(2, "200");
        Assert.Equal("67", _buffers[3]);
    }

    [Fact]
    public void 空欄が二つになったら自動入力を消す()
    {
        Type(0, "452"); Type(1, "281"); Type(2, "175");
        Type(1, "");
        Assert.Null(_input.AutoFilledIndex);
        Assert.Equal("", _buffers[3]);
    }

    [Fact]
    public void 自動入力した欄を手で直し始めたら手入力の扱いになる()
    {
        Type(0, "452"); Type(1, "281"); Type(2, "175");
        _input.TakeOverAutoFilled(3);
        Assert.Null(_input.AutoFilledIndex);
        Assert.Equal("", _buffers[3]);
    }

    [Fact]
    public void 式のまま入っていても合計を判定できる()
    {
        // 自動入力を介さず、4人とも手で入れた状態
        _buffers[0] = "500-48"; _buffers[1] = "281"; _buffers[2] = "175"; _buffers[3] = "92";
        Assert.True(_input.IsTotalCorrect(_rules, 4));
        _buffers[3] = "93";
        Assert.False(_input.IsTotalCorrect(_rules, 4));
    }

    [Fact]
    public void 抜け番にすると素点を退避し戻すと復元する()
    {
        // 5人。2人目に素点と焼き鳥を入れてから抜け番にする
        _buffers[1] = "300";
        _input.Yaki[1] = true;
        _input.SetSitOut(1, true);
        Assert.Equal("", _buffers[1]);
        Assert.False(_input.Yaki[1]);
        Assert.Equal(300, _input.PreviousScores[1]);

        _input.SetSitOut(1, false);
        Assert.Equal("300", _buffers[1]);
        Assert.Null(_input.PreviousScores[1]);
    }

    [Fact]
    public void 抜け番を除いた四人で自動入力する()
    {
        _input.SetSitOut(4, true);
        _buffers[0] = "520"; _buffers[1] = "300"; _buffers[2] = "185";
        _input.EvaluateAutoFill(_rules, 5);
        Assert.Equal(3, _input.AutoFilledIndex);
        Assert.Equal("-5", _buffers[3]);
    }

    [Fact]
    public void 五人目の枠を消すと抜け番の人を参加に戻す()
    {
        _buffers[1] = "300";
        _input.SetSitOut(1, true);
        _buffers[4] = "250";
        _input.ClearLastSlot(_rules, 5);
        Assert.Equal("", _buffers[4]);
        Assert.False(_input.SitOuts[1]);
        Assert.Equal("300", _buffers[1]);
    }

    [Fact]
    public void 六人から五人に減らすときは抜け番を残す()
    {
        _input.SetSitOut(1, true);
        _input.ClearLastSlot(_rules, 6);
        Assert.True(_input.SitOuts[1]);
    }

    [Fact]
    public void 前の局の素点と焼き鳥とトビ賞を写す()
    {
        var last = new GameResult
        {
            RawScores = new() { 520, null, 300, 185, -5 },
            YakiPlayers = new() { false, false, true, false, false },
            TobiBonusRecipients = new() { true, false, false, false, false },
        };
        _input.CopyFrom(last, 5);
        Assert.Equal(new[] { "520", "", "300", "185", "-5" }, _buffers.Take(5));
        Assert.True(_input.Yaki[2]);
        Assert.True(_input.TobiBonus[0]);
    }

    [Fact]
    public void 修正のために局を読み込み元の途中入力に戻せる()
    {
        // 途中入力: 1人目に 250
        _buffers[0] = "250";
        var snapshot = _input.TakeSnapshot(4);

        // 5人で記録した局（2人目が抜け番、3人目が焼き鳥）を読み込む
        var game = new GameResult
        {
            PlayerNames = new() { "あ", "い", "う", "え", "お" },
            RawScores = new() { 520, null, 300, 185, -5 },
            YakiPlayers = new() { false, false, true, false, false },
        };
        var master = new List<PlayerProfile> { new() { Id = "p-u", Name = "う" } };
        int slots = _input.LoadGame(game, master);
        Assert.Equal(5, slots);
        Assert.Equal("520", _buffers[0]);
        Assert.True(_input.SitOuts[1]);
        Assert.True(_input.Yaki[2]);
        Assert.Equal("p-u", _input.Players[2].Id);
        Assert.Null(_input.Players[0].Id);

        _input.Restore(snapshot);
        Assert.Equal("250", _buffers[0]);
        Assert.Equal("", _buffers[2]);
        Assert.False(_input.SitOuts[1]);
        Assert.False(_input.Yaki[2]);
        Assert.Equal("お", _input.Players[4].Name);
    }

    [Fact]
    public void 参加者が対局人数ちょうどでなければ記録できない()
    {
        _buffers[0] = "452"; _buffers[1] = "281"; _buffers[2] = "175";
        Assert.Null(_input.BuildActiveInput(_rules, 4));
    }

    [Fact]
    public void 抜け番を除いて計算し入力欄の並びに戻して記録する()
    {
        // 5人。2人目が抜け番、4人目が焼き鳥
        _input.SetSitOut(1, true);
        _buffers[0] = "520"; _buffers[2] = "300"; _buffers[3] = "185"; _buffers[4] = "-5";
        _input.Yaki[3] = true;

        var active = _input.BuildActiveInput(_rules, 5)!;
        Assert.Equal(new[] { 0, 2, 3, 4 }, active.Indices);
        Assert.Equal(new[] { 52000, 30000, 18500, -500 }, active.Scores);
        Assert.Equal(new[] { "あ", "う", "え", "お" }, active.Names);
        Assert.Equal(new[] { false, false, true, false }, active.Yaki);

        var calc = new CalculationResult
        {
            Points = new() { 62.0, 10.0, -21.5, -50.5 },
            Yen = new() { 3100, 500, -1075, -2525 },
            SinkPoints = new() { 0, 0, 0, 0 },
            YakiPoints = new() { 1, 1, -3, 1 },
            TobiPoints = new() { 0, 0, 0, 0 },
            ColdPoints = new() { 0, 0, 0, 0 },
            TobiPlayers = new() { false, false, false, true },
        };
        var game = _input.ToGameResult(active, calc, 5);
        Assert.Equal(new[] { "あ", "い", "う", "え", "お" }, game.PlayerNames);
        Assert.Equal(new int?[] { 520, null, 300, 185, -5 }, game.RawScores);
        Assert.Equal(new[] { 62.0, 0, 10.0, -21.5, -50.5 }, game.Points);
        Assert.Equal(new[] { 3100, 0, 500, -1075, -2525 }, game.Yen);
        Assert.Equal(new[] { 1.0, 0, 1, -3, 1 }, game.YakiPoints);
        Assert.Equal(new[] { false, false, false, false, true }, game.TobiPlayers);
        Assert.Equal(new[] { false, false, false, true, false }, game.YakiPlayers);
    }
}
