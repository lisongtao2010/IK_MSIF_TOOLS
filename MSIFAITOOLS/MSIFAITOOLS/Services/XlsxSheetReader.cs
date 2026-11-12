using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 读 <c>.xlsx</c>（OpenXML）第一张工作表的内容，返回「行 → 单元格文本数组」。
    ///
    /// <para>
    /// 本工具只需要读 Excel 里的文本（部品コード / 使い分け 等），不写 Excel、不需要样式，
    /// 所以不引入第三方 Excel 库（ClosedXML / EPPlus / DocumentFormat.OpenXml 都要额外装包），
    /// 直接用 .NET 自带的 <see cref="ZipArchive"/>（xlsx = zip 包）+
    /// <see cref="XmlDocument"/> 解 <c>xl/sharedStrings.xml</c> 与 <c>xl/worksheets/sheet1.xml</c>。
    /// </para>
    ///
    /// <para>支持的内容：</para>
    /// <list type="bullet">
    ///   <item>共享字符串（<c>t="s"</c>，绝大多数文本单元格）；</item>
    ///   <item>内联字符串（<c>t="inlineStr"</c>，部分工具写出的文件）；</item>
    ///   <item>数字 / 公式结果（<c>t="n"</c> / <c>t="str"</c>，直接取 <c>&lt;v&gt;</c> 文本）；</item>
    ///   <item>空的 <c>&lt;c&gt;</c>（值按空字符串处理）。</item>
    /// </list>
    /// 不处理日期格式 / 合并单元格 / 多工作表（只读第一张 sheet，与「拾い出し表」的用法一致）。
    /// </summary>
    internal static class XlsxSheetReader
    {
        /// <summary>共享字符串表的 zip 内路径。</summary>
        private const string SharedStringsEntryName = "xl/sharedStrings.xml";

        /// <summary>工作表 zip 内路径的目录前缀。</summary>
        private const string WorksheetEntryPrefix = "xl/worksheets/";

        /// <summary>工作簿定义（表名 → rId）的 zip 内路径。</summary>
        private const string WorkbookEntryName = "xl/workbook.xml";

        /// <summary>工作簿关系表（rId → 工作表路径）的 zip 内路径。</summary>
        private const string WorkbookRelationshipsEntryName = "xl/_rels/workbook.xml.rels";

        /// <summary>打包内路径的目录前缀（<c>worksheets/sheet1.xml</c> → <c>xl/worksheets/sheet1.xml</c>）。</summary>
        private const string PackageDirectoryPrefix = "xl/";

        /// <summary>
        /// 读第一张工作表。文件不存在 / 打不开 / 不是 xlsx 时返回空表，
        /// 失败原因写入 <paramref name="notes"/>（供日志提示，不抛异常）。
        /// </summary>
        /// <param name="filePath">xlsx 文件绝对路径。</param>
        /// <param name="notes">失败说明（可为 <c>null</c>）。</param>
        /// <returns>每一行为一个字符串数组（下标 = 列号 - 1，缺列按空字符串补齐）。</returns>
        public static List<string[]> ReadFirstSheet(string filePath, IList<string> notes)
        {
            return ReadSheetCore(filePath, null, notes);
        }

        /// <summary>
        /// 读<b>指定名字</b>的工作表（例：Excel 部品表的「カメラ」表）。找不到这张表 / 文件打不开时
        /// 返回空表并把原因写进 <paramref name="notes"/>（不抛异常）；
        /// <paramref name="sheetName"/> 为空时与 <see cref="ReadFirstSheet"/> 完全相同。
        /// </summary>
        /// <param name="filePath">xlsx 文件绝对路径。</param>
        /// <param name="sheetName">工作表名（Excel 里显示的标签名，例 <c>カメラ</c>）。</param>
        /// <param name="notes">失败说明（可为 <c>null</c>）。</param>
        public static List<string[]> ReadSheet(string filePath, string sheetName, IList<string> notes)
        {
            return ReadSheetCore(filePath, sheetName, notes);
        }

        /// <summary><see cref="ReadFirstSheet"/> / <see cref="ReadSheet"/> 的共同实现。</summary>
        private static List<string[]> ReadSheetCore(string filePath, string sheetName, IList<string> notes)
        {
            List<string[]> rows = new List<string[]>();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                AddNote(notes, "找不到文件：" + filePath);
                return rows;
            }

            try
            {
                // FileShare.ReadWrite：Excel 正开着这个文件时也能读
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8))
                {
                    List<string> sharedStrings = ReadSharedStrings(archive);
                    bool byName = !string.IsNullOrWhiteSpace(sheetName);
                    ZipArchiveEntry sheetEntry = byName
                        ? FindWorksheetEntry(archive, sheetName.Trim())
                        : FindFirstWorksheetEntry(archive);

                    if (sheetEntry == null)
                    {
                        AddNote(notes, Path.GetFileName(filePath) + (byName
                            ? "：找不到工作表「" + sheetName.Trim() + "」"
                            : "：找不到工作表（xl/worksheets/sheet1.xml）"));

                        return rows;
                    }

                    using (Stream sheetStream = sheetEntry.Open())
                    {
                        XmlDocument document = new XmlDocument();
                        document.Load(sheetStream);

                        XmlNode sheetData = FindDescendant(document.DocumentElement, "sheetData");

                        if (sheetData == null)
                        {
                            return rows;
                        }

                        foreach (XmlNode rowNode in sheetData.ChildNodes)
                        {
                            if (rowNode == null || rowNode.NodeType != XmlNodeType.Element ||
                                !IsName(rowNode, "row"))
                            {
                                continue;
                            }

                            rows.Add(ReadRow(rowNode, sharedStrings));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddNote(notes, Path.GetFileName(filePath) + "：读取失败（" + ex.Message + "）");
            }

            return rows;
        }

        /// <summary>读 <c>xl/sharedStrings.xml</c>（没有该文件时返回空表）。</summary>
        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            List<string> values = new List<string>();
            ZipArchiveEntry entry = archive.GetEntry(SharedStringsEntryName);

            if (entry == null)
            {
                return values;
            }

            using (Stream stream = entry.Open())
            {
                XmlDocument document = new XmlDocument();
                document.Load(stream);

                XmlNode root = document.DocumentElement;

                if (root == null)
                {
                    return values;
                }

                foreach (XmlNode item in root.ChildNodes)
                {
                    if (item != null && item.NodeType == XmlNodeType.Element && IsName(item, "si"))
                    {
                        // <si> 里可能有 <r><t> 分段（富文本），InnerText 会把各段拼起来
                        values.Add(item.InnerText ?? string.Empty);
                    }
                }
            }

            return values;
        }

        /// <summary>取 zip 里第一张 worksheet（优先 sheet1.xml，其次按名字排序的第一个）。</summary>
        private static ZipArchiveEntry FindFirstWorksheetEntry(ZipArchive archive)
        {
            ZipArchiveEntry fallback = null;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName) ||
                    !entry.FullName.StartsWith(WorksheetEntryPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(entry.FullName, WorksheetEntryPrefix + "sheet1.xml",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }

                if (fallback == null || string.CompareOrdinal(entry.FullName, fallback.FullName) < 0)
                {
                    fallback = entry;
                }
            }

            return fallback;
        }

        /// <summary>
        /// 按工作表名找 zip 里的 worksheet：读 <c>xl/workbook.xml</c> 的
        /// <c>&lt;sheet name="…" r:id="rIdN"/&gt;</c>，再用 <c>xl/_rels/workbook.xml.rels</c>
        /// 把 rId 换成实际路径。找不到（或工作簿定义损坏）返回 <c>null</c>。
        /// </summary>
        private static ZipArchiveEntry FindWorksheetEntry(ZipArchive archive, string sheetName)
        {
            if (archive == null || string.IsNullOrWhiteSpace(sheetName))
            {
                return null;
            }

            try
            {
                string relationshipId = FindSheetRelationshipId(archive, sheetName);

                if (string.IsNullOrEmpty(relationshipId))
                {
                    return null;
                }

                string target = FindRelationshipTarget(archive, relationshipId);

                if (string.IsNullOrEmpty(target))
                {
                    return null;
                }

                string entryName = NormalizeEntryName(target);
                ZipArchiveEntry entry = archive.GetEntry(entryName);

                if (entry != null)
                {
                    return entry;
                }

                // 路径写法 / 大小写不一致时再按不区分大小写找一遍
                foreach (ZipArchiveEntry candidate in archive.Entries)
                {
                    if (string.Equals(candidate.FullName, entryName, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // 工作簿定义读不出来：当作「找不到这张表」，由调用方决定是否退回第一张工作表
            }

            return null;
        }

        /// <summary>在 <c>xl/workbook.xml</c> 里找「表名 → r:id」；找不到返回空字符串。</summary>
        private static string FindSheetRelationshipId(ZipArchive archive, string sheetName)
        {
            ZipArchiveEntry entry = archive.GetEntry(WorkbookEntryName);

            if (entry == null)
            {
                return string.Empty;
            }

            using (Stream stream = entry.Open())
            {
                XmlDocument document = new XmlDocument();
                document.Load(stream);

                XmlNode sheets = FindDescendant(document.DocumentElement, "sheets");

                if (sheets == null)
                {
                    return string.Empty;
                }

                foreach (XmlNode node in sheets.ChildNodes)
                {
                    if (node == null || node.NodeType != XmlNodeType.Element || !IsName(node, "sheet"))
                    {
                        continue;
                    }

                    XmlAttribute nameAttribute = node.Attributes == null ? null : node.Attributes["name"];

                    if (nameAttribute == null ||
                        !string.Equals((nameAttribute.Value ?? string.Empty).Trim(), sheetName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return ReadRelationshipId(node.Attributes);
                }
            }

            return string.Empty;
        }

        /// <summary>取 <c>&lt;sheet&gt;</c> 的 <c>r:id</c>（LocalName = <c>id</c>，与 <c>sheetId</c> 区分）。</summary>
        private static string ReadRelationshipId(XmlAttributeCollection attributes)
        {
            if (attributes == null)
            {
                return string.Empty;
            }

            foreach (XmlAttribute attribute in attributes)
            {
                if (attribute != null && string.Equals(attribute.LocalName, "id", StringComparison.Ordinal))
                {
                    return attribute.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>在 <c>xl/_rels/workbook.xml.rels</c> 里把 rId 换成 <c>Target</c>；找不到返回空字符串。</summary>
        private static string FindRelationshipTarget(ZipArchive archive, string relationshipId)
        {
            ZipArchiveEntry entry = archive.GetEntry(WorkbookRelationshipsEntryName);

            if (entry == null)
            {
                return string.Empty;
            }

            using (Stream stream = entry.Open())
            {
                XmlDocument document = new XmlDocument();
                document.Load(stream);

                XmlNode root = document.DocumentElement;

                if (root == null)
                {
                    return string.Empty;
                }

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node == null || node.NodeType != XmlNodeType.Element || !IsName(node, "Relationship"))
                    {
                        continue;
                    }

                    XmlAttribute idAttribute = node.Attributes == null ? null : node.Attributes["Id"];

                    if (idAttribute == null ||
                        !string.Equals(idAttribute.Value, relationshipId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    XmlAttribute targetAttribute = node.Attributes["Target"];

                    return targetAttribute == null ? string.Empty : (targetAttribute.Value ?? string.Empty);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 把 rels 的 <c>Target</c> 变成 zip 内的完整路径：
        /// <c>/xl/worksheets/sheet2.xml</c> → <c>xl/worksheets/sheet2.xml</c>；
        /// <c>worksheets/sheet2.xml</c> → <c>xl/worksheets/sheet2.xml</c>。
        /// </summary>
        private static string NormalizeEntryName(string target)
        {
            string value = (target ?? string.Empty).Replace('\\', '/').Trim();

            while (value.StartsWith("/", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.StartsWith(PackageDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return PackageDirectoryPrefix + value;
        }

        /// <summary>把一行的单元格解成「列号 - 1」下标的文本数组。</summary>
        private static string[] ReadRow(XmlNode rowNode, IList<string> sharedStrings)
        {
            List<string> cells = new List<string>();
            int nextIndex = 0;

            foreach (XmlNode cellNode in rowNode.ChildNodes)
            {
                if (cellNode == null || cellNode.NodeType != XmlNodeType.Element || !IsName(cellNode, "c"))
                {
                    continue;
                }

                // <c r="L3"> 的 r 给出「列字母 + 行号」；没有 r 时按出现顺序排列
                int columnIndex = ResolveColumnIndex(cellNode.Attributes, nextIndex);
                nextIndex = columnIndex + 1;

                while (cells.Count <= columnIndex)
                {
                    cells.Add(string.Empty);
                }

                cells[columnIndex] = ReadCellText(cellNode, sharedStrings);
            }

            return cells.ToArray();
        }

        /// <summary>解析 <c>r="L3"</c> 里的列号（L → 11）；解析不了时用 <paramref name="fallback"/>。</summary>
        private static int ResolveColumnIndex(XmlAttributeCollection attributes, int fallback)
        {
            if (attributes == null)
            {
                return fallback;
            }

            XmlAttribute reference = attributes["r"];

            if (reference == null || string.IsNullOrEmpty(reference.Value))
            {
                return fallback;
            }

            int column = 0;
            int letters = 0;

            foreach (char ch in reference.Value)
            {
                if (ch >= 'A' && ch <= 'Z')
                {
                    column = column * 26 + (ch - 'A' + 1);
                    letters++;
                }
                else if (ch >= 'a' && ch <= 'z')
                {
                    column = column * 26 + (ch - 'a' + 1);
                    letters++;
                }
                else
                {
                    break;
                }
            }

            return letters == 0 ? fallback : column - 1;
        }

        /// <summary>读一个单元格的文本（共享字符串 / 内联字符串 / 数字）。</summary>
        private static string ReadCellText(XmlNode cellNode, IList<string> sharedStrings)
        {
            string type = string.Empty;
            XmlAttribute typeAttribute = cellNode.Attributes == null ? null : cellNode.Attributes["t"];

            if (typeAttribute != null)
            {
                type = typeAttribute.Value ?? string.Empty;
            }

            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                XmlNode inlineText = FindDescendant(cellNode, "t");
                return inlineText == null ? string.Empty : (inlineText.InnerText ?? string.Empty).Trim();
            }

            XmlNode valueNode = FindChild(cellNode, "v");
            string raw = valueNode == null ? string.Empty : (valueNode.InnerText ?? string.Empty);

            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase))
            {
                int index;

                if (int.TryParse(raw.Trim(), out index) && index >= 0 && index < sharedStrings.Count)
                {
                    return (sharedStrings[index] ?? string.Empty).Trim();
                }

                return string.Empty;
            }

            return raw.Trim();
        }

        /// <summary>按 localName 找直接子元素（兼容 transitional / strict 两种命名空间）。</summary>
        private static XmlNode FindChild(XmlNode parent, string localName)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child != null && child.NodeType == XmlNodeType.Element && IsName(child, localName))
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>按 localName 深度优先找第一个后代元素。</summary>
        private static XmlNode FindDescendant(XmlNode parent, string localName)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child == null || child.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (IsName(child, localName))
                {
                    return child;
                }

                XmlNode found = FindDescendant(child, localName);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool IsName(XmlNode node, string localName)
        {
            return string.Equals(node.LocalName ?? node.Name, localName, StringComparison.Ordinal);
        }

        private static void AddNote(IList<string> notes, string message)
        {
            if (notes != null && !string.IsNullOrWhiteSpace(message))
            {
                notes.Add(message);
            }
        }
    }
}
