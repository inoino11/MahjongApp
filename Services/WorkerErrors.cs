namespace MahjongApp.Services;

/// <summary>
/// Worker（共有・観戦・クラウドバックアップ・問い合わせの API）からのエラーを、利用者向けの文に直す
/// </summary>
public static class WorkerErrors
{
    // Worker は KV の書き込みが1日の上限（無料枠は UTC 0 時 = 日本時間 9 時に戻る）に達すると 429 を返す
    public const string DailyLimitMessage = "サーバーの今日の上限に達しました\n日本時間 9 時に回復します";

    public static bool IsDailyLimit(System.Net.HttpStatusCode? status) => status == System.Net.HttpStatusCode.TooManyRequests;
}
