using System;
using System.Collections.Generic;
using System.Linq;
using MahjongApp.Models;

namespace MahjongApp.Services;

/// <summary>
/// 1局分の素点からポイント・収支を計算する（状態を持たない）
/// </summary>
public class MahjongCalculatorService
{
    /// <summary>
    /// ポイントを0.1pt単位に丸める（五捨六入）。
    /// 素点は100点単位のため、通常の返し点では値は変わらない
    /// </summary>
    public double RoundToTenth(double value) => Math.Floor((value * 10) + 0.4) / 10.0;

    /// <summary>
    /// 合計ptが0からずれた分を0.1ずつ調整する（足りなければ上位から加え、余れば下位から引く）
    /// </summary>
    public void AdjustFractions(double[] points, List<int> orderedIndices)
    {
        double diff = Math.Round(0 - points.Sum(), 1);
        if (Math.Abs(diff) < 0.05) return;
        
        int steps = (int)Math.Round(Math.Abs(diff) * 10);
        double adjustment = diff > 0 ? 0.1 : -0.1;
        var targetIndices = diff > 0 ? orderedIndices : Enumerable.Reverse(orderedIndices).ToList();
        
        for (int i = 0; i < steps; i++)
        {
            int targetIndex = targetIndices[i % targetIndices.Count];
            points[targetIndex] = Math.Round(points[targetIndex] + adjustment, 1);
        }
    }

    /// <summary>
    /// 同点者がいて、席順によって計算結果が変わるかを判定する。
    /// ポイント折半では、同点者内の並び順を全通り試して実際に計算し、結果が変わる場合だけ席順の入力を求める
    /// </summary>
    public bool CheckIfTieBreakerNeeded(RuleSet rules, List<int> activeScores, List<bool> activeYaki, List<bool> activeTobiBonus)
    {
        var tieGroups = activeScores.Select((score, index) => new { score, index })
            .GroupBy(x => x.score)
            .OrderByDescending(g => g.Key)
            .Select(g => g.Select(x => x.index).ToList())
            .ToList();

        if (!tieGroups.Any(g => g.Count > 1)) return false;
        if (rules.TieBreaker == TieBreakerRule.Kamicha) return true;

        string? baseline = null;
        foreach (var order in EnumerateTieOrders(tieGroups))
        {
            var priority = order.Select((index, rank) => new { index, rank }).ToDictionary(x => x.index, x => x.rank);
            var result = Calculate(rules, activeScores, activeYaki, activeTobiBonus, priority);
            string outcome = string.Join(",", result.Points.Select(p => Math.Round(p, 1))) + "|" + string.Join(",", result.Yen);
            if (baseline == null) baseline = outcome;
            else if (outcome != baseline) return true;
        }
        return false;
    }

    // 素点の高い順に並んだ同点グループごとに、グループ内の並びを全通り組み合わせた席順を列挙する
    private static IEnumerable<List<int>> EnumerateTieOrders(List<List<int>> tieGroups)
    {
        IEnumerable<List<int>> orders = new[] { new List<int>() };
        foreach (var group in tieGroups)
            orders = orders.SelectMany(o => Permutations(group).Select(p => o.Concat(p).ToList())).ToList();
        return orders;
    }

