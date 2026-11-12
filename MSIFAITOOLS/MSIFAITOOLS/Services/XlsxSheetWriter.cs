using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 把「表头 + 行数据」写成 <c>.xlsx</c>（OpenXML）第一张工作表。
    ///
    /// <para>
    /// 与 <see cref="XlsxSheetReader"/> 对称：不引入第三方 Excel 库（ClosedXML / EPPlus /
    /// DocumentFormat.OpenXml 都要额外装包），直接用 .NET 自带的 <see cref="ZipArchive"/>
    /// （xlsx = zip 包）+ 手写最小 OpenXML 部件。
    /// </para>
    ///
    /// <para>写出的内容：</para>
    /// <list type="bullet">
    ///   <item>默认全部单元格用内联字符串（<c>t="inlineStr"</c>），文本原样保存，Excel 不会把
    ///         <c>3-100</c>、<c>0012</c> 之类自动转成日期 / 数字；</item>
    ///   <item>调用方用 <c>numericColumnIndexes</c> 指定的列（数量 / 切断寸法 / H・W 上下限 等）
    ///         写成<b>数字类型</b>单元格（<c>&lt;v&gt;</c>，无 <c>t</c> 属性），Excel 里可直接排序 / 求和；
    ///         该列里转不成数字的值（如 <c>3-100</c>、<c>2枚</c>）仍按文本写，内容不丢；</item>
    ///   <item>第 1 行表头加粗 + 冻结（<c>pane ySplit="1"</c>），列宽按调用方给的宽度设置；</item>
    ///   <item>有数据行时给表头加自动筛选（<c>autoFilter</c>）。</item>
    /// </list>
    ///
    /// <para>
    /// 先写同目录的 <c>.tmp</c> 临时文件，成功后替换目标文件：写一半失败不会破坏已有文件；
    /// 目标文件被 Excel 打开时会抛 <see cref="IOException"/>（调用方给出提示）。
    /// </para>
    /// </summary>
    internal static class XlsxSheetWriter
    {
        /// <summary>Excel 单元格文本上限（超过会被 Excel 判为文件损坏）。</summary>
        private const int MaxCellLength = 32767;

        /// <summary>未指定列宽时使用的宽度（字符数）。</summary>
        private const double DefaultColumnWidth = 10;

        /// <summary>工作表名上限（Excel 限制 31 个字符）。</summary>
        private const int MaxSheetNameLength = 31;

        /// <summary>所有部件共用的 XML 声明。</summary>
        private const string XmlHeader =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n";

        /// <summary>包关系命名空间（Content Types / Relationships）。</summary>
        private const string PackageRelationshipsNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>SpreadsheetML 主命名空间。</summary>
        private const string SpreadsheetNamespace =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>OfficeDocument 关系命名空间。</summary>
        private const string OfficeDocumentRelationshipsNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        /// <summary><c>xl/styleSheet</c>：默认字体 + 表头加粗字体，两套单元格格式（0 = 普通，1 = 表头加粗）。</summary>
        private const string StylesXml =
            XmlHeader +
            "<styleSheet xmlns=\"" + SpreadsheetNamespace + "\">" +
            "<fonts count=\"2\">" +
            "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
            "</fonts>" +
            "<fills count=\"2\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "</fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        /// <summary><c>[Content_Types].xml</c>：声明包里各 xml 部件的类型。</summary>
        private const string ContentTypesXml =
            XmlHeader +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" " +
            "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" " +
            "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" " +
            "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        /// <summary><c>_rels/.rels</c>：包 → 工作簿的关系。</summary>
        private const string RootRelationshipsXml =
            XmlHeader +
            "<Relationships xmlns=\"" + PackageRelationshipsNamespace + "\">" +
            "<Relationship Id=\"rId1\" " +
            "Type=\"" + OfficeDocumentRelationshipsNamespace + "/officeDocument\" " +
            "Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        /// <summary><c>xl/_rels/workbook.xml.rels</c>：工作簿 → 工作表 / 样式的关系。</summary>
        private const string WorkbookRelationshipsXml =
            XmlHeader +
            "<Relationships xmlns=\"" + PackageRelationshipsNamespace + "\">" +
            "<Relationship Id=\"rId1\" " +
            "Type=\"" + OfficeDocumentRelationshipsNamespace + "/worksheet\" " +
            "Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" " +
            "Type=\"" + OfficeDocumentRelationshipsNamespace + "/styles\" " +
            "Target=\"styles.xml\"/>" +
            "</Relationships>";

        /// <summary>
        /// 写出 xlsx 文件（第一张工作表）。
        /// </summary>
        /// <param name="filePath">目标文件绝对路径（目录不存在时自动创建）。</param>
        /// <param name="sheetName">工作表名（非法字符会被替换，超长会截断；为空时用「Sheet1」）。</param>
        /// <param name="headers">表头文本（列顺序 = 数组顺序）。</param>
        /// <param name="rows">数据行（每行按列顺序；行内元素少于表头时缺少的列按空单元格处理）。</param>
        /// <param name="columnWidths">列宽（字符数，可与表头等长或更短；为 <c>null</c> / 0 时用默认宽度）。</param>
        /// <param name="numericColumnIndexes">
        /// 需要写成<b>数字类型</b>的列下标（0 起，可为 <c>null</c> = 全部按文本写）。
        /// 只影响数据行：该列的值能解析成数字时写 <c>&lt;v&gt;数字&lt;/v&gt;</c>，
        /// 解析不了（如 <c>3-100</c>、<c>2枚</c>）或为空时仍按文本 / 空单元格处理。
        /// </param>
        public static void WriteSheet(string filePath, string sheetName,
            IList<string> headers, IList<string[]> rows, IList<double> columnWidths,
            IList<int> numericColumnIndexes = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("导出文件路径不能为空。", nameof(filePath));
            }

            if (headers == null || headers.Count == 0)
            {
                throw new ArgumentException("表头不能为空。", nameof(headers));
            }

            string fullPath = Path.GetFullPath(filePath.Trim());
            string directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string sheetXml = BuildSheetXml(headers, rows, columnWidths, numericColumnIndexes);
            string temporaryPath = fullPath + ".tmp";

            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.Create,
                    FileAccess.Write, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
                    WriteEntry(archive, "_rels/.rels", RootRelationshipsXml);
                    WriteEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheetName));
                    WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
                    WriteEntry(archive, "xl/styles.xml", StylesXml);
                    WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
                }

                // 目标文件已存在时直接覆盖（被 Excel 占用会在 Delete / Move 处抛 IOException）
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        // 临时文件删不掉不影响导出结果，忽略
                    }
                }
            }
        }

        /// <summary>生成 <c>xl/workbook.xml</c>（只有一张工作表）。</summary>
        /// <param name="sheetName">工作表名（非法字符 / 超长会被修正）。</param>
        private static string BuildWorkbookXml(string sheetName)
        {
            StringBuilder xml = new StringBuilder(512);
            xml.Append(XmlHeader);
            xml.Append("<workbook xmlns=\"").Append(SpreadsheetNamespace).Append("\" ");
            xml.Append("xmlns:r=\"").Append(OfficeDocumentRelationshipsNamespace).Append("\">");
            xml.Append("<sheets><sheet name=\"").Append(EscapeXml(SanitizeSheetName(sheetName)))
                .Append("\" sheetId=\"1\" r:id=\"rId1\"/></sheets>");
            xml.Append("</workbook>");

            return xml.ToString();
        }

        /// <summary>生成 <c>xl/worksheets/sheet1.xml</c>（表头 + 数据行 + 列宽 + 冻结首行 + 自动筛选）。</summary>
        /// <param name="headers">表头文本。</param>
        /// <param name="rows">数据行。</param>
        /// <param name="columnWidths">列宽。</param>
        /// <param name="numericColumnIndexes">需要写成数字类型的列下标（0 起，可为 <c>null</c>）。</param>
        private static string BuildSheetXml(IList<string> headers, IList<string[]> rows,
            IList<double> columnWidths, IList<int> numericColumnIndexes)
        {
            int rowCount = rows == null ? 0 : rows.Count;
            string lastRow = (rowCount + 1).ToString(CultureInfo.InvariantCulture);
            string reference = "A1:" + GetColumnName(headers.Count - 1) + lastRow;

            StringBuilder xml = new StringBuilder(4096);
            xml.Append(XmlHeader);
            xml.Append("<worksheet xmlns=\"").Append(SpreadsheetNamespace).Append("\">");
            xml.Append("<dimension ref=\"").Append(reference).Append("\"/>");
            // 冻结首行（表头），向下滚动时表头始终可见
            xml.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\">");
            xml.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            xml.Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>");
            xml.Append("</sheetView></sheetViews>");
            xml.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            xml.Append("<cols>");

            for (int index = 0; index < headers.Count; index++)
            {
                double width = DefaultColumnWidth;

                if (columnWidths != null && index < columnWidths.Count && columnWidths[index] > 0)
                {
                    width = columnWidths[index];
                }

                xml.Append("<col min=\"").Append((index + 1).ToString(CultureInfo.InvariantCulture))
                    .Append("\" max=\"").Append((index + 1).ToString(CultureInfo.InvariantCulture))
                    .Append("\" width=\"").Append(width.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("\" customWidth=\"1\"/>");
            }

            xml.Append("</cols>");
            xml.Append("<sheetData>");
            AppendRow(xml, 1, headers, true, null);

            for (int index = 0; index < rowCount; index++)
            {
                AppendRow(xml, index + 2, rows[index], false, numericColumnIndexes);
            }

            xml.Append("</sheetData>");

            if (rowCount > 0)
            {
                xml.Append("<autoFilter ref=\"").Append(reference).Append("\"/>");
            }

            xml.Append("</worksheet>");

            return xml.ToString();
        }

        /// <summary>追加一行（空值单元格不写，减小文件体积）。</summary>
        /// <param name="xml">目标 XML 缓冲区。</param>
        /// <param name="rowNumber">行号（从 1 开始）。</param>
        /// <param name="values">该行各列文本。</param>
        /// <param name="header">true = 表头行（加粗 + 行高 20）。</param>
        /// <param name="numericColumnIndexes">
        /// 需要写成数字类型的列下标（0 起，可为 <c>null</c>）；表头行忽略该参数。
        /// </param>
        private static void AppendRow(StringBuilder xml, int rowNumber, IList<string> values,
            bool header, IList<int> numericColumnIndexes)
        {
            xml.Append("<row r=\"").Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append('"');

            if (header)
            {
                xml.Append(" ht=\"20\" customHeight=\"1\"");
            }

            xml.Append('>');

            int count = values == null ? 0 : values.Count;

            for (int index = 0; index < count; index++)
            {
                string text = CleanCellText(values[index]);

                if (text.Length == 0)
                {
                    continue;
                }

                string cellReference = GetColumnName(index) + rowNumber.ToString(CultureInfo.InvariantCulture);

                // 数字类型的列：能转成数字就写 <v>（Excel 里是数值，可排序 / 求和），
                // 转不了的（3-100 / 2枚 …）落到下面的文本分支，内容不丢
                if (!header && IsNumericColumn(numericColumnIndexes, index))
                {
                    string number;

                    if (TryFormatNumber(text, out number))
                    {
                        xml.Append("<c r=\"").Append(cellReference).Append("\"><v>")
                            .Append(number).Append("</v></c>");
                        continue;
                    }
                }

                xml.Append("<c r=\"").Append(cellReference).Append("\" t=\"inlineStr\"");

                if (header)
                {
                    xml.Append(" s=\"1\"");
                }

                xml.Append("><is><t xml:space=\"preserve\">").Append(EscapeXml(text))
                    .Append("</t></is></c>");
            }

            xml.Append("</row>");
        }

        /// <summary>该列（0 起）是否在「写成数字类型」的列清单里。</summary>
        /// <param name="numericColumnIndexes">数字列下标清单（可为 <c>null</c>）。</param>
        /// <param name="columnIndex">要判断的列下标（0 起）。</param>
        private static bool IsNumericColumn(IList<int> numericColumnIndexes, int columnIndex)
        {
            if (numericColumnIndexes == null)
            {
                return false;
            }

            for (int index = 0; index < numericColumnIndexes.Count; index++)
            {
                if (numericColumnIndexes[index] == columnIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把单元格文本转成 xlsx 数字字面量（<c>&lt;v&gt;</c> 里能直接用的写法）。
        /// </summary>
        /// <param name="text">单元格文本（已去掉控制字符）。</param>
        /// <param name="number">能转时输出数字字面量（如 <c>12</c>、<c>12.5</c>、<c>1200</c>）。</param>
        /// <returns>
        /// true = 是数字（可写成数字类型单元格）；false = 不是数字（保持文本），
        /// 例如 <c>3-100</c>、<c>2枚</c>、<c>MDU762</c>、空串。
        /// </returns>
        /// <remarks>
        /// 数字按<b>不变文化</b>（<see cref="CultureInfo.InvariantCulture"/>）解析与输出，与 Excel 的
        /// 存储格式一致：<c>1,200</c> 去掉千分位写成 <c>1200</c>、<c>12.50</c> 写成 <c>12.5</c>、
        /// <c>0012</c> 写成 <c>12</c>（Excel 本来就把它当数字，这里只保证字面量合法）。
        /// </remarks>
        private static bool TryFormatNumber(string text, out string number)
        {
            number = null;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (trimmed.Length == 0)
            {
                return false;
            }

            // 全角数字（２）、全角逗号（，）等也按数字处理：用兼容形式（NFKC）解析，
            // 但解析失败时仍按原文写文本，不改内容
            string candidate = NormalizeNumericCandidate(trimmed);

            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            decimal decimalValue;

            if (decimal.TryParse(candidate, styles, CultureInfo.InvariantCulture, out decimalValue))
            {
                number = decimalValue == decimal.Truncate(decimalValue)
                    ? decimalValue.ToString("0", CultureInfo.InvariantCulture)
                    : decimalValue.ToString("0.############################", CultureInfo.InvariantCulture);

                return true;
            }

            // decimal 放不下的（1E+30 之类）交给 double，写成 Excel 也认的指数形式
            double doubleValue;

            if (double.TryParse(candidate, styles, CultureInfo.InvariantCulture, out doubleValue)
                && !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue))
            {
                number = doubleValue.ToString("R", CultureInfo.InvariantCulture);

                return true;
            }

            return false;
        }

        /// <summary>
        /// 数字解析前的归一化：NFKC（全角数字 / 全角符号 → 半角）。非法 Unicode 时原样返回。
        /// </summary>
        /// <param name="text">单元格文本（已去首尾空白）。</param>
        private static string NormalizeNumericCandidate(string text)
        {
            try
            {
                return text.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                return text;
            }
        }

        /// <summary>把列号（0 起）转成 Excel 列名：0 → A，25 → Z，26 → AA。</summary>
        /// <param name="columnIndex">列下标（0 起）。</param>
        private static string GetColumnName(int columnIndex)
        {
            StringBuilder name = new StringBuilder(3);
            int value = columnIndex + 1;

            while (value > 0)
            {
                int remainder = (value - 1) % 26;
                name.Insert(0, (char)('A' + remainder));
                value = (value - 1) / 26;
            }

            return name.Length == 0 ? "A" : name.ToString();
        }

        /// <summary>清理单元格文本：去掉 XML 不允许的控制字符，并截断到 Excel 的长度上限。</summary>
        /// <param name="value">原始文本（可为 <c>null</c>）。</param>
        private static string CleanCellText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder text = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (ch == '\r')
                {
                    continue;
                }

                // XML 1.0 不允许的控制字符（保留 \t 和 \n）
                if (ch < ' ' && ch != '\t' && ch != '\n')
                {
                    continue;
                }

                text.Append(ch);

                if (text.Length >= MaxCellLength)
                {
                    break;
                }
            }

            return text.ToString();
        }

        /// <summary>把工作表名不允许的字符替换成下划线（Excel 限制：31 字符，不能含 \ / ? * [ ] :）。</summary>
        /// <param name="sheetName">原始工作表名（可为 <c>null</c>）。</param>
        private static string SanitizeSheetName(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                return "Sheet1";
            }

            StringBuilder builder = new StringBuilder(MaxSheetNameLength);

            foreach (char ch in sheetName.Trim())
            {
                builder.Append("\\/?*[]:".IndexOf(ch) >= 0 ? '_' : ch);

                if (builder.Length >= MaxSheetNameLength)
                {
                    break;
                }
            }

            string result = builder.ToString().Trim('\'');

            return result.Length == 0 ? "Sheet1" : result;
        }

        /// <summary>XML 转义（<c>&amp; &lt; &gt; " '</c>）。</summary>
        /// <param name="value">待转义文本。</param>
        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder text = new StringBuilder(value.Length + 16);

            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '&':
                        text.Append("&amp;");
                        break;
                    case '<':
                        text.Append("&lt;");
                        break;
                    case '>':
                        text.Append("&gt;");
                        break;
                    case '"':
                        text.Append("&quot;");
                        break;
                    case '\'':
                        text.Append("&apos;");
                        break;
                    default:
                        text.Append(ch);
                        break;
                }
            }

            return text.ToString();
        }

        /// <summary>往 zip 包里写一个 xml 部件（UTF-8 无 BOM）。</summary>
        /// <param name="archive">目标 zip 包。</param>
        /// <param name="entryName">部件路径（如 <c>xl/workbook.xml</c>）。</param>
        /// <param name="content">部件内容。</param>
        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }
    }
}
