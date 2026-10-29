using MahjongApp.Services;

namespace MahjongApp.Tests;

// エラー報告で送る文（利用者に見せてから送る）
public class ErrorReportTests
{
    private const string Base = "https://tagame.inoino11.com/";

    private static Exception Thrown(Func<Exception> make)
    {
        try { throw make(); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void 共有ページのシャープ以降の鍵は送らない()
    {
        Assert.Equal("/shared", ErrorReport.PagePath(Base + "shared?id=abc#key=SECRET", Base));
        var text = ErrorReport.Describe(Thrown(() => new InvalidOperationException("x")), Base + "shared#SECRET", Base);
        Assert.DoesNotContain("SECRET", text);
        Assert.StartsWith("画面: /shared", text);
    }

    [Fact]
    public void トップはスラッシュだけになり_別の場所ならパスだけを使う()
    {
        Assert.Equal("/", ErrorReport.PagePath(Base, Base));
        Assert.Equal("/settings", ErrorReport.PagePath("http://localhost:5260/settings?x=1", "http://localhost:5260/"));
        Assert.Equal("/privacy", ErrorReport.PagePath(Base + "privacy", Base));
        // 旧住所（リポジトリ名のパスの下）で開いたときも、アプリ内のパスだけになる
        Assert.Equal("/privacy", ErrorReport.PagePath("https://inoino11.github.io/MahjongApp/privacy", "https://inoino11.github.io/MahjongApp/"));
        Assert.Equal("/MahjongApp/privacy", ErrorReport.PagePath("https://example.com/MahjongApp/privacy", Base));
    }

    [Fact]
    public void 種類と文面と内側の例外とスタックトレースを含む()
    {
        var ex = Thrown(() => new InvalidOperationException("外側", new FormatException("内側の文面")));
        var text = ErrorReport.Describe(ex, Base + "settlement", Base);
        Assert.Contains("System.InvalidOperationException: 外側", text);
        Assert.Contains("内側: System.FormatException: 内側の文面", text);
        Assert.Contains("at ", text);
    }

    [Fact]
    public void 長い文面は上限で切る()
    {
        var ex = Thrown(() => new Exception(new string('あ', 5000)));
        var text = ErrorReport.Describe(ex, Base, Base);
        Assert.Equal(ErrorReport.MaxLength + 1, text.Length);
        Assert.EndsWith("…", text);
    }
}
