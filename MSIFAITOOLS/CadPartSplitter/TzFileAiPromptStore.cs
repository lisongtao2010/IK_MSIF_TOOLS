using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace CadPartSplitter
{
    /// <summary>
    /// 一次加载得到的「非 Excel 特注品文件 AI 判定」提示语：系统提示词 + 用户提示词模板，
    /// 以及来源说明（外部文件 / 内置资源 / 内置默认值）。
    /// </summary>
    internal sealed class TzFileAiPromptSet
    {
        public TzFileAiPromptSet(string systemPrompt, string userTemplate, string source, bool isExternal)
        {
            SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? string.Empty : systemPrompt.Trim();
            UserTemplate = string.IsNullOrEmpty(userTemplate) ? string.Empty : userTemplate.Trim();
            Source = source ?? string.Empty;
            IsExternal = isExternal;
        }

        /// <summary>系统提示词（角色与输出约束）。</summary>
        public string SystemPrompt { get; private set; }

        /// <summary>用户提示词模板（含 {{DRAWING_NO}} / {{FILE_NAME}} / {{FILE_KIND}} / {{TRUNCATED_NOTE}} / {{FILE_TEXT}} 占位符）。</summary>
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
        /// <param name="drawingNo">图番（文件名去扩展名）。</param>
        /// <param name="fileName">文件名（带扩展名）。</param>
        /// <param name="fileKind">内容来源说明（PDF 文字层 / 文本文件）。</param>
        /// <param name="truncatedNote">超长截断说明（没截断时传空串）。</param>
        /// <param name="fileText">文件正文。</param>
        public string BuildUserPrompt(string drawingNo, string fileName, string fileKind,
            string truncatedNote, string fileText)
        {
            string text = UserTemplate;

            text = text.Replace("{{DRAWING_NO}}", drawingNo ?? string.Empty);
            text = text.Replace("{{FILE_NAME}}", fileName ?? string.Empty);
            text = text.Replace("{{FILE_KIND}}", fileKind ?? string.Empty);
            text = text.Replace("{{TRUNCATED_NOTE}}", truncatedNote ?? string.Empty);
            text = text.Replace("{{FILE_TEXT}}", fileText ?? string.Empty);

            return text;
        }
    }

    /// <summary>
    /// 「非 Excel 特注品文件 AI 判定」提示语的加载器（与 <see cref="TzAiPromptStore"/> 同一套思路）：
    /// 优先读取 exe 目录下的 <c>Prompts\TzJsonFileAi.Prompt.txt</c>（可运行时修改、无需重新编译），
    /// 不可用时回退到编译进 exe 的同名内嵌资源，最后回退到内置默认值。
    /// 文件内容变化后（按修改时间 + 大小判断）自动重新加载。
    /// </summary>
    internal static class TzFileAiPromptStore
    {
        /// <summary>提示语文件所在目录（相对 exe 目录）。</summary>
        public const string PromptDirectoryName = "Prompts";

        /// <summary>提示语文件名。</summary>
        public const string PromptFileName = "TzJsonFileAi.Prompt.txt";

        /// <summary>内嵌资源名（编译进 exe 的同一份提示语文件）。</summary>
        public const string EmbeddedResourceName = "CadPartSplitter.Prompts.TzJsonFileAi.Prompt.txt";

        /// <summary>来源说明：外部文件。</summary>
        public const string SourceExternal = "外部文件";

        /// <summary>来源说明：内嵌资源。</summary>
        public const string SourceEmbedded = "内置资源";

        /// <summary>来源说明：内置默认值。</summary>
        public const string SourceDefault = "内置默认值";

        private const string SectionSystem = "system";

        private const string SectionUser = "user";

        /// <summary>提示语文件缺失或写坏时的兜底：系统提示词。</summary>
        private const string DefaultSystemPrompt =
            "你是日本建筑门窗（MSIF）特注品部品表的解析专家。你只输出一个 JSON 对象："
            + "不输出解释文字，不输出 Markdown 代码块，不输出注释。";

        /// <summary>提示语文件缺失或写坏时的兜底：用户提示词模板（外部文件带完整说明，这里只保可用）。</summary>
        private const string DefaultUserTemplate =
            "任务：从文件「{{FILE_NAME}}」（图番 {{DRAWING_NO}}，内容来源 {{FILE_KIND}}）里，把部品明细抽出来。\n"
            + "要抽的五个字段：品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法。\n"
            + "每个字段的值必须与正文原文一致（程序会逐字核对），不要翻译、不要补全、不要改写；判断不出就留空。\n"
            + "只收真正的部品明细；表头、合计、表題欄、変更履歴、注记不要收。不要输出重复明细。\n"
            + "只输出：{\"items\":[{\"品種\":\"…\",\"部品名\":\"…\",\"枠区分\":\"…\",\"部品コード\":\"…\",\"追加対応特注寸法\":\"…\"}],\"notes\":\"…\"}\n"
            + "{{TRUNCATED_NOTE}}\n【文件正文】\n{{FILE_TEXT}}";

        private static readonly object SyncRoot = new object();

        private static TzFileAiPromptSet _cache;

        private static string _cacheKey;

        /// <summary>最近一次加载失败的原因（供日志说明）。</summary>
        public static string LastLoadError { get; private set; }

        /// <summary>提示语文件的全路径（exe 目录下的 <c>Prompts\TzJsonFileAi.Prompt.txt</c>）。</summary>
        public static string PromptFilePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    Path.Combine(PromptDirectoryName, PromptFileName));
            }
        }

        /// <summary>
        /// 加载提示语（带缓存：外部文件没变就不重读）。任何异常都退回内置默认值，不抛。
        /// </summary>
        public static TzFileAiPromptSet Load()
        {
            string path = PromptFilePath;
            string key;

            try
            {
                FileInfo info = new FileInfo(path);
                key = info.Exists
                    ? path + "|" + info.LastWriteTimeUtc.Ticks.ToString() + "|" + info.Length.ToString()
                    : path + "|missing";
            }
            catch (Exception)
            {
                key = path + "|error";
            }

            lock (SyncRoot)
            {
                if (_cache != null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                {
                    return _cache;
                }

                LastLoadError = string.Empty;

                TzFileAiPromptSet loaded = LoadCore(path);

                _cache = loaded;
                _cacheKey = key;

                return loaded;
            }
        }

        /// <summary>按「外部文件 → 内嵌资源 → 内置默认值」逐级取一份可用的提示语。</summary>
        private static TzFileAiPromptSet LoadCore(string path)
        {
            bool external = true;
            string text = TryReadFile(path);

            if (string.IsNullOrWhiteSpace(text))
            {
                external = false;
                text = TryReadEmbedded();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new TzFileAiPromptSet(DefaultSystemPrompt, DefaultUserTemplate,
                    SourceDefault, false);
            }

            Dictionary<string, string> sections = ParseSections(text);

            string systemPrompt;
            string userTemplate;

            bool hasSystem = sections.TryGetValue(SectionSystem, out systemPrompt);
            bool hasUser = sections.TryGetValue(SectionUser, out userTemplate);

            if (!hasSystem || !hasUser || string.IsNullOrWhiteSpace(systemPrompt) ||
                string.IsNullOrWhiteSpace(userTemplate))
            {
                SetLoadError("[system] / [user] 分节缺失或为空（" + (external ? path : "内嵌资源") + "）");

                return new TzFileAiPromptSet(DefaultSystemPrompt, DefaultUserTemplate,
                    SourceDefault, false);
            }

            return new TzFileAiPromptSet(systemPrompt, userTemplate,
                external ? SourceExternal + "（" + path + "）" : SourceEmbedded, external);
        }

        /// <summary>
        /// 解析 <c>[system]</c> / <c>[user]</c> 分节（<c>#</c> 开头的注释行在第一个分节之前丢弃，
        /// 分节内的 <c>#</c> 行保留 —— 与 <see cref="TzAiPromptStore"/> 同一套写法）。
        /// </summary>
        private static Dictionary<string, string> ParseSections(string text)
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
