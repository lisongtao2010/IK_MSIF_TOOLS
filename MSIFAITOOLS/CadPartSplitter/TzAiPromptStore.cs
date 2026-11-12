using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace CadPartSplitter
{
    /// <summary>
    /// 一次加载得到的「Excel 部品表 AI 判定」提示语：系统提示词 + 用户提示词模板，
    /// 以及来源说明（外部文件 / 内置资源 / 内置默认值）。
    /// </summary>
    internal sealed class TzAiPromptSet
    {
        public TzAiPromptSet(string systemPrompt, string userTemplate, string source, bool isExternal)
        {
            SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? string.Empty : systemPrompt.Trim();
            UserTemplate = string.IsNullOrEmpty(userTemplate) ? string.Empty : userTemplate.Trim();
            Source = source ?? string.Empty;
            IsExternal = isExternal;
        }

        /// <summary>系统提示词（角色与输出约束）。</summary>
        public string SystemPrompt { get; private set; }

        /// <summary>用户提示词模板（含 {{DRAWING_NO}} / {{SHEET}} / {{GRID_TEXT}} 占位符）。</summary>
        public string UserTemplate { get; private set; }

        /// <summary>提示语来源说明（文件路径等，用于写入运行日志）。</summary>
        public string Source { get; private set; }

        /// <summary>是否来自外部文件（可在程序运行时直接修改）。</summary>
        public bool IsExternal { get; private set; }

        /// <summary>提示语是否完整（系统提示词与用户模板都不为空）。</summary>
        public bool IsValid
        {
            get { return SystemPrompt.Length > 0 && UserTemplate.Length > 0; }
        }

        /// <summary>按占位符生成实际发送给 AI 的用户提示词。</summary>
        /// <param name="drawingNo">图番（PDF 文件名去扩展名；批量时是工作簿名）。</param>
        /// <param name="sheetName">工作表名（正常是 カメラ）。</param>
        /// <param name="gridText">带行号的工作表内容。</param>
        public string BuildUserPrompt(string drawingNo, string sheetName, string gridText)
        {
            string text = UserTemplate;

            text = text.Replace("{{DRAWING_NO}}", drawingNo ?? string.Empty);
            text = text.Replace("{{SHEET}}", sheetName ?? string.Empty);
            text = text.Replace("{{GRID_TEXT}}", gridText ?? string.Empty);

            return text;
        }
    }

    /// <summary>
    /// 「Excel 部品表 AI 判定」提示语的加载器（与 <c>MSIFAITOOLS</c> 的 <c>AiPromptStore</c> 同思路）：
    /// 优先读取 exe 目录下的 <c>Prompts\TzJsonAi.Prompt.txt</c>（可运行时修改、无需重新编译），
    /// 不可用时回退到编译进 exe 的同名内嵌资源，最后回退到内置默认值。
    /// 文件内容变化后（按修改时间 + 大小判断）自动重新加载。
    ///
    /// <para>
    /// 文件格式：用 <c>[system]</c> / <c>[user]</c> 两个分节，<c>#</c> 开头的行是注释（分节内保留）。
    /// </para>
    /// </summary>
    internal static class TzAiPromptStore
    {
        /// <summary>提示语文件所在目录（相对 exe 目录）。</summary>
        public const string PromptDirectoryName = "Prompts";

        /// <summary>提示语文件名。</summary>
        public const string PromptFileName = "TzJsonAi.Prompt.txt";

        /// <summary>内嵌资源名（编译进 exe 的同一份提示语文件）。</summary>
        public const string EmbeddedResourceName = "CadPartSplitter.Prompts.TzJsonAi.Prompt.txt";

        /// <summary>来源说明：外部文件。</summary>
        public const string SourceExternal = "外部文件";

        /// <summary>来源说明：内嵌资源。</summary>
        public const string SourceEmbedded = "内置资源";

        /// <summary>来源说明：内置默认值。</summary>
        public const string SourceDefault = "内置默认值";

        private const string SectionSystem = "system";

        private const string SectionUser = "user";

        private static readonly object SyncRoot = new object();

        private static TzAiPromptSet _cache;

        private static string _cacheKey;

        /// <summary>最近一次读取提示语文件/资源失败的原因（正常时为空）。</summary>
        public static string LastLoadError { get; private set; }

        /// <summary>提示语是否来自外部文件（日志里提示「想改提示语改这个文件」用）。</summary>
        public static bool IsExternal
        {
            get
            {
                TzAiPromptSet set = Load();

                return set != null && set.IsExternal;
            }
        }

        /// <summary>兜底用的系统提示词（仅在外部文件与内置资源都不可用时使用）。</summary>
        private const string DefaultSystemPrompt =
            "你是日本建筑门窗（MSIF）Excel 部品表（部品明细表）的解析专家。" +
            "你只输出一个 JSON 对象：不输出解释文字，不输出 Markdown 代码块，不输出注释。";

        /// <summary>兜底用的用户提示词模板（仅在外部文件与内置资源都不可用时使用）。</summary>
        private const string DefaultUserTemplate =
            "任务：把工作表「{{SHEET}}」（图番 {{DRAWING_NO}}）里的部品明细抽出来。\n" +
            "工作表内容的写法：每行以 #行号 开头，后面是 C列号=值（列号从 1 开始），空格子省略不写。\n\n" +
            "要抽的五个字段（列名在不同 Excel 里可能被改写，或表头被拆成两三行）：\n" +
            "- 品種：部品种类（如 片引戸 / 引戸 / 三方枠）。合并单元格导致本行为空时，沿用上方最近一行的值。\n" +
            "- 部品名：部品名称。\n" +
            "- 枠区分：枠的种类（如 三方枠 / 片三方枠）。\n" +
            "- 部品コード：部品编号（字母数字，如 MZA674）。也有可能是" +
            "「追加部品セット ： 片引戸用部品セット（ MZA674 ）」这种长格子里的括号内编号。\n" +
            "- 追加対応特注寸法：特注尺寸（如 2199 ≦Ｗ）。原样抄写，不要换算、不要把 ≦ 改写成 <=。\n\n" +
            "版式可能是：\n" +
            "① 普通 5 列表：一行一条明细，表头在上方（表头可能占 2~3 行；一张表里可能叠着多段）。\n" +
            "② 分块版式：先一个标题行（如「① 三方枠」），接着「追加部品セット ： …（ MZA674 ）」" +
            "给出部品名 / 部品コード，再一行「品種」+ 多个「追加対応特注寸法」表头，" +
            "后面几行是 品種 × 特注寸法 的组合；同一行可能并排 2 个以上的块。\n" +
            "③ 多张表上下排列：每张表都有自己的表头。\n\n" +
            "规则：\n" +
            "1. 每个字段的值必须原文出现在工作表内容里（程序会回原表逐字核对），" +
            "不要翻译、不要补全、不要计算、不要改字。\n" +
            "2. row 必须填：这条明细所在的行号（# 后面的数字）。\n" +
            "3. 只收真正的部品明细行；表头行、合计行、表題欄、変更履歴、注记行不要收。\n" +
            "4. 判断不出某个字段就留空字符串，不要编造。\n" +
            "5. 不要输出重复明细。\n" +
            "6. 只在 notes 里用一句话说明你的版式判断（可省略）。\n\n" +
            "只输出：\n" +
            "{\"items\":[{\"row\":12,\"品種\":\"片引戸\",\"部品名\":\"…\",\"枠区分\":\"三方枠\"," +
            "\"部品コード\":\"MZA674\",\"追加対応特注寸法\":\"2199 ≦Ｗ\"}],\"notes\":\"…\"}\n\n" +
            "【工作表内容】\n" +
            "{{GRID_TEXT}}";

        /// <summary>提示语文件的全路径（exe 目录\Prompts\TzJsonAi.Prompt.txt）。</summary>
        public static string PromptFilePath
        {
            get
            {
                return Path.Combine(ResolveBaseDirectory(), PromptDirectoryName, PromptFileName);
            }
        }

        /// <summary>
        /// 取提示语所在的基础目录：优先 exe 目录；若 exe 目录下没有 Prompts 文件夹，
        /// 则退回到程序集所在目录。
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

                string location = typeof(TzAiPromptStore).Assembly.Location;

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
        public static TzAiPromptSet Load()
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
                LastLoadError = null;
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
        private static TzAiPromptSet LoadCore(string path)
        {
            LastLoadError = null;

            TzAiPromptSet external = BuildSet(TryReadFile(path),
                SourceExternal + "：" + path, true);

            if (external != null)
            {
                return external;
            }

            TzAiPromptSet embedded = BuildSet(TryReadEmbedded(),
                SourceEmbedded + "（" + EmbeddedResourceName + "）", false);

            if (embedded != null)
            {
                return embedded;
            }

            return new TzAiPromptSet(DefaultSystemPrompt, DefaultUserTemplate, SourceDefault, false);
        }

        /// <summary>把提示语文本解析成提示语集合；缺少 [system] / [user] 时返回 null。</summary>
        private static TzAiPromptSet BuildSet(string text, string source, bool isExternal)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            IDictionary<string, string> sections = ParseSections(text);

            if (!sections.ContainsKey(SectionSystem) || !sections.ContainsKey(SectionUser))
            {
                SetLoadError("提示语缺少 [" + SectionSystem + "] 或 [" + SectionUser + "] 分节：" + source);

                return null;
            }

            TzAiPromptSet set = new TzAiPromptSet(sections[SectionSystem], sections[SectionUser],
                source, isExternal);

            if (!set.IsValid)
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

                    if (trimmed.Length >= 3 && trimmed[0] == '[' &&
                        trimmed[trimmed.Length - 1] == ']')
                    {
                        if (current != null && currentName != null)
                        {
                            sections[currentName] = current.ToString().Trim();
                        }

                        currentName = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        current = new StringBuilder();
                        seenSection = true;

                        continue;
                    }

                    if (!seenSection)
                    {
                        continue;
                    }

                    if (current != null)
                    {
                        current.AppendLine(line);
                    }
                }
            }

            if (current != null && currentName != null)
            {
                sections[currentName] = current.ToString().Trim();
            }

            return sections;
        }

        /// <summary>读取外部提示语文件（不存在或读取失败时返回 null）。</summary>
        private static string TryReadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
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

        /// <summary>读取编译进 exe 的内嵌提示语资源（读取失败时返回 null）。</summary>
        private static string TryReadEmbedded()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();

                using (Stream stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        SetLoadError("exe 里没有内嵌资源 " + EmbeddedResourceName);
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
                SetLoadError("读取内嵌提示语资源失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>记下最近一次读取失败的原因。</summary>
        private static void SetLoadError(string message)
        {
            LastLoadError = message;
        }
    }
}
