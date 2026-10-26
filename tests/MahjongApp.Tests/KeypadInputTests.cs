using MahjongApp.Models;

namespace MahjongApp.Tests;

// テンキーで入力する式と、キーを押したときの文字列の変化
public class KeypadInputTests
{
    [Theory]
    [InlineData("250", 250)]
    [InlineData("300-50", 250)]
    [InlineData("-120", -120)]
    [InlineData("100+50-30", 120)]
    [InlineData("1 2 3", 123)]
    [InlineData("1　0", 10)]
    [InlineData("300-", 300)]   // 入力途中の末尾の符号は無視
    [InlineData("0", 0)]
    public void 式を計算する(string input, int expected)
    {
        Assert.Equal(expected, ScoreExpression.Evaluate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("3*4")]
    [InlineData("１２")]               // 全角数字
    [InlineData("99999999999")]       // 桁あふれ
    public void 式として読めなければnull(string? input)
    {
        Assert.Null(ScoreExpression.Evaluate(input));
    }

    [Theory]
    [InlineData("", "3", "3")]
    [InlineData("25", "0", "250")]
    [InlineData("250", "⌫", "25")]
    [InlineData("", "⌫", "")]
    [InlineData("250", "C", "")]
    [InlineData("300", "-", "300-")]
    [InlineData("300-", "+", "300+")]  // 符号の連打は後から押した方にする
    [InlineData("0", "7", "7")]        // 先頭の0は消す
    [InlineData("", "0", "0")]
    [InlineData("10+0", "5", "10+5")]
    [InlineData("-", "0", "-0")]
    [InlineData("-0", "5", "-5")]
    public void キーを押した後の文字列(string current, string key, string expected)
    {
        Assert.Equal(expected, KeypadInput.Apply(current, key));
    }

    [Fact]
    public void キーを順に押して式を作れる()
    {
        string buffer = "";
        foreach (var key in new[] { "3", "0", "0", "-", "+", "-", "5", "0" })
            buffer = KeypadInput.Apply(buffer, key);
        Assert.Equal("300-50", buffer);
        Assert.Equal(250, ScoreExpression.Evaluate(buffer));
    }
}
