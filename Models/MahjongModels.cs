namespace MahjongApp.Models;

public class RuleSet
{
    public string Name { get; set; } = "デフォルト設定";
    public int PlayerCount { get; set; } = 4;
    public int StartingPoints { get; set; } = 25000;
    public int ReturnPoints { get; set; } = 30000;
    public int Rate { get; set; } = 50; // 円/1000点
    public int Uma1 { get; set; } = 20; // 4麻:1位、3麻:1位
    public int Uma2 { get; set; } = 10; // 4麻:2位、3麻:なし
    public int ChipRate { get; set; } = 100; // 円/枚
}

public class GameResult
{
    public List<string> PlayerNames { get; set; } = new(); // ★追加
    public List<int> RawScores { get; set; } = new();
    public List<double> Points { get; set; } = new();
    public List<int> Yen { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}