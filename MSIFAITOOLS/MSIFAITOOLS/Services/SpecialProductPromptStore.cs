using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】最前面 · 特注品 CSV 生成用的「AI 补足」提示语：
    /// 系统提示词 + 两个用户提示词模板（先补缺行 / 缺字段，再补特注寸法），以及来源说明。
    /// 用户模板里的占位符（由 <see cref="CmdSpecialProductAiAdvisor"/> 替换）：
    /// <c>{{DRAWING_NO}}</c> / <c>{{EXCEL_TABLE}}</c> / <c>{{PDF_TEXT}}</c>。
    /// </summary>
    internal sealed class SpecialProductPromptSet
    {
        public SpecialProductPromptSet(string systemPrompt, string fieldTemplate,
            string specialSizeTemplate, string source)
        {
            SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? string.Empty : systemPrompt.Trim();
            FieldTemplate = string.IsNullOrEmpty(fieldTemplate) ? string.Empty : fieldTemplate.Trim();
            SpecialSizeTemplate = string.IsNullOrEmpty(specialSizeTemplate)
                ? string.Empty
                : specialSizeTemplate.Trim();
            Source = source ?? string.Empty;
        }

        /// <summary>系统提示词（角色与输出约束）。</summary>
        public string SystemPrompt { get; private set; }

        /// <summary>第一个任务（补缺行 / 缺字段）的用户提示词模板。</summary>
        public string FieldTemplate { get; private set; }

        /// <summary>第二个任务（补「追加対応特注寸法」）的用户提示词模板。</summary>
        public string SpecialSizeTemplate { get; private set; }

        /// <summary>提示语来源说明（文件路径等，用于写入运行日志）。</summary>
        public string Source { get; private set; }
    }

    /// <summary>
    /// 特注品 AI 补足提示语的加载器：优先读 exe 目录
    /// <c>Prompts\SpecialProductAdvisor.Prompt.txt</c>（运行时可改、无需重新编译），
    /// 不可用时回退到编译进 exe 的同名内嵌资源，最后回退到内置默认值。文件变化后（按修改时间）自动重新加载。
    /// <para>
    /// 文件格式与 <see cref="MemberNamePromptStore"/> 相同，只是用户提示词分两个分节：
    /// <c>[system]</c> / <c>[user_fields]</c>（补缺行 / 缺字段）/ <c>[user_special]</c>（补特注寸法）。
    /// 第一个分节之前的 <c>#</c> 注释行不送给 AI。
    /// </para>
    /// </summary>
    internal static class SpecialProductPromptStore
    {
        /// <summary>提示语文件所在目录（相对 exe 目录）。</summary>
        public const string PromptDirectoryName = "Prompts";

        /// <summary>提示语文件名。</summary>
        public const string PromptFileName = "SpecialProductAdvisor.Prompt.txt";

        /// <summary>内嵌资源名（编译进 exe 的同一份提示语文件）。</summary>
        public const string EmbeddedResourceName = "MSIFAITOOLS.Prompts.SpecialProductAdvisor.Prompt.txt";

        /// <summary>来源说明：外部文件。</summary>
        public const string SourceExternal = "外部文件";

        /// <summary>来源说明：内嵌资源。</summary>
        public const string SourceEmbedded = "内置资源";

        /// <summary>来源说明：内置默认值。</summary>
        public const string SourceDefault = "内置默认值";

        private const string SectionSystem = "system";

        private const string SectionFields = "user_fields";

        private const string SectionSpecial = "user_special";

        /// <summary>兜底用的系统提示词（外部文件与内置资源都不可用时）。</summary>
        private const string DefaultSystemPrompt =
            "你是建筑门窗（MSIF）特注品部品表的整理专家。用户会给你一份 Excel 部品表（可能缺行 / " +
            "缺字段）和这张图的 PDF 图面文字（plu_table.txt）。" +
            "请只根据图面文字补全缺失内容，不要改写已有内容，不要编造图面上没有的信息。" +
            "你只输出一个 JSON 对象，不输出解释文字，不输出 Markdown 代码块，不输出注释。";

        /// <summary>兜底用的「补缺行 / 缺字段」模板。</summary>
        private const string DefaultFieldTemplate =
            "図番：{{DRAWING_NO}}\n\n" +
            "任务：下面「Excel 部品表」里空着的字段，按「PDF 图面文字」补全；" +
            "图面上有、但部品表里没有的部品，作为新行补上。已有内容不许改。\n\n" +
            "### Excel 部品表（line = 行号）\n{{EXCEL_TABLE}}\n\n" +
            "### PDF 图面文字\n{{PDF_TEXT}}\n\n" +
            "输出格式（只输出 JSON）：\n" +
            "{\"rows\":[{\"line\":2,\"品種\":\"\",\"部品名\":\"\",\"枠区分\":\"\",\"部品コード\":\"\",\"員数\":\"\"}]," +
            "\"new_rows\":[{\"品種\":\"\",\"部品名\":\"\",\"枠区分\":\"\",\"部品コード\":\"\",\"員数\":\"\"," +
            "\"追加対応特注寸法\":\"\"}]}\n" +
            "没有把握的字段 / 行直接省略。";

        /// <summary>兜底用的「补特注寸法」模板。</summary>
        private const string DefaultSpecialSizeTemplate =
            "図番：{{DRAWING_NO}}\n\n" +
            "任务：给下面每个部品判读「追加対応特注寸法」。只写图上确实写着的特注尺寸，没有就省略该行。\n\n" +
            "### 部品表（line = 行号）\n{{EXCEL_TABLE}}\n\n" +
            "### PDF 图面文字\n{{PDF_TEXT}}\n\n" +
            "输出格式（只输出 JSON）：\n" +
            "{\"special_sizes\":[{\"line\":2,\"追加対応特注寸法\":\"2199 ≦Ｗ\"}]}\n" +
            "写法与图面原文一致（例 2199 ≦Ｗ / Ｗ ≦ 1800 / 1800 ≦Ｈ≦ 2400）；" +
            "图上明说不要追加时写「追加不要」。";

        private static readonly object SyncRoot = new object();

        private static SpecialProductPromptSet _cache;

        private static string _cacheKey;

        private static string _lastLoadError;

        /// <summary>最近一次读取提示语文件/资源失败的原因（正常时为空）。</summary>
        public static string LastLoadError
        {
            get { return _lastLoadError ?? string.Empty; }
        }

        /// <summary>提示语文件的全路径（exe 目录\Prompts\SpecialProductAdvisor.Prompt.txt）。</summary>
        public static string PromptFilePath
        {
            get { return Path.Combine(ResolveBaseDirectory(), PromptDirectoryName, PromptFileName); }
        }

        /// <summary>
        /// 取提示语所在的基础目录：优先 exe 目录；若那里没有 Prompts 文件夹，
        /// 退回到程序集所在目录（便于被其他宿主进程加载时仍能找到提示语）。
        /// </summary>
        private static string ResolveBaseDirectory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                if (!string.IsNullOrEmpty(baseDirectory) &&
                    Directory.Exists(Path.Combine(baseDirectory, PromptDirectoryName)))
                {
                    return baseDirectory;
                }

                string location = typeof(SpecialProductPromptStore).Assembly.Location;

                if (!string.IsNullOrEmpty(location))
                {
                    string assemblyDirectory = Path.GetDirectoryName(location);

                    if (!string.IsNullOrEmpty(assemblyDirectory) &&
                        Directory.Exists(Path.Combine(assemblyDirectory, PromptDirectoryName)))
                    {
                        return assemblyDirectory;
                    }
                }
            }
            catch (Exception)
            {
                // 目录探测失败时按默认 exe 目录处理
            }

            return string.IsNullOrEmpty(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;
        }

        /// <summary>加载提示语（外部文件优先，文件变化后自动重新加载）。</summary>
        public static SpecialProductPromptSet Load()
        {
            string path = PromptFilePath;

            lock (SyncRoot)
            {
                string key = BuildCacheKey(path);

                if (_cache != null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                {
                    return _cache;
                }

                _cache = LoadCore(path);
                _cacheKey = key;

                return _cache;
            }
        }

        /// <summary>清空缓存（下次 Load 必定重新读取文件）。</summary>
        public static void ClearCache()
        {
            lock (SyncRoot)
            {
                _cache = null;
                _cacheKey = null;
                _lastLoadError = null;
            }
        }

        /// <summary>缓存键：文件路径 + 修改时间 + 大小；文件不存在时使用固定标记。</summary>
        private static string BuildCacheKey(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);

                if (info.Exists)
                {
                    return info.FullName + "|" + info.LastWriteTimeUtc.Ticks.ToString() + "|" +
                           info.Length.ToString();
                }
            }
            catch (Exception ex)
            {
                SetLoadError("检查提示语文件失败（" + path + "）：" + ex.Message);
            }

            return "no-file";
        }

        /// <summary>读取顺序：外部文件 → 内置资源 → 内置默认值。</summary>
        private static SpecialProductPromptSet LoadCore(string path)
        {
            _lastLoadError = null;

            SpecialProductPromptSet external = BuildSet(TryReadFile(path),
                SourceExternal + "：" + path);

            if (external != null)
            {
                return external;
            }

            SpecialProductPromptSet embedded = BuildSet(TryReadEmbedded(),
                SourceEmbedded + "（" + EmbeddedResourceName + "）");

            if (embedded != null)
            {
                return embedded;
            }

            SetLoadError("读不到提示语文件与内置资源，已使用内置默认提示语：" + path);

            return new SpecialProductPromptSet(DefaultSystemPrompt, DefaultFieldTemplate,
                DefaultSpecialSizeTemplate, SourceDefault);
        }

        /// <summary>把提示语文本解析成提示语集合；缺少必需分节时返回 null。</summary>
        private static SpecialProductPromptSet BuildSet(string text, string source)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            IDictionary<string, string> sections = ParseSections(text);

            if (!sections.ContainsKey(SectionSystem) || !sections.ContainsKey(SectionFields) ||
                !sections.ContainsKey(SectionSpecial))
            {
                SetLoadError(string.Format("提示语缺少 [{0}] / [{1}] / [{2}] 分节：{3}",
                    SectionSystem, SectionFields, SectionSpecial, source));

                return null;
            }

            SpecialProductPromptSet set = new SpecialProductPromptSet(sections[SectionSystem],
                sections[SectionFields], sections[SectionSpecial], source);

            if (set.SystemPrompt.Length == 0 || set.FieldTemplate.Length == 0 ||
                set.SpecialSizeTemplate.Length == 0)
            {
                SetLoadError("提示语内容为空：" + source);

                return null;
            }

            return set;
        }

        /// <summary>
        /// 按 [节名] 切分文本。第一个分节之前的非注释行忽略；
        /// 分节内的内容原样保留（包括空行与 # 开头的行）。
        /// </summary>
        private static IDictionary<string, string> ParseSections(string text)
        {
            Dictionary<string, string> sections =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(text))
            {
                return sections;
            }

            StringBuilder current = null;
            string currentName = null;
            bool seenSection = false;

            using (StringReader reader = new StringReader(text))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();

                    if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                    {
                        FlushSection(sections, currentName, current);
                        currentName = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        current = new StringBuilder();
                        seenSection = true;

                        continue;
                    }

                    if (!seenSection || current == null)
                    {
                        continue;
                    }

                    current.AppendLine(line);
                }
            }

            FlushSection(sections, currentName, current);

            return sections;
        }

        /// <summary>把当前分节内容写入字典（同名分节多次出现时按顺序拼接）。</summary>
        private static void FlushSection(IDictionary<string, string> sections, string name,
            StringBuilder content)
        {
            if (string.IsNullOrWhiteSpace(name) || content == null)
            {
                return;
            }

            string text = content.ToString().Trim();

            if (text.Length == 0)
            {
                return;
            }

            if (sections.ContainsKey(name))
            {
                sections[name] = (sections[name] + "\n" + text).Trim();
            }
            else
            {
                sections[name] = text;
            }
        }

        /// <summary>读取外部提示语文件（UTF-8，自动识别 BOM）。</summary>
        private static string TryReadFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    // 外部文件不存在是正常情况（exe 目录里没有也能用内置资源），不记为错误
                    return null;
                }

                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SetLoadError("读取提示语文件失败（" + path + "）：" + ex.Message);

                return null;
            }
        }

        /// <summary>读取编译进 exe 的提示语资源。</summary>
        private static string TryReadEmbedded()
        {
            try
            {
                Assembly assembly = typeof(SpecialProductPromptStore).Assembly;

                using (Stream stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                SetLoadError("读取内置提示语资源失败：" + ex.Message);

                return null;
            }
        }

        /// <summary>记录失败原因（多次失败时保留最后一条）。</summary>
        private static void SetLoadError(string message)
        {
            _lastLoadError = message;
        }
    }
}




