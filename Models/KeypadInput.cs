using System.Text.RegularExpressions;

namespace MahjongApp.Models;

/// <summary>
/// テンキー（Components/ScoreKeypad.razor）のキーを、入力中の文字列に反映する。素点・チップ・場代の入力で共用する
/// </summary>
public static partial class KeypadInput
{
    /// <summary>
    /// key を押した後の文字列を返す。「閉じる」「記録」などの操作キーは呼び出し側で処理する
    /// </summary>
    public static string Apply(string current, string key)
    {
        if (key == "C") return string.Empty;
        if (key == "⌫") return current.Length > 0 ? current[..^1] : current;
        // 符号が続いたら、後から押した符号で置き換える
        if (key is "+" or "-" && (current.EndsWith('+') || current.EndsWith('-'))) current = current[..^1];
        // 数字の先頭の余分な0は消す（「007」→「7」、「10+05」→「10+5」）
        return LeadingZeros().Replace(current + key, "$1");
    }

    [GeneratedRegex(@"(^|[-+])0+(?=[0-9])")]
    private static partial Regex LeadingZeros();
}
