using MahjongApp.Services;

namespace MahjongApp.Tests;

// 観戦データに載せるチップ（合計が ±0 のときだけ採用する）
public class LiveShareTests
{
    [Fact]
    public void 合計が0なら今の値を採用する()
    {
        var last = new Dictionary<string, int> { ["あ"] = 0, ["い"] = 0 };
        var current = new Dictionary<string, int> { ["あ"] = 2, ["い"] = -2 };
        var chips = LiveShareService.SelectChips(current, last);
        Assert.Equal(current, chips);
        Assert.NotSame(current, chips); // 後で入力欄が変わっても影響しないよう複製する
    }

    [Fact]
    public void 入力途中で合計が合わないときは前に採用した値のまま()
    {
        var last = new Dictionary<string, int> { ["あ"] = 2, ["い"] = -2 };
        var current = new Dictionary<string, int> { ["あ"] = 2, ["い"] = -2, ["う"] = 3 };
        Assert.Same(last, LiveShareService.SelectChips(current, last));
    }

    [Fact]
    public void 内容が同じなら同じものを返す()
    {
        // 同じものを返すと送る内容が変わらないので、再送信も保存も起きない
        var last = new Dictionary<string, int> { ["あ"] = 1, ["い"] = -1 };
        var current = new Dictionary<string, int> { ["い"] = -1, ["あ"] = 1 };
        Assert.Same(last, LiveShareService.SelectChips(current, last));
    }

    [Fact]
    public void まだ採用したことがなければ今の値を使う()
    {
        var current = new Dictionary<string, int> { ["あ"] = 3 };
        Assert.Equal(current, LiveShareService.SelectChips(current, null));
    }
}
