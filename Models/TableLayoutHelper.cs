using System;
using System.Globalization;

namespace MahjongApp.Models // ※プロジェクトの構成に合わせて適宜変更してください
{
    public static class TableLayoutHelper
    {
        // 全ページ共通の文字幅(ch)計算ロジック
        public static double CalculateWidthInCh(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double width = 0;
            foreach (char c in s)
            {
                if (c == '\uFE0F' || c == '\uFE0E' || c == '\u200D') continue;
                
                // 【修正】文字ごとの幅見積もりを実態（フォントの描画幅）に近づけました
                if (c == '.') width += 0.4;
                else if (c == ',' || c == ' ') width += 0.4;   // 半角スペースやカンマは狭く見積もる
                else if (c == '+' || c == '-') width += 0.6;   // ハイフンや符号も1chより狭い
                else if (c == '%') width += 1.2;
                else if (char.IsDigit(c)) width += 1.0;        // 数字はtabular-numsで1ch
                else if (c >= 0x00 && c <= 0x7F) width += 0.8; // その他の半角英字なども少し細めに見積もる
                else if (char.IsSurrogate(c)) width += 1.0;
                else width += 2.0;                             // 漢字などの全角文字
            }
            return width;
        }

        // Td/Th（セル全体と中央揃え）用のスタイル生成
        public static string GetTdStyle(double widthCh, string extraStyle = "")
        {
            string w = widthCh.ToString("F2", CultureInfo.InvariantCulture);
            // 【修正】MudTableのデフォルト左揃えを確実に上書きするため text-align: center !important; を付与
            return $"text-align: center !important; min-width: {w}ch; padding-left: 0 !important; padding-right: 0 !important; box-sizing: border-box; {extraStyle}";
        }

        // 内部データ（右揃えとResultTable準拠の文字縮小）用のスタイル生成
        public static string GetDataStyle(double widthCh, string extraStyle = "")
        {
            string w = widthCh.ToString("F2", CultureInfo.InvariantCulture);
            return $"display: block; margin: 0 auto; width: {w}ch; text-align: right; font-variant-numeric: tabular-nums; font-size: 0.85rem; letter-spacing: -0.5px; {extraStyle}";
        }
    }

    // テーブルの列幅状態を管理するクラス（他ページでも流用可能）
    public class TableLayoutManager
    {
        private readonly double[] _colWidths;
        private readonly double[] _dataWidths;

        public TableLayoutManager(int columnCount)
        {
            _colWidths = new double[columnCount];
            _dataWidths = new double[columnCount];
        }

        // データの最大幅を更新（同時に列全体の幅も更新）
        public void UpdateDataWidth(int colIndex, string text)
        {
            double w = TableLayoutHelper.CalculateWidthInCh(text);
            if (w > _dataWidths[colIndex]) _dataWidths[colIndex] = w;
            if (w > _colWidths[colIndex]) _colWidths[colIndex] = w;
        }

        // ヘッダーの幅を更新（列全体の幅のみ更新）
        public void UpdateHeaderWidth(int colIndex, string text)
        {
            double w = TableLayoutHelper.CalculateWidthInCh(text);
            if (w > _colWidths[colIndex]) _colWidths[colIndex] = w;
        }

        // Td/Th用のスタイルを取得
        public string GetTdStyle(int colIndex, string extraStyle = "")
        {
            return TableLayoutHelper.GetTdStyle(_colWidths[colIndex], extraStyle);
        }

        // 数値データ用のスタイルを取得
        public string GetDataStyle(int colIndex, string extraStyle = "")
        {
            return TableLayoutHelper.GetDataStyle(_dataWidths[colIndex], extraStyle);
        }
    }
}