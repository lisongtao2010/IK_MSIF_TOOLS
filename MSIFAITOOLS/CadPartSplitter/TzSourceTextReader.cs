using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CadPartSplitter
{
    /// <summary>
    /// 从「非 Excel 的特注品文件」读出来的一段文本（送 AI 判明细用）。
    /// </summary>
    internal sealed class TzSourceText
    {
        /// <summary>正文（已按 maxChars 截断）。</summary>
        public string Text { get; set; }

        /// <summary>内容类型说明（写进日志 / 提示词）：<c>PDF 文字层</c> / <c>文本文件</c>。</summary>
        public string Kind { get; set; }

        /// <summary>是否因为超长被截断（提示词里会注明，AI 不会以为文件就这么多内容）。</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// 把「第 5 位是 Z 的非 Excel 文件」摊成一段纯文本，交给 <see cref="TzFileAiExtractor"/> 判部品明细。
    ///
    /// <para>
    /// 为什么不能像 Excel 那样逐格核对：非 Excel 没有「行 / 列」结构，
    /// 所以这里只负责把**纸面上的字**按阅读顺序摊平，核对改成「这个值在全文里出现过没有」
    /// （见 <see cref="TzFileAiExtractor"/>）。
    /// </para>
    ///
    /// <para>
    /// 支持范围：
    /// <list type="bullet">
    ///   <item><b>PDF</b>：用 PdfPig 读文字层（与 <see cref="PartTextExtractor"/> 同一个库），
    ///         逐页按「行」拼文本；扫描件 / 文字已轮廓化的 PDF 没有文字层，返回 null 并记日志（不做 OCR）；</item>
    ///   <item><b>文本类</b>（<c>.csv</c> / <c>.txt</c> / <c>.tsv</c> / <c>.xml</c> / <c>.json</c> / <c>.md</c> / <c>.log</c>）：
    ///         直读全文（BOM 优先，其次 UTF-8 严格解码，再退回 932 日文代码页）；</item>
    ///   <item><b>其它</b>（图片、DXF / DWG、压缩包……）：不认，返回 null 并记日志。</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class TzSourceTextReader
    {
        /// <summary>送给 AI 的正文最小上限（与 <see cref="DetectorOptions.TzAiMaxGridChars"/> 的最小值保持一致）。</summary>
        private const int MinMaxChars = 2000;

        /// <summary>PDF 里判定「同一行」的 Y 容差（pt）；小于它的词算一行。</summary>
        private const double SameLineTolerance = 2.0;

        /// <summary>文本类扩展名（能直接读全文的）。</summary>
        private static readonly string[] TextExtensions =
        {
            ".csv", ".txt", ".tsv", ".xml", ".json", ".md", ".log", ".htm", ".html", ".ini"
        };

        /// <summary>图片扩展名（没有文字层，本工具不做 OCR）。</summary>
        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
        };

        /// <summary>是不是 PDF。</summary>
        public static bool IsPdf(string path)
        {
            return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>是不是能直读的文本类文件。</summary>
        public static bool IsTextFile(string path)
        {
            string ext = Path.GetExtension(path);

            for (int i = 0; i < TextExtensions.Length; i++)
            {
                if (string.Equals(ext, TextExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是不是图片（提示「没有文字层」时用）。</summary>
        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path);

            for (int i = 0; i < ImageExtensions.Length; i++)
            {
                if (string.Equals(ext, ImageExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读出一段送 AI 的文本。读不出来（格式不支持 / 没有文字层 / 读取失败）时返回 null，
        /// 原因写进日志 —— 不抛异常：单个文件读不动不该打断整批特注品.json。
        /// </summary>
        /// <param name="path">文件全路径。</param>
        /// <param name="maxChars">正文最多多少字符（超长截断并标记）。</param>
        /// <param name="log">日志回调（可为 null）。</param>
        public static TzSourceText Read(string path, int maxChars, Action<string> log)
        {
            Action<string> write = log ?? (_ => { });
            int limit = maxChars < MinMaxChars ? MinMaxChars : maxChars;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                write("[INFO] 特注品.json：文件不存在，跳过：" + (path ?? "空"));
                return null;
            }

            try
            {
                if (IsPdf(path))
                {
                    return ReadPdf(path, limit, write);
                }

                if (IsTextFile(path))
                {
                    return ReadTextFile(path, limit, write);
                }

                write("[INFO] 特注品.json：" + Path.GetFileName(path) + " 是"
                      + (IsImage(path)
                          ? "图片（没有文字层，本工具不做 OCR）"
                          : "二进制格式（不支持读取内容）")
                      + "，跳过；要收它的部品明细请另存成 Excel 或 PDF。");

                return null;
            }
            catch (Exception ex)
            {
                write("[WARN] 特注品.json：读不了 " + Path.GetFileName(path) + "（" + ex.GetType().Name
                      + "：" + ex.Message + "），跳过这个文件。");
                return null;
            }
        }

        /// <summary>读 PDF 文字层：逐页取词、按行拼文本。</summary>
        private static TzSourceText ReadPdf(string path, int limit, Action<string> write)
        {
            var builder = new StringBuilder();
            bool truncated = false;
            int wordCount = 0;

            using (PdfDocument document = PdfDocument.Open(path))
            {
                for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
                {
                    if (truncated)
                    {
                        break;
                    }

                    Page page = document.GetPage(pageNumber);
                    var words = new List<Word>();

                    foreach (Word word in page.GetWords())
                    {
                        if (word != null && !string.IsNullOrWhiteSpace(word.Text))
                        {
                            words.Add(word);
                        }
                    }

                    if (words.Count == 0)
                    {
                        continue;       // 这一页没有文字层（图形页），跳过但继续看后面的页
                    }

                    AppendLine(builder, "【第 " + pageNumber + " 页】", limit, ref truncated);

                    if (truncated)
                    {
                        break;
                    }

                    // 从上往下、从左往右：PdfPig 的 Y 轴向上，Bottom 越大越靠上
                    words.Sort(delegate(Word a, Word b)
                    {
                        if (Math.Abs(b.BoundingBox.Bottom - a.BoundingBox.Bottom) > SameLineTolerance)
                        {
                            return b.BoundingBox.Bottom.CompareTo(a.BoundingBox.Bottom);
                        }

                        return a.BoundingBox.Left.CompareTo(b.BoundingBox.Left);
                    });

                    var line = new StringBuilder();
                    double lineBottom = words[0].BoundingBox.Bottom;

                    foreach (Word word in words)
                    {
                        if (Math.Abs(word.BoundingBox.Bottom - lineBottom) > SameLineTolerance)
                        {
                            AppendLine(builder, line.ToString(), limit, ref truncated);

                            if (truncated)
                            {
                                break;
                            }

                            line.Length = 0;
                            lineBottom = word.BoundingBox.Bottom;
                        }

                        if (line.Length > 0)
                        {
                            line.Append(' ');
                        }

                        line.Append(CsvFile.SingleLine(word.Text).Trim());
                        wordCount++;
                    }

                    if (!truncated && line.Length > 0)
                    {
                        AppendLine(builder, line.ToString(), limit, ref truncated);
                    }
                }
            }

            if (wordCount == 0)
            {
                write("[INFO] 特注品.json：" + Path.GetFileName(path)
                      + " 的 PDF 没有文字层（扫描件或文字已轮廓化），跳过（本工具不做 OCR）。");
                return null;
            }

            return new TzSourceText
            {
                Text = builder.ToString().Trim(),
                Kind = "PDF 文字层（" + wordCount + " 个词）",
                Truncated = truncated
            };
        }

        /// <summary>读文本类文件全文（BOM → UTF-8 严格 → 932 日文代码页）。</summary>
        private static TzSourceText ReadTextFile(string path, int limit, Action<string> write)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length == 0)
            {
                write("[INFO] 特注品.json：" + Path.GetFileName(path) + " 是空文件，跳过。");
                return null;
            }

            string text = Decode(bytes);

            // 换行统一成 CRLF（AI 看到的分行更稳），再按上限截断
            text = text.Replace("\r\n", "\n").Replace('\r', '\n')
                       .Replace("\n", Environment.NewLine).Trim();

            if (text.Length == 0)
            {
                write("[INFO] 特注品.json：" + Path.GetFileName(path) + " 里没有可读的文字，跳过。");
                return null;
            }

            bool truncated = text.Length > limit;

            if (truncated)
            {
                text = text.Substring(0, limit);
            }

            return new TzSourceText
            {
                Text = text,
                Kind = "文本文件（" + Path.GetFileName(path) + "）",
                Truncated = truncated
            };
        }

        /// <summary>
        /// 解字节：有 BOM 按 BOM；否则先按 UTF-8 严格解码（解不开说明不是 UTF-8），
        /// 再退回 932（日文 Shift-JIS，与 Excel 那条路的兜底代码页一致）。
        /// </summary>
        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(TzJsonBuilder.FallbackCodePage).GetString(bytes);
            }
        }

        /// <summary>往正文追加一行；超过上限就截断并打标记（只截一次）。</summary>
        private static void AppendLine(StringBuilder builder, string line, int limit, ref bool truncated)
        {
            if (truncated || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (builder.Length + line.Length + Environment.NewLine.Length > limit)
            {
                int room = limit - builder.Length;

                if (room > 0)
                {
                    builder.Append(line.Substring(0, Math.Min(room, line.Length)));
                }

                truncated = true;
                return;
            }

            builder.AppendLine(line);
        }
    }
}
