using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MahjongApp.Models;

// --- 各種ルールの選択肢（UIバインディング用） ---

public enum TieBreakerRule
{
    Kamicha, // 上家取り
    Split    // ポイント折半
}

public enum RoundingRule
{
    GoshaRokunyu, // 五捨六入
    ShishaGonyu,  // 四捨五入
    RoundDown,    // 切り捨て
    RoundUp       // 切り上げ
}

public enum PenaltyDistribution
{
    WinnerTakeAll, // 1着総取り
    Split          // 分配
}

public class RuleSet
{
    // --- 基本ルール ---
    public string Name { get; set; } = "デフォルト設定";
    public int PlayerCount { get; set; } = 4;
    public int StartingPoints { get; set; } = 25000;
    public int ReturnPoints { get; set; } = 30000;
    public int Rate { get; set; } = 50; // 円/1000点
    public int Uma1 { get; set; } = 10; // 4麻:ウマ(2←3)、3麻:ウマ(1←2)
    public int Uma2 { get; set; } = 20; // 4麻:ウマ(1←4)、3麻:ウマ(1←3)
    public int ChipRate { get; set; } = 100; // 円/枚

    // ==========================================
    // 以下、カスタムルール用追加プロパティ
    // ==========================================

    // --- ウマ完全個別設定 ---
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

    // --- 同点・端数処理 ---
    public TieBreakerRule TieBreaker { get; set; } = TieBreakerRule.Kamicha;
    public RoundingRule Rounding { get; set; } = RoundingRule.GoshaRokunyu;

    // --- 沈みウマ ---
    public bool IsSunkenUmaEnabled { get; set; } = false;
    public bool IsSunkenBaseCustom { get; set; } = false;      // true: 入力値, false: 原点(StartingPoints)
    public int SunkenUmaBasePoint { get; set; } = 30000;       // カスタム時の基準点数
    public bool SunkenJustIsUki { get; set; } = true;          // true: ジャスト浮き, false: ジャスト沈み
    public PenaltyDistribution SunkenUmaDistribution { get; set; } = PenaltyDistribution.WinnerTakeAll;
    public int SunkenUmaPenalty { get; set; } = 10;

    // --- 焼き鳥ルール ---
    public bool IsYakitoriEnabled { get; set; } = false;
    public PenaltyDistribution YakitoriDistribution { get; set; } = PenaltyDistribution.Split;
    public int YakitoriPenalty { get; set; } = 10;

    // --- トビ賞 ---
    public bool IsTobiEnabled { get; set; } = false;
    public int TobiPoint { get; set; } = 0;
    public bool TobiJustIsTobi { get; set; } = true;           // true: ジャストトビ(0点でトビ), false: 続行(マイナスでトビ)
    public int TobiPenalty { get; set; } = 10;

    // --- コールド賞 ---
    public bool IsColdEnabled { get; set; } = false;
    public int ColdPoint { get; set; } = 60000;
    public bool ColdJustIsCold { get; set; } = true;           // true: ジャストコールド(6万点で終了), false: 続行
    public int ColdBonus { get; set; } = 10;

    // --- 安全なコピー（設定キャンセル時の破棄用） ---
    public RuleSet DeepClone()
    {
        // System.Text.Json を使った最も確実で簡潔なディープコピー
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<RuleSet>(json) ?? new RuleSet();
    }

    // --- 個別の順位ウマ表示用テキストを生成 ---
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

public class GameResult
{
    public List<string> PlayerNames { get; set; } = new();
    public List<int> RawScores { get; set; } = new();
    public List<double> Points { get; set; } = new();
    public List<int> Yen { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // ==========================================
    // 以下、イベント発生履歴（結果取り消し・再描画用）
    // ==========================================
    
    // インデックスはPlayerNamesと一致させる（trueの人が対象）
    public List<bool> YakitoriPlayers { get; set; } = new();      // 焼き鳥を食らった人
    public List<bool> TobiPlayers { get; set; } = new();          // トんだ人
    public List<bool> TobiBonusRecipients { get; set; } = new();  // トバした人（ダブロン等で複数人になる可能性を考慮してList）
    public List<double> YakitoriPoints { get; set; } = new(); // 焼き鳥で動いたポイント
    public List<double> TobiPoints { get; set; } = new();     // トビ賞で動いたポイント
}