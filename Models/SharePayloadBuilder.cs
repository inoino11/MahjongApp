namespace MahjongApp.Models;

/// <summary>
/// URL共有・観戦URLで送る対局データを組み立てる（キーを短縮した形式。共有ページの MinifiedPayload と対応）
/// </summary>
public static class SharePayloadBuilder
{
    public static object Build(RuleSet rules, List<string> tablePlayers, List<GameResult> history, Dictionary<string, int> sessionChips, CurrencySetting currency)
    {
        return new
        {
            r = rules,
            p = tablePlayers,
            cs = currency,
            c = tablePlayers.Select(p => sessionChips.TryGetValue(p, out var v) ? v : 0).ToList(),
            h = history.Select(g => new
            {
                t = g.Timestamp.Ticks,
                s = g.RawScores,
                pt = g.Points,
                yp = g.YakiPlayers,
                tb = g.TobiBonusRecipients,
                sp = g.SinkPoints,
                ypt = g.YakiPoints,
                tpt = g.TobiPoints,
                cp = g.ColdPoints,
                y = g.Yen
            }).ToList()
        };
    }

    public static string BuildJson(RuleSet rules, List<string> tablePlayers, List<GameResult> history, Dictionary<string, int> sessionChips, CurrencySetting currency)
        => System.Text.Json.JsonSerializer.Serialize(Build(rules, tablePlayers, history, sessionChips, currency));
}
