using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MahjongApp.Models;

public enum TieBreakerRule
{
    Kamicha, // 上家取り
    Split    // ポイント折半
}

public enum PenaltyDistribution
{
    WinnerTakeAll, // 1着総取り
    Split          // 分配
}

/// <summary>
/// 対局ルール。プリセットとして保存し、確定したセッションにも当時の内容を残す
/// </summary>
public class RuleSet
{
    // --- 基本ルール ---
    public string Name { get; set; } = "デフォルト設定";
    public int PlayerCount { get; set; } = 4;
    public int StartingPoints { get; set; } = 25000;
    public int ReturnPoints { get; set; } = 30000;
    public int Uma1 { get; set; } = 10;
    public int Uma2 { get; set; } = 20;
    public int Rate { get; set; } = 50;
    public int ChipRate { get; set; } = 100;
    public string Memo { get; set; } = ""; // 集計に関係しないルールのメモ（抜き牌・赤の枚数など）

    // --- 順位ウマの個別設定（カスタムルール） ---
    // 計算には常に CustomUma* を使う。個別設定でないときは Uma1/Uma2 から同期する
    public bool IsCustomUma { get; set; } = false;

    // 4麻用カスタムウマ
    public int CustomUma4_1 { get; set; } = 20;
    public int CustomUma4_2 { get; set; } = 10;
    public int CustomUma4_3 { get; set; } = -10;
    public int CustomUma4_4 { get; set; } = -20;

    // 3麻用カスタムウマ
    public int CustomUma3_1 { get; set; } = 30;
    public int CustomUma3_2 { get; set; } = -10;
    public int CustomUma3_3 { get; set; } = -20;

    // --- 同点の処理 ---
    public TieBreakerRule TieBreaker { get; set; } = TieBreakerRule.Kamicha;

    // --- 沈みウマ ---
    public bool IsSinkEnabled { get; set; } = false;
    public bool IsSinkBaseCustom { get; set; } = false;
    public int SinkUmaBasePoint { get; set; } = 30000;
    public bool SinkJustIsUki { get; set; } = true;
    public PenaltyDistribution SinkUmaDistribution { get; set; } = PenaltyDistribution.WinnerTakeAll;
    public int SinkUmaPenalty { get; set; } = 10;

    // --- 焼き鳥 ---
    public bool IsYakiEnabled { get; set; } = false;
    public PenaltyDistribution YakiDistribution { get; set; } = PenaltyDistribution.Split;
    public int YakiPenalty { get; set; } = 10;

    // --- トビ賞 ---
    public bool IsTobiEnabled { get; set; } = false;
    public int TobiPoint { get; set; } = 0;
    public bool TobiJustIsTobi { get; set; } = true;
    public int TobiPenalty { get; set; } = 10;

    // --- コールド賞 ---
    public bool IsColdEnabled { get; set; } = false;
    public int ColdPoint { get; set; } = 60000;
    public bool ColdJustIsCold { get; set; } = true;
    public int ColdBonus { get; set; } = 10;

    /// <summary>
    /// 通常のウマ（Uma1/Uma2）を、計算に使う順位ごとのウマ（CustomUma*）に反映する。個別設定中は手入力の値を残す
    /// </summary>
    public void SyncCustomUmaFromNormal()
    {
        if (IsCustomUma) return;
        if (PlayerCount == 4)
        {
            CustomUma4_1 = Uma2;
            CustomUma4_2 = Uma1;
            CustomUma4_3 = -Uma1;
            CustomUma4_4 = -Uma2;
        }
        else if (PlayerCount == 3)
        {
            // 三麻は2位・3位の両方から1位が受け取る
            CustomUma3_1 = Uma1 + Uma2;
            CustomUma3_2 = -Uma1;
            CustomUma3_3 = -Uma2;
        }
    }

    /// <summary>
    /// 順位ウマの個別設定をやめ、通常のウマで計算するよう戻す
    /// </summary>
    public void DisableCustomUma()
    {
        IsCustomUma = false;
        SyncCustomUmaFromNormal();
    }

    /// <summary>
    /// 内容を比べるためのJSON。順位ごとのウマは通常のウマから揃え直す（初期のプリセットには、揃っていないまま保存されたものがあるため）
    /// </summary>
    public string ToComparableJson(bool ignoreName = false, bool ignoreMemo = false)
    {
        var copy = DeepClone();
        copy.SyncCustomUmaFromNormal();
        if (ignoreName) copy.Name = "";
        if (ignoreMemo) copy.Memo = "";
        return JsonSerializer.Serialize(copy);
    }

    // 編集用のコピー（ダイアログをキャンセルしたら破棄する）
    public RuleSet DeepClone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<RuleSet>(json) ?? new RuleSet();
    }

    // 順位ウマの表示用テキスト
    public string GetUmaDisplayText(bool isDetailed = false)
    {
        if (!IsCustomUma)
        {
            return $"{Uma1}-{Uma2}";
        }
        string Format(int v) => v > 0 ? $"+{v}" : v.ToString();

        if (PlayerCount == 4)
        {
            if (isDetailed)
                return $" {Format(CustomUma4_1)}     {Format(CustomUma4_2)}     {Format(CustomUma4_3)}     {Format(CustomUma4_4)}";
            else
                return $"{Format(CustomUma4_1)}/{Format(CustomUma4_2)}/{Format(CustomUma4_3)}/{Format(CustomUma4_4)}";
        }
        else
        {
            if (isDetailed)
                return $" {Format(CustomUma3_1)}     {Format(CustomUma3_2)}     {Format(CustomUma3_3)}";
            else
                return $"{Format(CustomUma3_1)}/{Format(CustomUma3_2)}/{Format(CustomUma3_3)}";
        }
    }
}

