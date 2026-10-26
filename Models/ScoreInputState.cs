using MahjongApp.Services;

namespace MahjongApp.Models;

/// <summary>
/// 素点入力欄（最大7枠）の状態と操作。対局者・抜け番・焼き鳥・トビ賞・最後の1人の自動入力を扱う。
/// 素点の文字列は SessionStateService.ScoreInputBuffers の配列をそのまま書き換える（保存のタイミングは画面側で決める）
/// </summary>
public class ScoreInputState
{
    public const int MaxParticipants = 7;

    // 入力欄の対局者
    public class PlayerSlot
    {
        public string? Id { get; set; } // 名簿にある人はそのID、新しい名前なら null
        public string Name { get; set; } = string.Empty;
    }

    // 記録する局の参加者（抜け番と空欄を除いた人）。Scores は点数（百点単位を100倍した値）
    public record ActiveInput(List<int> Indices, List<int> Scores, List<string> Names, List<bool> Yaki, List<bool> TobiBonus);

    // 修正を始める前の途中入力
    public class Snapshot
    {
        public List<(string Name, string? Id)> Players { get; init; } = new();
        public int ParticipantCount { get; init; }
        public string[] Buffers { get; init; } = Array.Empty<string>();
        public List<bool> Yaki { get; init; } = new();
        public List<bool> TobiBonus { get; init; } = new();
        public List<bool> SitOuts { get; init; } = new();
        public List<int?> PreviousScores { get; init; } = new();
        public int? AutoFilledIndex { get; init; }
    }

    private readonly Func<string[]> _buffers;

    public ScoreInputState(Func<string[]> buffers)
    {
        _buffers = buffers;
        Reset();
    }

    public List<PlayerSlot> Players { get; } = Enumerable.Range(0, MaxParticipants).Select(_ => new PlayerSlot()).ToList();
    public string[] Buffers => _buffers();
    public List<bool> SitOuts { get; private set; } = new();
    public List<bool> Yaki { get; private set; } = new();
    public List<bool> TobiBonus { get; private set; } = new();
    public List<int?> PreviousScores { get; private set; } = new(); // 抜け番にする前の素点（参加に戻したら復元する）
    public int? AutoFilledIndex { get; set; }                        // 自動入力した欄（なければ null）

    // 入力欄の素点（百点単位）。式は計算し、読めなければ null
    public List<int?> Scores => Buffers.Select(ScoreExpression.Evaluate).ToList();

    /// <summary>
    /// 抜け番・焼き鳥・トビ賞・自動入力を初期状態に戻す（素点の文字列は画面側で入れ替える）
    /// </summary>
    public void Reset()
    {
        PreviousScores = Enumerable.Repeat((int?)null, MaxParticipants).ToList();
        Yaki = new(new bool[MaxParticipants]);
        TobiBonus = new(new bool[MaxParticipants]);
        SitOuts = new(new bool[MaxParticipants]);
        AutoFilledIndex = null;
    }

    /// <summary>
    /// 参加者がちょうど対局人数そろい、素点の合計が持ち点×人数になっているか
    /// </summary>
    public bool IsTotalCorrect(RuleSet rules, int participantCount)
    {
        var activeScores = Scores.Take(participantCount).Where((s, i) => !SitOuts[i]).ToList();
        if (activeScores.Count(x => x.HasValue) != rules.PlayerCount) return false;
        return activeScores.Sum(x => x ?? 0) == (rules.StartingPoints * rules.PlayerCount) / 100;
    }

    /// <summary>
    /// 1人だけ空欄なら合計が合うように自動入力し、条件を満たさなくなったら自動入力を消す
    /// </summary>
    public void EvaluateAutoFill(RuleSet rules, int participantCount)
    {
        var activeIndices = Enumerable.Range(0, participantCount).Where(i => !SitOuts[i]).ToList();
        if (activeIndices.Count != rules.PlayerCount)
        {
            ClearAutoFill();
            return;
        }
        var scores = Scores;
        int targetTotal = (rules.StartingPoints * rules.PlayerCount) / 100;
        var manualIndices = activeIndices.Where(i => i != AutoFilledIndex).ToList();
        var nullManualIndices = manualIndices.Where(i => !scores[i].HasValue).ToList();
        if (nullManualIndices.Count == 0)
        {
            if (AutoFilledIndex.HasValue)
                Buffers[AutoFilledIndex.Value] = (targetTotal - manualIndices.Sum(i => scores[i] ?? 0)).ToString();
        }
        else if (nullManualIndices.Count == 1)
        {
            if (AutoFilledIndex.HasValue) ClearAutoFill();
            else
            {
                AutoFilledIndex = nullManualIndices[0];
                Buffers[AutoFilledIndex.Value] = (targetTotal - activeIndices.Where(i => i != AutoFilledIndex).Sum(i => scores[i] ?? 0)).ToString();
            }
        }
        else ClearAutoFill();
    }

