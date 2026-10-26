namespace MahjongApp.Models;

/// <summary>
/// テンキーで入力した足し算・引き算の式を計算する（例: 「300-50」→ 250）。素点・チップ・場代の入力で共用する
/// </summary>
public static class ScoreExpression
{
    /// <summary>
    /// 式を計算する。空欄や式として読めない入力は null。末尾の符号（入力途中）は無視する
    /// </summary>
    public static int? Evaluate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Replace(" ", "").Replace("　", "");
        try
        {
            int total = 0;
            string currentNumber = "";
            char currentOp = '+';
            foreach (char c in input)
            {
                if (c == '+' || c == '-')
                {
                    // 数字の前の符号（先頭のマイナスなど）
                    if (string.IsNullOrEmpty(currentNumber))
                    {
                        currentOp = c == '-' && currentOp == '+' ? '-' : c;
                        continue;
                    }
                    int val = int.Parse(currentNumber);
                    total = currentOp == '+' ? total + val : total - val;
                    currentNumber = "";
                    currentOp = c;
                }
                else if (char.IsAsciiDigit(c))
                {
                    currentNumber += c;
                }
                else
                {
                    return null;
                }
            }
            if (!string.IsNullOrEmpty(currentNumber))
            {
                int val = int.Parse(currentNumber);
                total = currentOp == '+' ? total + val : total - val;
            }
            return total;
        }
        catch
        {
            return null; // 桁あふれなどで読めない場合
        }
    }
}
