using System;
using System.Globalization;

namespace MahjongApp.Models;

/// <summary>
/// 表の列幅を、中身の文字幅（ch）から計算する
/// </summary>
public static class TableLayoutHelper
{
    // 表示幅を ch 単位で見積もる（実際のフォントの描画幅に近づけた係数）
    public static double CalculateWidthInCh(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        double width = 0;
        foreach (char c in s)
        {
            if (c == '\uFE0F' || c == '\uFE0E' || c == '\u200D') continue;
            
            if (c == '.') width += 0.4;
            else if (c == ',' || c == ' ') width += 0.4;
            else if (c == '+' || c == '-') width += 0.6;
            else if (c == '%') width += 1.2;
            else if (char.IsDigit(c)) width += 1.0;        // 数字は tabular-nums で 1ch
            else if (c >= 0x00 && c <= 0x7F) width += 0.8; // その他の半角文字
            else if (char.IsSurrogate(c)) width += 1.0;
            else width += 2.0;                             // 全角文字
        }
        return width;
    }

    // セル（td/th）のスタイル
    public static string GetTdStyle(double widthCh, string extraStyle = "")
    {
        string w = widthCh.ToString("F2", CultureInfo.InvariantCulture);
        // MudTable の既定の左揃えを上書きするため !important を付ける
        return $"text-align: center !important; min-width: {w}ch; padding-left: 0 !important; padding-right: 0 !important; box-sizing: border-box; {extraStyle}";
    }

    // セル内の数値のスタイル（右揃え。文字サイズは ResultTable に合わせる）
    public static string GetDataStyle(double widthCh, string extraStyle = "")
    {
        string w = widthCh.ToString("F2", CultureInfo.InvariantCulture);
        return $"display: block; margin: 0 auto; width: {w}ch; text-align: right; font-variant-numeric: tabular-nums; font-size: 0.85rem; letter-spacing: -0.5px; {extraStyle}";
    }
}

/// <summary>
/// 1つの表の列幅を管理する。各列の最大幅を記録し、セルのスタイルを返す
/// </summary>
public class TableLayoutManager
{
    private readonly double[] _colWidths;
    private readonly double[] _dataWidths;

    public TableLayoutManager(int columnCount)
    {
        _colWidths = new double[columnCount];
        _dataWidths = new double[columnCount];
    }

    // データの幅を記録する（列全体の幅にも反映する）
    public void UpdateDataWidth(int colIndex, string text)
    {
        double w = TableLayoutHelper.CalculateWidthInCh(text);
        if (w > _dataWidths[colIndex]) _dataWidths[colIndex] = w;
        if (w > _colWidths[colIndex]) _colWidths[colIndex] = w;
    }

    // 見出しの幅を記録する（列全体の幅だけに反映する）
    public void UpdateHeaderWidth(int colIndex, string text)
    {
        double w = TableLayoutHelper.CalculateWidthInCh(text);
        if (w > _colWidths[colIndex]) _colWidths[colIndex] = w;
    }

    public string GetTdStyle(int colIndex, string extraStyle = "")
    {
        return TableLayoutHelper.GetTdStyle(_colWidths[colIndex], extraStyle);
    }

    public string GetDataStyle(int colIndex, string extraStyle = "")
    {
        return TableLayoutHelper.GetDataStyle(_dataWidths[colIndex], extraStyle);
    }
}