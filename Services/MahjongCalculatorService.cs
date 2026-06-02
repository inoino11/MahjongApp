using System;
using System.Collections.Generic;
using System.Linq;
using MahjongApp.Models;

namespace MahjongApp.Services;

public class MahjongCalculatorService
{
    /// <summary>
    /// ルール設定に応じたポイントの端数丸め処理を行います
    /// </summary>
    public double ApplyRounding(double value, RoundingRule rule) => rule switch
    {
        RoundingRule.ShishaGonyu => Math.Round(value, 1, MidpointRounding.AwayFromZero),
        RoundingRule.RoundDown => Math.Floor(value * 10) / 10.0,
        RoundingRule.RoundUp => Math.Ceiling(value * 10) / 10.0,
        RoundingRule.GoshaRokunyu => Math.Floor((value * 10) + 0.4) / 10.0,
        _ => value
    };

    /// <summary>
    /// 合計ptが0にならない場合の微調整を、順位が上のプレイヤーから順に行います
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
    /// 同点者が存在し、かつポイント折半やペナルティ分配時にタイブレーカー（席順決定）が必要かを判定します
    /// </summary>
    public bool CheckIfTieBreakerNeeded(RuleSet rules, List<int> activeScores, List<bool> activeYaki)
    {
        int pCount = rules.PlayerCount;
        var groupedScores = activeScores.Select((score, index) => new { score, index })
            .GroupBy(x => x.score)
            .OrderByDescending(g => g.Key)
            .ToList();

        if (!groupedScores.Any(g => g.Count() > 1)) return false;
        if (rules.TieBreaker == TieBreakerRule.Kamicha) return true;

        double[] baseUmaOka = new double[pCount];
        double oka = (rules.ReturnPoints - rules.StartingPoints) * pCount / 1000.0;
        baseUmaOka[0] += oka;
        
        double[] umaArray = rules.PlayerCount == 4
            ? new double[] { rules.CustomUma4_1, rules.CustomUma4_2, rules.CustomUma4_3, rules.CustomUma4_4 }
            : new double[] { rules.CustomUma3_1, rules.CustomUma3_2, rules.CustomUma3_3 };

        for (int rank = 0; rank < pCount; rank++)
            baseUmaOka[rank] += umaArray[rank];

        double totalPts = 0;
        for (int i = 0; i < pCount; i++)
        {
            double rawPt = (activeScores[i] - rules.ReturnPoints) / 1000.0;
            totalPts += ApplyRounding(rawPt, rules.Rounding);
        }
        int fractionsToAdjust = (int)Math.Round(-(totalPts + oka) * 10);

        int currentRank = 0;
        foreach (var group in groupedScores)
        {
            int count = group.Count();
            if (count > 1)
            {
                if (fractionsToAdjust > currentRank && fractionsToAdjust < currentRank + count) return true;
                double sumUmaOka = 0;
                for (int i = 0; i < count; i++) sumUmaOka += baseUmaOka[currentRank + i];
                double splitBase = Math.Floor(sumUmaOka / count * 10) / 10.0;
                double remainder = Math.Round(sumUmaOka - (splitBase * count), 1);
                if (remainder > 0) return true;
            }
            currentRank += count;
        }

        int topCount = groupedScores.First().Count();
        if (topCount > 1)
        {
            if (rules.IsSinkEnabled && rules.SinkUmaDistribution == PenaltyDistribution.WinnerTakeAll)
            {
                int borderPoint = rules.IsSinkBaseCustom ? rules.SinkUmaBasePoint : rules.StartingPoints;
                int shizumiCount = activeScores.Count(s => rules.SinkJustIsUki ? s < borderPoint : s <= borderPoint);
                if (shizumiCount > 0 && shizumiCount < pCount && (shizumiCount * rules.SinkUmaPenalty * 10) % topCount != 0)
                    return true;
            }
            if (rules.IsYakiEnabled && rules.YakiDistribution == PenaltyDistribution.WinnerTakeAll)
            {
                int yakiCount = activeYaki.Count(y => y);
                if (yakiCount > 0 && yakiCount < pCount && (yakiCount * rules.YakiPenalty * 10) % topCount != 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ルール、素点、各種役フラグ、および席順優先度を元に、最終的なポイント・収支（円）を厳密に計算します
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
            points[i] = ApplyRounding(rawPt, rules.Rounding);
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

        // --- 沈みウマ計算 ---
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

        // --- 焼き鳥計算 ---
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

        // --- トビ計算 ---
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

        // --- コールド計算 ---
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

        // --- 収支（円）算出 ---
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