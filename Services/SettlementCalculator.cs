using MahjongApp.Models;

namespace MahjongApp.Services;

/// <summary>
/// 場代の支払い方
/// </summary>
public enum FeeRule
{
    TopPays,      // 総額トップ払い
    SplitEvenly,  // 同額払い
    TopPerGame,   // 各戦トップ払い
    SplitByGames  // 対戦数比払い
}

/// <summary>
/// 精算の対象に選べるセッション（確定済み、または入力中）と、その局
/// </summary>
public class SettlementSessionGroup
{
    public SavedSessionRecord Session { get; set; } = new();
    public List<SavedGameRecord> Games { get; set; } = new();
    public bool IsUnsaved { get; set; } = false;
    public override bool Equals(object? obj) => obj is SettlementSessionGroup other && Session.SessionId == other.Session.SessionId;
    public override int GetHashCode() => Session.SessionId.GetHashCode();
}

/// <summary>
/// 1人分の精算結果
/// </summary>
public class SettlementResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double TotalPt { get; set; }
    public int GamesPlayed { get; set; }
    public double Fee { get; set; }
    public Dictionary<string, int> SessionYen { get; set; } = new(); // セッションごとの収支（局の収支 + チップ）
    public int TotalMahjongYen => SessionYen.Values.Sum();
    public double FinalAmount => TotalMahjongYen - Fee;
}

/// <summary>
/// 選んだセッションの収支を合計し、場代を支払い方に応じて割り振る（状態を持たない）
/// </summary>
public static class SettlementCalculator
{
    /// <summary>
    /// 精算する。playerNames は ID → 名前（名簿にない ID は「不明」）。結果は最終収支の多い順
    /// </summary>
    public static List<SettlementResult> Calculate(IEnumerable<SettlementSessionGroup> sessions, int totalFee, FeeRule rule, IReadOnlyDictionary<string, string> playerNames)
    {
        var groups = sessions.ToList();
        if (groups.Count == 0) return new();

        var map = new Dictionary<string, SettlementResult>();
        SettlementResult GetOrAdd(string id)
        {
            if (!map.TryGetValue(id, out var res))
            {
                res = new SettlementResult { Id = id, Name = playerNames.TryGetValue(id, out var name) ? name : "不明" };
                map[id] = res;
            }
            return res;
        }

        foreach (var group in groups)
        {
            var session = group.Session;
            var sessionId = session.SessionId;
            var pIds = session.ParticipantPlayerIds ?? new();
            var sChips = session.SettledChips ?? new();

            // チップ × そのセッションのチップレート
            for (int i = 0; i < pIds.Count; i++)
            {
                var pId = pIds[i];
                if (string.IsNullOrEmpty(pId)) continue;
                var res = GetOrAdd(pId);
                if (!res.SessionYen.ContainsKey(sessionId)) res.SessionYen[sessionId] = 0;
                if (sChips.Count > i) res.SessionYen[sessionId] += sChips[i] * (session.Rules?.ChipRate ?? 0);
            }

            // 局の収支（抜け番の局は数えない）
            foreach (var game in group.Games)
            {
                var gPids = game.PlayerIds ?? new();
                var gPts = game.Points ?? new();
                var gYen = game.Yen ?? new();
                var gRaws = game.RawScores ?? new();
                for (int i = 0; i < gPids.Count; i++)
                {
                    var pId = gPids[i];
                    if (string.IsNullOrEmpty(pId)) continue;
                    var res = GetOrAdd(pId);
                    if (!res.SessionYen.ContainsKey(sessionId)) res.SessionYen[sessionId] = 0;
                    if (gRaws.Count > i && gRaws[i].HasValue)
                    {
                        res.GamesPlayed++;
                        res.TotalPt += gPts.Count > i ? gPts[i] : 0;
                        res.SessionYen[sessionId] += gYen.Count > i ? gYen[i] : 0;
                    }
                }
            }
        }

        var players = map.Values.Where(p => p.GamesPlayed > 0 || p.TotalMahjongYen != 0).ToList();
        if (players.Count == 0) return new();
        double fee = totalFee;
        switch (rule)
        {
            case FeeRule.TopPays:
            {
                // 小数の計算誤差で同点を取りこぼさないよう、表示と同じ小数第1位で比較する
                double maxPt = Math.Round(players.Max(p => p.TotalPt), 1);
                var tops = players.Where(p => Math.Round(p.TotalPt, 1) == maxPt).ToList();
                foreach (var p in tops) p.Fee = fee / tops.Count;
                break;
            }
            case FeeRule.TopPerGame:
            {
                // 各局のトップ（同点なら全員）が、場代 ÷ 局数 を分け合う
                var gameTops = new List<List<SettlementResult>>();
                foreach (var game in groups.SelectMany(g => g.Games))
                {
                    var gPids = game.PlayerIds ?? new();
                    var gPts = game.Points ?? new();
                    double maxScore = double.MinValue;
                    var tops = new List<SettlementResult>();
                    for (int i = 0; i < gPids.Count; i++)
                    {
                        var pId = gPids[i];
                        if (string.IsNullOrEmpty(pId) || i >= gPts.Count) continue;
                        double score = Math.Round(gPts[i], 1);
                        if (score > maxScore)
                        {
                            maxScore = score;
                            tops.Clear();
                            if (map.TryGetValue(pId, out var pr)) tops.Add(pr);
                        }
                        else if (score == maxScore && map.TryGetValue(pId, out var pr))
                        {
                            tops.Add(pr);
                        }
                    }
                    if (tops.Count > 0) gameTops.Add(tops);
                }
                if (gameTops.Count > 0)
                {
                    double feePerGame = fee / gameTops.Count;
                    foreach (var tops in gameTops)
                        foreach (var p in tops) p.Fee += feePerGame / tops.Count;
                }
                break;
            }
            case FeeRule.SplitByGames:
            {
                int totalGames = players.Sum(p => p.GamesPlayed);
                if (totalGames > 0)
                    foreach (var p in players) p.Fee = fee * ((double)p.GamesPlayed / totalGames);
                break;
            }
            case FeeRule.SplitEvenly:
            {
                foreach (var p in players) p.Fee = fee / players.Count;
                break;
            }
        }
        return players.OrderByDescending(p => p.FinalAmount).ToList();
    }

    /// <summary>
    /// セッション内で同じ人が複数の参加枠にいる場合（途中で席を移ったなど）、1つにまとめてチップを合算する
    /// </summary>
    public static SavedSessionRecord MergeDuplicateParticipants(SavedSessionRecord session)
    {
        var distinctIds = new List<string>();
        var distinctChips = new List<int>();
        var pIds = session.ParticipantPlayerIds ?? new();
        var sChips = session.SettledChips ?? new();
        for (int i = 0; i < pIds.Count; i++)
        {
            var id = pIds[i];
            var chip = sChips.Count > i ? sChips[i] : 0;
            int existingIdx = id == null ? -1 : distinctIds.IndexOf(id);
            if (existingIdx != -1) distinctChips[existingIdx] += chip;
            else { distinctIds.Add(id ?? ""); distinctChips.Add(chip); }
        }
        return new SavedSessionRecord
        {
            SessionId = session.SessionId,
            Date = session.Date,
            Rate = session.Rate,
            ChipRate = session.ChipRate,
            Rules = session.Rules,
            ParticipantPlayerIds = distinctIds,
            SettledChips = distinctChips
        };
    }
}
