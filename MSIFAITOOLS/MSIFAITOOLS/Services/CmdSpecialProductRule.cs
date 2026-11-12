using System;
using System.Collections.Generic;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】特注品判定：<b>図番（PDF 文件名去掉扩展名）的第 5 个字符是 Z</b>
    /// （例：<c>H310Z10004</c>）时，该 PDF 读到的部品一律按「特注品」处理。
    ///
    /// <para>
    /// 用法：第 1 / 2 / 3 步写库前调用 <see cref="MarkSpecialProducts"/>，把该図番 的行
    /// 「特注」列写成 <see cref="PartBasicInfo.SpecialProductMark"/>（=「特注品」）。
    /// 「特注」=「特注品」的行与「特注」= 1 的行一样<b>不清空数量 / 切断寸法</b>
    /// （见 <see cref="PartBasicInfo.ClearQuantityUnlessSpecial"/>）。
    /// </para>
    /// <para>
    /// 为什么写「特注品」而不是 1：画面上要能一眼看出这一行是因为図番 带 Z 才成为特注的
    /// （tz.json 的追加対応部品仍写 1，两者都算特注）。
    /// </para>
    /// <para>
    /// 只对 PDF（図番 = <see cref="PdfClassification.Stem"/>）判定，Excel（tz.json）侧不判定；
    /// 図番 不足 5 位 / 第 5 位不是 Z 时不标记，行为与以前完全一样。
    /// </para>
    /// </summary>
    internal static class CmdSpecialProductRule
    {
        /// <summary>判定位置（从 1 起）：図番 的第 5 个字符。</summary>
        public const int DrawingNoPosition = 5;

        /// <summary>特注品的记号（半角大写；判定时半角小写 z / 全角Ｚ 也认）。</summary>
        public const char DrawingNoMark = 'Z';

        /// <summary>「特注」列写进去的文字（= <see cref="PartBasicInfo.SpecialProductMark"/>）。</summary>
        public const string SpecialProductMark = PartBasicInfo.SpecialProductMark;

        /// <summary>
        /// 図番 的第 5 个字符是不是 <see cref="DrawingNoMark"/>
        /// （<c>H310Z10004</c> → true、<c>H310U10004</c> → false）。
        /// 半角小写 z / 全角Ｚ 都算；前后空白不影响判定。
        /// </summary>
        public static bool IsSpecialProductDrawingNo(string drawingNo)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                return false;
            }

            string value = drawingNo.Trim();

            if (value.Length < DrawingNoPosition)
            {
                return false;
            }

            char mark = char.ToUpperInvariant(value[DrawingNoPosition - 1]);

            return mark == DrawingNoMark || mark == 'Ｚ';
        }

        /// <summary>
        /// 図番 第 5 位是 Z 时，把这些行的「特注」列写成「特注品」。
        /// <b>已经有值的行（1 / 特注品 / 手工填的内容）不覆盖</b>，重复执行不会改变已有内容。
        /// <para>
        /// 判定优先用<b>行自己的「図番」</b>（<see cref="PartBasicInfo.DrawingNo"/> = PDF 文件名），
        /// 行上没有図番 时才退回参数 <paramref name="drawingNo"/>。
        /// </para>
        /// </summary>
        /// <param name="parts">本次要写库的行（第 1 / 2 / 3 步）。</param>
        /// <param name="drawingNo">図番（PDF 文件名去掉扩展名；行上没有図番 时用）。</param>
        /// <returns>本次写入「特注品」的行数；没有特注品的行时返回 0。</returns>
        public static int MarkSpecialProducts(IList<PartBasicInfo> parts, string drawingNo)
        {
            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            int marked = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part == null || !string.IsNullOrWhiteSpace(part.SpecialNote))
                {
                    continue;
                }

                string rowDrawingNo = string.IsNullOrWhiteSpace(part.DrawingNo)
                    ? drawingNo
                    : part.DrawingNo;

                if (!IsSpecialProductDrawingNo(rowDrawingNo))
                {
                    continue;
                }

                part.SpecialNote = SpecialProductMark;
                marked++;
            }

            return marked;
        }
    }
}