    private static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count <= 1)
        {
            yield return new List<int>(items);
            yield break;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, k) => k != i).ToList();
            foreach (var p in Permutations(rest))
            {
                p.Insert(0, items[i]);
                yield return p;
            }
        }
    }

    /// <summary>
    /// ルール・素点・焼き鳥・トビ賞・席順から、ポイントと収支を計算する。
    /// priority は同点時の順位（値が小さいほど上位）
    /// </summary>
    public CalculationResult Calculate(RuleSet rules, List<int> activeScores, List<bool> activeYaki, List<bool> activeTobiBonus, Dictionary<int, int> priority)
    {
        int pCount = rules.PlayerCount;
        double[] baseUmaOka = new double[pCount];
        double oka = (rules.ReturnPoints - rules.StartingPoints) * pCount / 1000.0;
        baseUmaOka[0] += oka;

        double[] umaArray = rules.PlayerCount == 4
            ? new double[] { rules.CustomUma4_1, rules.CustomUma4_2, rules.CustomUma4_3, rules.CustomUma4_4 }
            : new double[] { rules.CustomUma3_1, rules.CustomUma3_2, rules.CustomUma3_3 };

        for (int rank = 0; rank < pCount; rank++)
            baseUmaOka[rank] += umaArray[rank];

        double[] points = new double[pCount];
        for (int i = 0; i < pCount; i++)
        {
            double rawPt = (activeScores[i] - rules.ReturnPoints) / 1000.0;
            points[i] = RoundToTenth(rawPt);
        }

        var rankedIndices = activeScores.Select((score, index) => new { score, index })
            .OrderByDescending(x => x.score)
            .ThenBy(x => priority[x.index])
            .Select(x => x.index)
            .ToList();

        int currentR = 0;
        while (currentR < pCount)
        {
            int score = activeScores[rankedIndices[currentR]];
            int tieCount = 1;
            for (int i = currentR + 1; i < pCount; i++)
            {
                if (activeScores[rankedIndices[i]] == score) tieCount++;
                else break;
            }
            if (rules.TieBreaker == TieBreakerRule.Split && tieCount > 1)
            {
                double sumUmaOka = 0;
                for (int i = 0; i < tieCount; i++) sumUmaOka += baseUmaOka[currentR + i];
                double splitBase = Math.Floor(sumUmaOka / tieCount * 10) / 10.0;
                double remainder = Math.Round(sumUmaOka - (splitBase * tieCount), 1);
                int remainderUnits = (int)Math.Round(remainder * 10);
                for (int i = 0; i < tieCount; i++)
                {
                    double addPt = splitBase + (i < remainderUnits ? 0.1 : 0);
                    points[rankedIndices[currentR + i]] += addPt;
                }
            }
            else
            {
                for (int i = 0; i < tieCount; i++) points[rankedIndices[currentR + i]] += baseUmaOka[currentR + i];
            }
            currentR += tieCount;
        }

        AdjustFractions(points, rankedIndices);

        // --- 沈みウマ ---
        double[] preSinkPts = points.ToArray();
        if (rules.IsSinkEnabled)
        {
            int borderPoint = rules.IsSinkBaseCustom ? rules.SinkUmaBasePoint : rules.StartingPoints;
            var ukiIndices = new List<int>();
            var shizumiIndices = new List<int>();
            for (int i = 0; i < pCount; i++)
            {
                bool isUki = rules.SinkJustIsUki ? activeScores[i] >= borderPoint : activeScores[i] > borderPoint;
                if (isUki) ukiIndices.Add(i); else shizumiIndices.Add(i);
            }
            if (shizumiIndices.Count > 0 && ukiIndices.Count > 0)
            {
                int totalPenalty = shizumiIndices.Count * rules.SinkUmaPenalty;
                foreach (var i in shizumiIndices) points[i] -= rules.SinkUmaPenalty;
                if (rules.SinkUmaDistribution == PenaltyDistribution.WinnerTakeAll)
                {
                    int topScore = activeScores[rankedIndices[0]];
                    int topCount = 1;
                    for (int i = 1; i < pCount; i++)
                    {
                        if (activeScores[rankedIndices[i]] == topScore) topCount++;
                        else break;
                    }
                    if (rules.TieBreaker == TieBreakerRule.Split && topCount > 1)
                    {
                        double splitBase = Math.Floor((double)totalPenalty / topCount * 10) / 10.0;
                        double remainder = Math.Round(totalPenalty - (splitBase * topCount), 1);
                        int remainderUnits = (int)Math.Round(remainder * 10);
                        for (int i = 0; i < topCount; i++) points[rankedIndices[i]] += splitBase + (i < remainderUnits ? 0.1 : 0);
                        AdjustFractions(points, rankedIndices);
                    }
                    else points[rankedIndices[0]] += totalPenalty;
                }
                else
                {
                    double splitBase = Math.Floor((double)totalPenalty / ukiIndices.Count * 10) / 10.0;
                    double remainder = Math.Round(totalPenalty - (splitBase * ukiIndices.Count), 1);
                    int remainderUnits = (int)Math.Round(remainder * 10);
                    var orderedUki = rankedIndices.Where(idx => ukiIndices.Contains(idx)).ToList();
                    for (int j = 0; j < orderedUki.Count; j++) points[orderedUki[j]] += splitBase + (j < remainderUnits ? 0.1 : 0);
                }
            }
        }
        var currentSinkPts = points.Select((p, i) => Math.Round(p - preSinkPts[i], 1)).ToList();

        // --- 焼き鳥 ---
        double[] preYakiPts = points.ToArray();
        if (rules.IsYakiEnabled)
        {
            var yakiIndices = new List<int>();
            var safeIndices = new List<int>();
            for (int i = 0; i < pCount; i++)
            {
                if (activeYaki[i]) yakiIndices.Add(i);
                else safeIndices.Add(i);
            }
            if (yakiIndices.Count > 0 && yakiIndices.Count < pCount)
            {
                int totalPenalty = yakiIndices.Count * rules.YakiPenalty;
                foreach (var i in yakiIndices) points[i] -= rules.YakiPenalty;
                if (rules.YakiDistribution == PenaltyDistribution.WinnerTakeAll)
                {
                    int topScore = activeScores[rankedIndices[0]];
                    int topCount = 1;
                    for (int i = 1; i < pCount; i++)
                    {
                        if (activeScores[rankedIndices[i]] == topScore) topCount++;
                        else break;
                    }
                    if (rules.TieBreaker == TieBreakerRule.Split && topCount > 1)
                    {
                        double splitBase = Math.Floor((double)totalPenalty / topCount * 10) / 10.0;
                        double remainder = Math.Round(totalPenalty - (splitBase * topCount), 1);
                        int remainderUnits = (int)Math.Round(remainder * 10);
                        for (int i = 0; i < topCount; i++) points[rankedIndices[i]] += splitBase + (i < remainderUnits ? 0.1 : 0);
                        AdjustFractions(points, rankedIndices);
                    }
                    else points[rankedIndices[0]] += totalPenalty;
                }
                else
                {
                    double splitBase = Math.Floor((double)totalPenalty / safeIndices.Count * 10) / 10.0;
                    double remainder = Math.Round(totalPenalty - (splitBase * safeIndices.Count), 1);
                    int remainderUnits = (int)Math.Round(remainder * 10);
                    var orderedSafe = rankedIndices.Where(idx => safeIndices.Contains(idx)).ToList();
                    for (int j = 0; j < orderedSafe.Count; j++) points[orderedSafe[j]] += splitBase + (j < remainderUnits ? 0.1 : 0);
                }
            }
        }
        var yakiPts = points.Select((p, i) => Math.Round(p - preYakiPts[i], 1)).ToList();

        // --- トビ賞 ---
        double[] preTobiPts = points.ToArray();
        if (rules.IsTobiEnabled)
        {
            var tobiIndices = new List<int>();
            var bonusIndices = new List<int>();
            for (int i = 0; i < pCount; i++)
            {
                if (rules.TobiJustIsTobi ? activeScores[i] <= rules.TobiPoint : activeScores[i] < rules.TobiPoint) tobiIndices.Add(i);
                if (activeTobiBonus[i]) bonusIndices.Add(i);
            }
            if (tobiIndices.Count > 0 && bonusIndices.Count > 0)
            {
                int totalPenalty = tobiIndices.Count * rules.TobiPenalty;
                foreach (var i in tobiIndices) points[i] -= rules.TobiPenalty;

                double splitBase = Math.Floor((double)totalPenalty / bonusIndices.Count * 10) / 10.0;
                double remainder = Math.Round(totalPenalty - (splitBase * bonusIndices.Count), 1);
                int remainderUnits = (int)Math.Round(remainder * 10);
                var orderedBonus = rankedIndices.Where(idx => bonusIndices.Contains(idx)).ToList();
                for (int j = 0; j < orderedBonus.Count; j++) points[orderedBonus[j]] += splitBase + (j < remainderUnits ? 0.1 : 0);
            }
        }
        var tobiPts = points.Select((p, i) => Math.Round(p - preTobiPts[i], 1)).ToList();

        // --- コールド賞 ---
        double[] preColdPts = points.ToArray();
        if (rules.IsColdEnabled)
        {
            var coldIndices = new List<int>();
            var nonColdIndices = new List<int>();
            for (int i = 0; i < pCount; i++)
            {
                if (rules.ColdJustIsCold ? activeScores[i] >= rules.ColdPoint : activeScores[i] > rules.ColdPoint) coldIndices.Add(i);
                else nonColdIndices.Add(i);
            }
            if (coldIndices.Count > 0 && nonColdIndices.Count > 0)
            {
                int bonusPerColdPlayer = nonColdIndices.Count * rules.ColdBonus;
                int penaltyPerNonColdPlayer = coldIndices.Count * rules.ColdBonus;
                foreach (var i in coldIndices) points[i] += bonusPerColdPlayer;
                foreach (var i in nonColdIndices) points[i] -= penaltyPerNonColdPlayer;
            }
        }
        var currentColdPts = points.Select((p, i) => Math.Round(p - preColdPts[i], 1)).ToList();

        // --- 収支 ---
        // 端数の切り捨てで合計が0にならない分は、端数の大きい人から1ずつ配る
        double[] exactYen = new double[pCount];
        int[] baseYen = new int[pCount];
        double[] fractions = new double[pCount];
        int totalBaseYen = 0;
        for (int i = 0; i < pCount; i++)
        {
            exactYen[i] = points[i] * rules.Rate;
            baseYen[i] = (int)Math.Floor(exactYen[i]);
            fractions[i] = exactYen[i] - baseYen[i];
            totalBaseYen += baseYen[i];
        }
        int shortage = 0 - totalBaseYen;
        var yenDistributionOrder = Enumerable.Range(0, pCount)
            .OrderByDescending(i => fractions[i])
            .ThenBy(i => rankedIndices.IndexOf(i))
            .ToList();
        for (int i = 0; i < shortage; i++) baseYen[yenDistributionOrder[i]] += 1;

        List<bool> tobiPlayers = Enumerable.Repeat(false, pCount).ToList();
        for (int i = 0; i < pCount; i++)
        {
            tobiPlayers[i] = rules.TobiJustIsTobi ? activeScores[i] <= rules.TobiPoint : activeScores[i] < rules.TobiPoint;
        }

        return new CalculationResult
        {
            Points = points.ToList(),
            Yen = baseYen.ToList(),
            SinkPoints = currentSinkPts,
            YakiPoints = yakiPts,
            TobiPoints = tobiPts,
            ColdPoints = currentColdPts,
            TobiPlayers = tobiPlayers
        };
    }
}

/// <summary>
/// 1局分の計算結果（各リストは素点と同じ並び）
/// </summary>
public class CalculationResult
{
    public List<double> Points { get; set; } = new();
    public List<int> Yen { get; set; } = new();
    public List<double> SinkPoints { get; set; } = new();
    public List<double> YakiPoints { get; set; } = new();
    public List<double> TobiPoints { get; set; } = new();
    public List<double> ColdPoints { get; set; } = new();
    public List<bool> TobiPlayers { get; set; } = new();
}