    public void ClearAutoFill()
    {
        if (AutoFilledIndex.HasValue)
        {
            Buffers[AutoFilledIndex.Value] = string.Empty;
            AutoFilledIndex = null;
        }
    }

    /// <summary>
    /// 自動入力した欄を手で直し始めたら、その欄を手入力の扱いに戻す
    /// </summary>
    public void TakeOverAutoFilled(int index)
    {
        if (index != AutoFilledIndex) return;
        Buffers[index] = string.Empty;
        AutoFilledIndex = null;
    }

    /// <summary>
    /// 抜け番にしたら素点を退避し、参加に戻したら元に戻す
    /// </summary>
    public void SetSitOut(int index, bool isSitOut)
    {
        SitOuts[index] = isSitOut;
        if (isSitOut)
        {
            PreviousScores[index] = Scores[index];
            Buffers[index] = string.Empty;
            Yaki[index] = false;
            TobiBonus[index] = false;
            if (index == AutoFilledIndex) AutoFilledIndex = null;
        }
        else
        {
            var prevScore = PreviousScores[index];
            if (prevScore.HasValue)
            {
                Buffers[index] = prevScore.Value.ToString();
                PreviousScores[index] = null;
            }
        }
    }

    /// <summary>
    /// 最後の枠を空にする（枠数を減らす前に呼ぶ）。抜け番が要らなくなる人数になれば、全員を参加に戻す
    /// </summary>
    public void ClearLastSlot(RuleSet rules, int participantCount)
    {
        int targetIndex = participantCount - 1;
        Buffers[targetIndex] = string.Empty;
        SitOuts[targetIndex] = false;
        Yaki[targetIndex] = false;
        TobiBonus[targetIndex] = false;
        if (targetIndex != rules.PlayerCount) return;
        for (int i = 0; i < targetIndex; i++)
            if (SitOuts[i]) SetSitOut(i, false);
    }

    /// <summary>
    /// 直前の局の素点・焼き鳥・トビ賞を入力欄に写す
    /// </summary>
    public void CopyFrom(GameResult lastGame, int participantCount)
    {
        for (int i = 0; i < participantCount && i < lastGame.RawScores.Count; i++)
        {
            Buffers[i] = lastGame.RawScores[i]?.ToString() ?? string.Empty;
            if (lastGame.YakiPlayers != null && i < lastGame.YakiPlayers.Count) Yaki[i] = lastGame.YakiPlayers[i];
            if (lastGame.TobiBonusRecipients != null && i < lastGame.TobiBonusRecipients.Count) TobiBonus[i] = lastGame.TobiBonusRecipients[i];
        }
        AutoFilledIndex = null;
    }

    public Snapshot TakeSnapshot(int participantCount) => new()
    {
        Players = Players.Select(p => (p.Name, p.Id)).ToList(),
        ParticipantCount = participantCount,
        Buffers = Buffers.ToArray(),
        Yaki = Yaki.ToList(),
        TobiBonus = TobiBonus.ToList(),
        SitOuts = SitOuts.ToList(),
        PreviousScores = PreviousScores.ToList(),
        AutoFilledIndex = AutoFilledIndex
    };

    public void Restore(Snapshot s)
    {
        for (int i = 0; i < MaxParticipants; i++)
        {
            Players[i].Name = s.Players[i].Name;
            Players[i].Id = s.Players[i].Id;
            Buffers[i] = s.Buffers[i];
        }
        Yaki = s.Yaki;
        TobiBonus = s.TobiBonus;
        SitOuts = s.SitOuts;
        PreviousScores = s.PreviousScores;
        AutoFilledIndex = s.AutoFilledIndex;
    }

