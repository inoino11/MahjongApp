using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MahjongApp.Models;

namespace MahjongApp.Services
{
    public class StatsCacheService
    {
        private readonly DatabaseService _databaseService;

        public bool IsInitialized { get; private set; } = false;

        // 計算済みの中間構造体
        public List<PlayerStats> Stats4 { get; private set; } = new();
        public List<PlayerStats> Stats3 { get; private set; } = new();
        public List<PlayerProfile> AvailablePlayers { get; private set; } = new();
        public List<SavedGameRecord> AllGames { get; private set; } = new();

        public StatsCacheService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        // 初期化および再計算ロジック
        public async Task InitializeAsync(bool forceReload = false)
        {
            if (IsInitialized && !forceReload) return;

            var dbData = await _databaseService.GetAllDataAsync();
            if (dbData == null || dbData.Players.Count == 0 || dbData.Games.Count == 0)
            {
                IsInitialized = true;
                return;
            }

            AllGames = dbData.Games;
            AvailablePlayers = dbData.Players.Where(p => p.Id != "deleted").OrderBy(p => p.Name).ToList();

            var statsMap4 = dbData.Players.ToDictionary(p => p.Id, p => new PlayerStats { Id = p.Id, Name = p.Name });
            var statsMap3 = dbData.Players.ToDictionary(p => p.Id, p => new PlayerStats { Id = p.Id, Name = p.Name });

            // 1. 局データの集計
            foreach (var game in dbData.Games)
            {
                bool isSanma = game.PlayerCount == 3;
                var currentMap = isSanma ? statsMap3 : statsMap4;
                var validPlayers = game.PlayerIds
                    .Select((id, idx) => new { Id = id, Score = game.RawScores.Count > idx ? game.RawScores[idx] : null, Index = idx })
                    .Where(x => !string.IsNullOrEmpty(x.Id) && x.Score.HasValue)
                    .OrderByDescending(x => x.Score)
                    .ToList();

                for (int rank = 0; rank < validPlayers.Count; rank++)
                {
                    var p = validPlayers[rank];
                    if (currentMap.ContainsKey(p.Id!))
                    {
                        var stat = currentMap[p.Id!];
                        var idx = p.Index;
                        stat.TotalGames++;
                        stat.RankCounts[rank]++;
                        stat.TotalPoint += game.Points.Count > idx ? game.Points[idx] : 0;
                        stat.TotalGameYen += game.Yen.Count > idx ? game.Yen[idx] : 0;
                    }
                }
            }

            // 2. セッション記録からのチップ集計
            foreach (var session in dbData.Sessions)
            {
                var firstGame = dbData.Games.FirstOrDefault(g => g.SessionId == session.SessionId);
                bool isSanmaSession = firstGame != null && firstGame.PlayerCount == 3;
                var currentMap = isSanmaSession ? statsMap3 : statsMap4;

                for (int i = 0; i < session.ParticipantPlayerIds.Count; i++)
                {
                    string pId = session.ParticipantPlayerIds[i];
                    if (!string.IsNullOrEmpty(pId) && currentMap.ContainsKey(pId) && session.SettledChips.Count > i)
                    {
                        currentMap[pId].TotalChipYen += session.SettledChips[i] * session.ChipRate;
                    }
                }
            }

            Stats4 = statsMap4.Values.Where(s => s.Id != "deleted" && s.TotalGames > 0).OrderByDescending(s => s.TotalYen).ToList();
            Stats3 = statsMap3.Values.Where(s => s.Id != "deleted" && s.TotalGames > 0).OrderByDescending(s => s.TotalYen).ToList();

            IsInitialized = true;
        }

        // 相性分析データの動的生成
        public List<MatchupStat> CalculateMatchups(string heroId, int playerCount)
        {
            var result = new List<MatchupStat>();
            if (string.IsNullOrEmpty(heroId)) return result;

            var map = new Dictionary<string, MatchupStat>();

            foreach (var game in AllGames.Where(g => g.PlayerCount == playerCount))
            {
                int heroIdx = game.PlayerIds.IndexOf(heroId);
                if (heroIdx < 0 || heroIdx >= game.RawScores.Count || !game.RawScores[heroIdx].HasValue) continue;

                double heroPt = game.Points.Count > heroIdx ? game.Points[heroIdx] : 0;
                int heroYen = game.Yen.Count > heroIdx ? game.Yen[heroIdx] : 0;
                double heroRaw = game.RawScores[heroIdx]!.Value;

                for (int i = 0; i < game.PlayerIds.Count; i++)
                {
                    string? oppId = game.PlayerIds[i];
                    if (string.IsNullOrEmpty(oppId) || oppId == "deleted" || oppId == heroId) continue;
                    if (!game.RawScores[i].HasValue) continue;

                    if (!map.ContainsKey(oppId))
                    {
                        var oppName = AvailablePlayers.FirstOrDefault(p => p.Id == oppId)?.Name ?? "不明";
                        map[oppId] = new MatchupStat { OpponentName = oppName };
                    }
                    var stat = map[oppId];
                    stat.GamesTogether++;
                    
                    double oppPt = game.Points.Count > i ? game.Points[i] : 0;
                    int oppYen = game.Yen.Count > i ? game.Yen[i] : 0;
                    
                    stat.HeroNetPoint += (heroPt - oppPt);
                    stat.HeroNetYen += (heroYen - oppYen);
                    
                    double oppRaw = game.RawScores[i]!.Value;
                    if (heroRaw > oppRaw) stat.HeroWins++;
                    else if (heroRaw < oppRaw) stat.HeroLosses++;
                }
            }

            return map.Values.OrderByDescending(s => s.HeroNetYen).ToList();
        }
    }
}