/// <summary>
/// 入力中セッションの1局分の結果。各リストは入力欄の枠の並び（抜け番は RawScores が null）
/// </summary>
public class GameResult
{
    public List<string> PlayerNames { get; set; } = new();
    public List<int?> RawScores { get; set; } = new();
    public List<double> Points { get; set; } = new();
    public List<int> Yen { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // --- 特殊ルールの発生状況と、それぞれで動いたポイント（表示切替用） ---
    public List<bool> YakiPlayers { get; set; } = new();          // 焼き鳥になった人
    public List<bool> TobiPlayers { get; set; } = new();          // トんだ人
    public List<bool> TobiBonusRecipients { get; set; } = new();  // トバした人
    public List<double> SinkPoints { get; set; } = new();         // 沈みウマで動いたポイント
    public List<double> YakiPoints { get; set; } = new();         // 焼き鳥で動いたポイント
    public List<double> TobiPoints { get; set; } = new();         // トビ賞で動いたポイント
    public List<double> ColdPoints { get; set; } = new();         // コールドで動いたポイント
}

/// <summary>
/// 名簿（IndexedDB）。履歴がある人を削除すると、Id "deleted" の「削除済み」に統合される
/// </summary>
public class PlayerProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CumulativeChips { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 確定済みの1局分の成績（IndexedDB）。各リストは PlayerIds と同じ並び
/// </summary>
public class SavedGameRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }                        // 3: 三麻 / 4: 四麻
    public List<string?> PlayerIds { get; set; } = new();
    public List<int?> RawScores { get; set; } = new();          // 百点単位。抜け番は null
    public List<double> Points { get; set; } = new();
    public List<int> Yen { get; set; } = new();
    public List<double> SinkPoints { get; set; } = new();
    public List<double> YakiPoints { get; set; } = new();
    public List<double> TobiPoints { get; set; } = new();
    public List<double> ColdPoints { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 確定済みのセッション（IndexedDB）
/// </summary>
public class SavedSessionRecord
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Now;
    public int Rate { get; set; } = 0;                              // 当時のレート（/1000点）
    public int ChipRate { get; set; } = 0;                          // 当時のチップレート（/枚）
    public List<string> ParticipantPlayerIds { get; set; } = new();
    public List<int> SettledChips { get; set; } = new();            // ParticipantPlayerIds と同じ並びのチップ枚数
    public RuleSet? Rules { get; set; }                             // 当時のルール
}

/// <summary>
/// ランキング用の個人成績（StatsCacheService が集計する）
/// </summary>
public class PlayerStats
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalGames { get; set; }
    public double TotalPoint { get; set; }
    public int TotalGameYen { get; set; }
    public int TotalChipYen { get; set; }
    public int TotalYen => TotalGameYen + TotalChipYen;
    public int[] RankCounts { get; set; } = new int[4];
    public double AverageRank => TotalGames == 0 ? 0 : RankCounts.Select((count, index) => count * (index + 1)).Sum() / (double)TotalGames;
    public double FirstPlaceRate => TotalGames == 0 ? 0 : (double)RankCounts[0] / TotalGames * 100;
    public double AvoidLastPlaceRate(int playerCount)
    {
        if (TotalGames == 0) return 0;
        int safeIndex = Math.Max(0, Math.Min(playerCount - 1, RankCounts.Length - 1));
        return (1.0 - (double)RankCounts[safeIndex] / TotalGames) * 100;
    }
}

/// <summary>
/// 相性分析用の、ある相手との対戦成績
/// </summary>
public class MatchupStat
{
    public string OpponentName { get; set; } = "";
    public int GamesTogether { get; set; }
    public double HeroNetPoint { get; set; }
    public int HeroNetYen { get; set; }
    public int HeroWins { get; set; }
    public int HeroLosses { get; set; }
    public double DirectWinRate => (HeroWins + HeroLosses) == 0 ? 0 : (double)HeroWins / (HeroWins + HeroLosses) * 100;
}

/// <summary>
/// 端末の OS と、ホーム画面から起動しているか（wwwroot/js/pwa.js で判定）
/// </summary>
public class EnvironmentInfo
{
    public bool IsIOS { get; set; }
    public bool IsAndroid { get; set; }
    public bool IsWindows { get; set; }
    public bool IsPwa { get; set; }
}

/// <summary>
/// 収支の表示単位（例: G、円、pt）
/// </summary>
public class CurrencySetting
{
    public string Unit { get; set; } = "G";
    public bool IsPrefix { get; set; } = false; // true なら「G100」、false なら「100G」

    public string Render(int value, bool showPlus = false)
    {
        string sign = (showPlus && value > 0) ? "+" : (value < 0 ? "-" : "");
        string num = Math.Abs(value).ToString("N0");
        return IsPrefix ? $"{sign}{Unit}{num}" : $"{sign}{num}{Unit}";
    }
    public string Render(double value, bool showPlus = false, string format = "N1")
    {
        string sign = (showPlus && value > 0) ? "+" : (value < 0 ? "-" : "");
        string num = Math.Abs(value).ToString(format);
        return IsPrefix ? $"{sign}{Unit}{num}" : $"{sign}{num}{Unit}";
    }
}