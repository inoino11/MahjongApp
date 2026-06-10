using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MahjongApp.Models;

namespace MahjongApp.Services
{
    public class StatsCacheService
    {
        private readonly DatabaseService _databaseService;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        public bool IsInitialized { get; private set; } = false;
        public List<PlayerStats> Stats4 { get; private set; } = new();
        public List<PlayerStats> Stats3 { get; private set; } = new();
        public List<PlayerProfile> AvailablePlayers { get; private set; } = new();
        public List<SavedGameRecord> AllGames { get; private set; } = new();
        public StatsCacheService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }
        public async Task InitializeAsync(bool forceReload = false)
        {
            if (IsInitialized && !forceReload) return;
            await _initLock.WaitAsync();
            try
            {
                if (IsInitialized && !forceReload) return;
                var dbData = await _databaseService.GetAllDataAsync();
                // データ自体がnullの場合、メモリ上のリストをクリア
                if (dbData == null)
                {
                    Stats4.Clear();
                    Stats3.Clear();
                    AvailablePlayers.Clear();
                    AllGames.Clear();
                    IsInitialized = true;
                    return;
                }
                // Listがnullの場合、空のリストを割り当ててNRE(NullReferenceException)を防ぐ
                var safePlayers = dbData.Players ?? new List<PlayerProfile>();
                var safeGames = dbData.Games ?? new List<SavedGameRecord>();
                var safeSessions = dbData.Sessions ?? new List<SavedSessionRecord>();
                // データが空の場合、メモリ上のリストをクリア
                if (!safePlayers.Any() || !safeGames.Any())
                {
                    Stats4.Clear();
                    Stats3.Clear();
                    AvailablePlayers.Clear();
                    AllGames.Clear();
                    IsInitialized = true;
                    return;
                }
                AllGames = safeGames;
                // p != null のチェックを追加し、不正なプレイヤーデータを除外
                AvailablePlayers = safePlayers.Where(p => p != null && p.Id != "deleted").OrderBy(p => p.Name).ToList();
                var statsMap4 = safePlayers.Where(p => p != null).ToDictionary(p => p.Id, p => new PlayerStats { Id = p.Id, Name = p.Name ?? "不明" });
                var statsMap3 = safePlayers.Where(p => p != null).ToDictionary(p => p.Id, p => new PlayerStats { Id = p.Id, Name = p.Name ?? "不明" });
                // 1. 局データの集計
                foreach (var game in safeGames)
                {
                    if (game == null) continue;
                    // 内部リストのnullチェック
                    var playerIds = game.PlayerIds ?? new List<string?>();
                    var rawScores = game.RawScores ?? new List<int?>();
                    var points = game.Points ?? new List<double>();
                    var yen = game.Yen ?? new List<int>();
                    bool isSanma = game.PlayerCount == 3;
                    var currentMap = isSanma ? statsMap3 : statsMap4;
                    var validPlayers = playerIds
                        .Select((id, idx) => new { Id = id, Score = rawScores.Count > idx ? rawScores[idx] : null, Index = idx })
                        .Where(x => !string.IsNullOrEmpty(x.Id) && x.Score.HasValue)
                        .OrderByDescending(x => x.Score)
                        .ToList();
                    for (int rank = 0; rank < validPlayers.Count; rank++)
                    {
                        var p = validPlayers[rank];
                        if (currentMap.TryGetValue(p.Id!, out var stat))
                        {
                            var idx = p.Index;
                            stat.TotalGames++;
                            if (rank < stat.RankCounts.Length) stat.RankCounts[rank]++;
                            stat.TotalPoint += points.Count > idx ? points[idx] : 0;
                            stat.TotalGameYen += yen.Count > idx ? yen[idx] : 0;
                        }
                    }
                }
                // 2. セッション記録からのチップ集計
                foreach (var session in safeSessions)
                {
                    if (session == null) continue;
                    var participantIds = session.ParticipantPlayerIds ?? new List<string>();
                    var settledChips = session.SettledChips ?? new List<int>();
                    var firstGame = safeGames.FirstOrDefault(g => g != null && g.SessionId == session.SessionId);
                    bool isSanmaSession = firstGame != null && firstGame.PlayerCount == 3;
                    var currentMap = isSanmaSession ? statsMap3 : statsMap4;
                    for (int i = 0; i < participantIds.Count; i++)
                    {
                        string pId = participantIds[i];
                        if (!string.IsNullOrEmpty(pId) && currentMap.TryGetValue(pId, out var stat) && settledChips.Count > i)
                        {
                            stat.TotalChipYen += settledChips[i] * session.ChipRate;
                        }
                    }
                }
                Stats4 = statsMap4.Values.Where(s => s.Id != "deleted" && s.TotalGames > 0).OrderByDescending(s => s.TotalYen).ToList();
                Stats3 = statsMap3.Values.Where(s => s.Id != "deleted" && s.TotalGames > 0).OrderByDescending(s => s.TotalYen).ToList();
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StatsCacheService Initialization Error: {ex.Message}");
                IsInitialized = true; // エラー時もフラグを立てて、無限リトライによるフリーズを防ぐ
            }
            finally
            {
                // 成功・失敗に関わらず、必ずロックを解放する
                _initLock.Release();
            }
        }

        public List<MatchupStat> CalculateMatchups(string heroId, int playerCount)
        {
            var result = new List<MatchupStat>();
            if (string.IsNullOrEmpty(heroId)) return result;
            var map = new Dictionary<string, MatchupStat>();
            foreach (var game in AllGames.Where(g => g != null && g.PlayerCount == playerCount))
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
                        var oppName = AvailablePlayers.FirstOrDefault(p => p != null && p.Id == oppId)?.Name ?? "不明";
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
}