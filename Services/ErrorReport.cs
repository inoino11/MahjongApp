using System.Text;

namespace MahjongApp.Services;

/// <summary>
/// エラー報告で送る文を作る。利用者には送る前にこの文をそのまま見せる
/// </summary>
public static class ErrorReport
{
    public const int MaxStackLines = 15;
    public const int MaxLength = 1500;

    public static string Describe(Exception ex, string uri, string baseUri)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"画面: {PagePath(uri, baseUri)}");
        sb.AppendLine($"{ex.GetType().FullName}: {ex.Message}");
        if (ex.InnerException is { } inner)
            sb.AppendLine($"内側: {inner.GetType().FullName}: {inner.Message}");
        var frames = (ex.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxStackLines);
        foreach (var frame in frames) sb.AppendLine(frame);

        string text = sb.ToString().TrimEnd();
        return text.Length <= MaxLength ? text : text[..MaxLength] + "…";
    }

    /// <summary>
    /// 開いていた画面のパス。? と # 以降は送らない（共有ページの # 以降は暗号の鍵）
    /// </summary>
    public static string PagePath(string uri, string baseUri)
    {
        int cut = uri.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) uri = uri[..cut];
        string path = uri.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase)
            ? uri[baseUri.Length..]
            : Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.AbsolutePath.TrimStart('/') : "";
        return "/" + path;
    }
}