    /// <summary>
    /// 記録済みの局を入力欄に読み込む（修正用）。戻り値はその局の枠数
    /// </summary>
    public int LoadGame(GameResult game, IReadOnlyList<PlayerProfile> playerMaster)
    {
        int slots = Math.Min(game.PlayerNames.Count, MaxParticipants);
        for (int i = 0; i < MaxParticipants; i++)
        {
            int? raw = i < slots && i < game.RawScores.Count ? game.RawScores[i] : null;
            string name = i < slots ? game.PlayerNames[i] : string.Empty;
            Players[i].Name = name;
            Players[i].Id = playerMaster.FirstOrDefault(p => p.Name == name)?.Id;
            // 配列ごと差し替えると保存されてしまうため要素を書き換える
            Buffers[i] = raw?.ToString() ?? string.Empty;
            SitOuts[i] = i < slots && !raw.HasValue;
            Yaki[i] = i < slots && (game.YakiPlayers?.ElementAtOrDefault(i) ?? false);
            TobiBonus[i] = i < slots && (game.TobiBonusRecipients?.ElementAtOrDefault(i) ?? false);
        }
        PreviousScores = Enumerable.Repeat((int?)null, MaxParticipants).ToList();
        AutoFilledIndex = null;
        return slots;
    }

    /// <summary>
    /// 記録する局の参加者を取り出す。素点の入った参加者が対局人数ちょうどでなければ null
    /// </summary>
    public ActiveInput? BuildActiveInput(RuleSet rules, int participantCount)
    {
        var scores = Scores;
        var indices = Enumerable.Range(0, participantCount).Where(i => !SitOuts[i] && scores[i].HasValue).ToList();
        if (indices.Count != rules.PlayerCount) return null;
        return new ActiveInput(
            indices,
            indices.Select(i => scores[i]!.Value * 100).ToList(),
            indices.Select(i => Players[i].Name).ToList(),
            indices.Select(i => Yaki[i]).ToList(),
            indices.Select(i => TobiBonus[i]).ToList());
    }

    /// <summary>
    /// 参加者だけで計算した結果を、抜け番を含む入力欄の並びに戻して1局分の記録にする
    /// </summary>
    public GameResult ToGameResult(ActiveInput input, CalculationResult calc, int participantCount)
    {
        var scores = Scores;
        var rawScores = Enumerable.Repeat((int?)null, participantCount).ToList();
        var points = Enumerable.Repeat(0.0, participantCount).ToList();
        var sinkPts = Enumerable.Repeat(0.0, participantCount).ToList();
        var yakiPts = Enumerable.Repeat(0.0, participantCount).ToList();
        var tobiPts = Enumerable.Repeat(0.0, participantCount).ToList();
        var coldPts = Enumerable.Repeat(0.0, participantCount).ToList();
        var yen = Enumerable.Repeat(0, participantCount).ToList();
        var tobiPlayers = Enumerable.Repeat(false, participantCount).ToList();
        for (int i = 0; i < input.Indices.Count; i++)
        {
            int slot = input.Indices[i];
            rawScores[slot] = scores[slot];
            points[slot] = calc.Points[i];
            sinkPts[slot] = calc.SinkPoints[i];
            yakiPts[slot] = calc.YakiPoints[i];
            tobiPts[slot] = calc.TobiPoints[i];
            coldPts[slot] = calc.ColdPoints[i];
            yen[slot] = calc.Yen[i];
            tobiPlayers[slot] = calc.TobiPlayers[i];
        }
        return new GameResult
        {
            Timestamp = DateTime.Now,
            PlayerNames = Players.Take(participantCount).Select(p => p.Name).ToList(),
            RawScores = rawScores,
            Points = points,
            YakiPlayers = Yaki.Take(participantCount).ToList(),
            TobiPlayers = tobiPlayers,
            TobiBonusRecipients = TobiBonus.Take(participantCount).ToList(),
            SinkPoints = sinkPts,
            YakiPoints = yakiPts,
            TobiPoints = tobiPts,
            ColdPoints = coldPts,
            Yen = yen
        };
    }
}
