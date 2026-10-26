using System.Text.Json;
using MahjongApp.Models;

namespace MahjongApp.Services;

/// <summary>
/// 入力中のセッションを確定するときの記録（名簿・セッション・各局）を組み立てる（状態を持たない）
/// </summary>
public static class SessionRecordBuilder
{
    // 確定時に保存する記録。NewPlayers は名簿に新しく加わる人（PlayersToUpdate にも含まれる）
    public record Records(List<PlayerProfile> PlayersToUpdate, List<PlayerProfile> NewPlayers, SavedSessionRecord Session, List<SavedGameRecord> Games);

    // ルール名を「未設定」にして記録する理由
    public enum RuleNameIssue
    {
        None,
        Empty,              // ルール名が空
        ChangedFromPreset   // 同じ名前のプリセットから内容（メモ以外）が変わっている
    }

    /// <summary>
    /// 局の記録に素点が入っている人の名前を、出てきた順に返す（途中で入れ替わった人も含める）
    /// </summary>
    public static List<string> CollectPlayerNames(IEnumerable<GameResult> history)
    {
        var players = new List<string>();
        foreach (var game in history)
            for (int i = 0; i < game.PlayerNames.Count; i++)
                if (i < game.RawScores.Count && game.RawScores[i].HasValue)
                {
                    var name = game.PlayerNames[i];
                    if (!string.IsNullOrWhiteSpace(name) && !players.Contains(name)) players.Add(name);
                }
        return players;
    }

    public static RuleNameIssue CheckRuleName(RuleSet rules, IEnumerable<RuleSet> presets)
    {
        if (string.IsNullOrWhiteSpace(rules.Name)) return RuleNameIssue.Empty;
        var preset = presets.FirstOrDefault(p => p.Name == rules.Name);
        if (preset != null && RuleJsonWithoutMemo(preset) != RuleJsonWithoutMemo(rules)) return RuleNameIssue.ChangedFromPreset;
        return RuleNameIssue.None;
    }

    // 計算に関わるルールだけを比べるため、メモを除いたJSONにする。
    // 順位ごとのウマは通常のウマから揃え直す（初期のプリセットには、通常のウマと揃っていないものがあるため）
    private static string RuleJsonWithoutMemo(RuleSet r)
    {
        var copy = r.DeepClone();
        copy.Memo = "";
        copy.SyncCustomUmaFromNormal();
        return JsonSerializer.Serialize(copy);
    }

    /// <summary>
    /// 確定する記録を組み立てる。名簿にない名前は新しい人として作る。チップは chips（名前 → 枚数）から参加者の順に並べる
    /// </summary>
    public static Records Build(List<GameResult> history, RuleSet rules, Dictionary<string, int> chips, IReadOnlyList<PlayerProfile> playerMaster, DateTime now)
    {
        var names = CollectPlayerNames(history);
        var nameToId = new Dictionary<string, string>();
        var playersToUpdate = new List<PlayerProfile>();
        var newPlayers = new List<PlayerProfile>();
        foreach (var name in names)
        {
            var existing = playerMaster.FirstOrDefault(p => p.Name == name);
            if (existing != null)
            {
                nameToId[name] = existing.Id;
                playersToUpdate.Add(new PlayerProfile { Id = existing.Id, Name = existing.Name, CumulativeChips = existing.CumulativeChips, CreatedAt = existing.CreatedAt });
            }
            else
            {
                var profile = new PlayerProfile { Id = Guid.NewGuid().ToString(), Name = name, CumulativeChips = 0, CreatedAt = now };
                nameToId[name] = profile.Id;
                playersToUpdate.Add(profile);
                newPlayers.Add(profile);
            }
        }

        var sessionId = Guid.NewGuid().ToString();
        var games = history.Select(game => new SavedGameRecord
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            RuleName = rules.Name,
            PlayerCount = rules.PlayerCount,
            Timestamp = game.Timestamp,
            PlayerIds = game.PlayerNames.Select(name => !string.IsNullOrWhiteSpace(name) && nameToId.TryGetValue(name, out var id) ? id : null).ToList(),
            RawScores = game.RawScores,
            Points = game.Points,
            Yen = game.Yen,
            SinkPoints = game.SinkPoints != null ? new List<double>(game.SinkPoints) : new(),
            YakiPoints = game.YakiPoints != null ? new List<double>(game.YakiPoints) : new(),
            TobiPoints = game.TobiPoints != null ? new List<double>(game.TobiPoints) : new(),
            ColdPoints = game.ColdPoints != null ? new List<double>(game.ColdPoints) : new()
        }).ToList();

        var session = new SavedSessionRecord
        {
            SessionId = sessionId,
            Date = now,
            Rate = rules.Rate,
            ChipRate = rules.ChipRate,
            ParticipantPlayerIds = names.Select(n => nameToId[n]).ToList(),
            SettledChips = names.Select(n => chips.TryGetValue(n, out var c) ? c : 0).ToList(),
            Rules = rules.DeepClone()
        };
        return new Records(playersToUpdate, newPlayers, session, games);
    }
}
