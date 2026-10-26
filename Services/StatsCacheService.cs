using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MahjongApp.Models;

namespace MahjongApp.Services;

/// <summary>
/// 確定済みの成績から集計したランキングをメモリに保持する。成績が変わったら InitializeAsync(forceReload: true) で作り直す
/// </summary>
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
            if (dbData == null)
            {
                Stats4.Clear();
                Stats3.Clear();
                AvailablePlayers.Clear();
                AllGames.Clear();
                IsInitialized = true;
                return;
            }
            // 古いデータや取り込んだデータでリストが null のことがある
            var safePlayers = dbData.Players ?? new List<PlayerProfile>();
            var safeGames = dbData.Games ?? new List<SavedGameRecord>();
            var safeSessions = dbData.Sessions ?? new List<SavedSessionRecord>();
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
            AvailablePlayers = safePlayers.Where(p => p != null && p.Id != "deleted").OrderBy(p => p.Name).ToList();
            (Stats4, Stats3) = StatsAggregator.Aggregate(safePlayers, safeGames, safeSessions);
            IsInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StatsCacheService Initialization Error: {ex.Message}");
            IsInitialized = true; // 失敗しても再試行し続けて固まらないよう、初期化済みにする
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// heroId の人から見た、同卓した相手ごとの成績を計算する
    /// </summary>
    public List<MatchupStat> CalculateMatchups(string heroId, int playerCount) =>
        StatsAggregator.CalculateMatchups(heroId, playerCount, AllGames, AvailablePlayers);
}
