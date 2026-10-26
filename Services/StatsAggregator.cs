using MahjongApp.Models;

namespace MahjongApp.Services;

/// <summary>
/// 確定済みの成績からランキングと相性を集計する（状態を持たない）。StatsCacheService が読み込んだデータを渡す
/// </summary>
public static class StatsAggregator
{
    /// <summary>
    /// 四麻・三麻それぞれの個人成績（局のある人だけ、収支の多い順）。「削除済み」は除く
    /// </summary>
    public static (List<PlayerStats> Stats4, List<PlayerStats> Stats3) Aggregate(List<PlayerProfile> players, List<SavedGameRecord> games, List<SavedSessionRecord> sessions)
    {
        var statsMap4 = players.Where(p => p != null).ToDictionary(p => p.Id, p => new PlayerStats { Id = p.Id, Name = p.Name ?? "不明" });
        var statsMap3 = players.Where(p => p != null).ToDictionary(p => p.Id, p => new PlayerStats { Id = p.Id, Name = p.Name ?? "不明" });

        // 1. 局ごとの順位・ポイント・収支
        foreach (var game in games)
        {
            if (game == null) continue;
            var playerIds = game.PlayerIds ?? new List<string?>();
            var rawScores = game.RawScores ?? new List<int?>();
            var points = game.Points ?? new List<double>();
            var yen = game.Yen ?? new List<int>();
            var currentMap = game.PlayerCount == 3 ? statsMap3 : statsMap4;
            // 同点時の順位は、席順で決まった順位ウマが反映された基本ポイント（特殊ルール分を除く）で判定する
            double At(List<double>? list, int idx) => list != null && idx < list.Count ? list[idx] : 0;
            double BasePoint(int idx) => At(points, idx) - At(game.SinkPoints, idx) - At(game.YakiPoints, idx) - At(game.TobiPoints, idx) - At(game.ColdPoints, idx);
            var validPlayers = playerIds
                .Select((id, idx) => new { Id = id, Score = rawScores.Count > idx ? rawScores[idx] : null, Index = idx })
                .Where(x => !string.IsNullOrEmpty(x.Id) && x.Score.HasValue)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => Math.Round(BasePoint(x.Index), 1))
                .ToList();
            for (int rank = 0; rank < validPlayers.Count; rank++)
            {
                var p = validPlayers[rank];
                if (p.Id != "deleted" && currentMap.TryGetValue(p.Id!, out var stat))
                {
                    var idx = p.Index;
                    stat.TotalGames++;
                    if (rank < stat.RankCounts.Length) stat.RankCounts[rank]++;
                    stat.TotalPoint += points.Count > idx ? points[idx] : 0;
                    stat.TotalGameYen += yen.Count > idx ? yen[idx] : 0;
                }
            }
        }

        // 2. セッションごとのチップ（三麻か四麻かは、そのセッションの最初の局で判断する）
        foreach (var session in sessions)
        {
            if (session == null) continue;
            var participantIds = session.ParticipantPlayerIds ?? new List<string>();
            var settledChips = session.SettledChips ?? new List<int>();
            var firstGame = games.FirstOrDefault(g => g != null && g.SessionId == session.SessionId);
            var currentMap = firstGame != null && firstGame.PlayerCount == 3 ? statsMap3 : statsMap4;
            for (int i = 0; i < participantIds.Count; i++)
            {
                string pId = participantIds[i];
                if (!string.IsNullOrEmpty(pId) && currentMap.TryGetValue(pId, out var stat) && settledChips.Count > i)
                    stat.TotalChipYen += settledChips[i] * session.ChipRate;
            }
        }

        var stats4 = statsMap4.Values.Where(s => s.Id != "deleted" && s.TotalGames > 0).OrderByDescending(s => s.TotalYen).ToList();
        var stats3 = statsMap3.Values.Where(s => s.Id != "deleted" && s.TotalGames > 0).OrderByDescending(s => s.TotalYen).ToList();
        return (stats4, stats3);
    }

    /// <summary>
    /// heroId の人から見た、同卓した相手ごとの成績（収支差の多い順）
    /// </summary>
    public static List<MatchupStat> CalculateMatchups(string heroId, int playerCount, List<SavedGameRecord> games, List<PlayerProfile> players)
    {
        var result = new List<MatchupStat>();
        if (string.IsNullOrEmpty(heroId)) return result;
        var map = new Dictionary<string, MatchupStat>();
        foreach (var game in games.Where(g => g != null && g.PlayerCount == playerCount))
        {
            var playerIds = game.PlayerIds ?? new List<string?>();
            var rawScores = game.RawScores ?? new List<int?>();
            var points = game.Points ?? new List<double>();
            var yen = game.Yen ?? new List<int>();
            int heroIdx = playerIds.IndexOf(heroId);
            if (heroIdx < 0 || heroIdx >= rawScores.Count || !rawScores[heroIdx].HasValue) continue;
            double heroPt = points.Count > heroIdx ? points[heroIdx] : 0;
            int heroYen = yen.Count > heroIdx ? yen[heroIdx] : 0;
            double heroRaw = rawScores[heroIdx]!.Value;
            for (int i = 0; i < playerIds.Count; i++)
            {
                string? oppId = playerIds[i];
                if (string.IsNullOrEmpty(oppId) || oppId == "deleted" || oppId == heroId) continue;
                if (i >= rawScores.Count || !rawScores[i].HasValue) continue;
                if (!map.TryGetValue(oppId, out var stat))
                {
                    var oppName = players.FirstOrDefault(p => p != null && p.Id == oppId)?.Name ?? "不明";
                    stat = new MatchupStat { OpponentName = oppName };
                    map[oppId] = stat;
                }
                stat.GamesTogether++;
                double oppPt = points.Count > i ? points[i] : 0;
                int oppYen = yen.Count > i ? yen[i] : 0;
                stat.HeroNetPoint += (heroPt - oppPt);
                stat.HeroNetYen += (heroYen - oppYen);
                double oppRaw = rawScores[i]!.Value;
                if (heroRaw > oppRaw) stat.HeroWins++;
                else if (heroRaw < oppRaw) stat.HeroLosses++;
            }
        }
        return map.Values.OrderByDescending(s => s.HeroNetYen).ToList();
    }
}
