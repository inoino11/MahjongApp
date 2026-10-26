namespace MahjongApp;

// localStorageのキー（値を変えると保存済みデータが読めなくなるので変更しないこと）
public static class StorageKeys
{
    public const string Rules = "rules";
    public const string CurrentPlayers = "currentPlayers";
    public const string ActiveParticipantCount = "activeParticipantCount";
    public const string RulePresets = "rulePresets";
    public const string ActiveHistory = "ActiveHistory";
    public const string ActiveSessionChips = "ActiveSessionChips";
    public const string BackupScoreBuffers = "backup_score_buffers";
    public const string TempTotalFee = "temp_total_fee";
    public const string TempFeeRule = "temp_fee_rule";
    public const string IsColorInverted = "isColorInverted";
    public const string IsDarkMode = "isDarkMode";
    public const string CurrencySetting = "currencySetting";
    public const string LiveShare = "liveShare";
    public const string LastBackupAt = "lastBackupAt";
}