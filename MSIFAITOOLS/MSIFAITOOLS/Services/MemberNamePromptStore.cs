using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【AI 分析】（部材名 AI 推荐）用的一套提示语：系统提示词 + 用户提示词模板，以及来源说明。
    /// 用户模板里的占位符（由 <see cref="MemberNameAiAdvisor"/> 替换）：
    /// <c>{{DRAWING_NO}}</c> / <c>{{PAGE}}</c> / <c>{{MEMBER_TABLE}}</c> /
    /// <c>{{CALLOUT_TABLE}}</c> / <c>{{TEXT_TABLE}}</c> / <c>{{DISTANCE_TABLE}}</c> / <c>{{TARGET_TABLE}}</c>。
    /// </summary>
    internal sealed class MemberNamePromptSet
    {
        public MemberNamePromptSet(string systemPrompt, string userTemplate, string source)
        {
            SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? string.Empty : systemPrompt.Trim();
            UserTemplate = string.IsNullOrEmpty(userTemplate) ? string.Empty : userTemplate.Trim();
            Source = source ?? string.Empty;
        }

        /// <summary>系统提示词（角色与输出约束）。</summary>
        public string SystemPrompt { get; private set; }

        /// <summary>用户提示词模板（含上面那些占位符）。</summary>
        public string UserTemplate { get; private set; }

        /// <summary>提示语来源说明（文件路径等，用于写入运行日志）。</summary>
        public string Source { get; private set; }
    }

    /// <summary>
    /// 部材名判定提示语的加载器：优先读 exe 目录 <c>Prompts\MemberNameAdvisor.Prompt.txt</c>
    /// （运行时可改、无需重新编译），不可用时回退到编译进 exe 的同名内嵌资源，
    /// 最后回退到内置默认值。文件变化后（按修改时间）自动重新加载。
    /// 文件格式与 <see cref="AiPromptStore"/> 相同：<c>[system]</c> / <c>[user]</c> 分节，
    /// 第一个分节之前的 <c>#</c> 注释行不送给 AI。
    /// </summary>
    internal static class MemberNamePromptStore
    {
        /// <summary>提示语文件所在目录（相对 exe 目录）。</summary>
        public const string PromptDirectoryName = "Prompts";

        /// <summary>提示语文件名。</summary>
        public const string PromptFileName = "MemberNameAdvisor.Prompt.txt";

        /// <summary>内嵌资源名（编译进 exe 的同一份提示语文件）。</summary>
        public const string EmbeddedResourceName = "MSIFAITOOLS.Prompts.MemberNameAdvisor.Prompt.txt";

        /// <summary>来源说明：外部文件。</summary>
        public const string SourceExternal = "外部文件";

        /// <summary>来源说明：内嵌资源。</summary>
        public const string SourceEmbedded = "内置资源";

        /// <summary>来源说明：内置默认值。</summary>
        public const string SourceDefault = "内置默认值";

        private const string SectionSystem = "system";

        private const string SectionUser = "user";

        /// <summary>兜底用的系统提示词（外部文件与内置资源都不可用时）。</summary>
        private const string DefaultSystemPrompt =
            "你是建筑门窗（MSIF）图纸的部材名判定专家。用户会给你一张图纸文字层的坐标数据：" +
            "圈号（番号）的坐标、零件图上各个文字的坐标、需要判定的番号与其候选文字，" +
            "以及一份「部材マスタ」（部材名 一览 + 各部材下属的部品名）。" +
            "请判断每个番号的部品装在哪个部材上 —— 图上的文字可能是部品名、尺寸或说明，不一定是部材名。" +
            "你只输出一个 JSON 对象，不输出解释文字，不输出 Markdown 代码块，不输出注释。";

        /// <summary>兜底用的用户提示词模板（外部文件与内置资源都不可用时）。</summary>
        private const string DefaultUserTemplate =
            "図番：{{DRAWING_NO}}（第 {{PAGE}} 页）\n\n" +
            "任务：给下面每个「需要判定」的番号，判断它的部材名。回答只能是：" +
            "①「部材マスタ线索」里该行部品名所属的部材名（有多个时从里面选 1 个）；②「その他」。\n\n" +
            "{{MEMBER_TABLE}}\n\n{{CALLOUT_TABLE}}\n\n{{TEXT_TABLE}}\n\n{{DISTANCE_TABLE}}\n\n" +
            "{{TARGET_TABLE}}\n\n" +
            "判定规则（按顺序看）：\n" +
            "- 先看「部材マスタ线索」的「该行部品名「…」在部材マスタ里属于：…」：\n" +
            "  · 只有 1 个部材 → 就回答这个部材名（图上写的是别的文字，也以部材マスタ为准）\n" +
            "  · 有多个部材 → 只能从这些部材里选 1 个：按「图上候选部材名」/ 零件图的文字 / 距离表判断\n" +
            "    （例：鬼目ﾅｯﾄ(ﾌﾞﾚｰｷ取付用) → 上ﾚｰﾙ 或 上枠：图上写「上レール」→ 上ﾚｰﾙ、\n" +
            "      图上写「鴨居」（三方枠的上框）→ 上枠；「区分」= 三方枠 时优先 上枠 / 右縦枠 / 左縦枠 系）\n" +
            "  · 一个都没有（部品セット / 取説 / ラベル 等）→ 回答「その他」\n" +
            "- 部品名不在部材マスタ里时，才看该番号的「图上候选部材名」：能确定部材名就用它，不要改写\n" +
            "- 图上的文字是部材マスタ的部品名（ｼﾞｮｲﾝﾄ金具 / 鬼目ﾅｯﾄ / 部品ｾｯﾄ / 取説）时不要照抄，按它所属的部材回答\n" +
            "- 「现在的部材名」含 | 时只保留正确的一个；为空或「その他」时也要判断\n" +
            "- 「距离表」是程序算好的精确距离，直接用；距离最小且比次小近 10% 以上时直接选它\n" +
            "- 多个番号共用候选时按距离远近一一分配，同一处文字不要分给两个番号\n" +
            "- 判断不了的番号直接省略\n\n" +
            "输出格式：只输出一个 JSON 对象，key = 番号（字符串），value = 选定的部材名（或「その他」）。\n" +
            "例：{\"1\":\"上ﾚｰﾙ\",\"3\":\"上枠\",\"5\":\"その他\"}";

        private static readonly object SyncRoot = new object();

        private static MemberNamePromptSet _cache;

        private static string _cacheKey;

        private static string _lastLoadError;

        /// <summary>最近一次读取提示语文件/资源失败的原因（正常时为空）。</summary>
        public static string LastLoadError
        {
            get { return _lastLoadError ?? string.Empty; }
        }

        /// <summary>提示语文件的全路径（exe 目录\Prompts\MemberNameAdvisor.Prompt.txt）。</summary>
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

                string location = typeof(MemberNamePromptStore).Assembly.Location;

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
        public static MemberNamePromptSet Load()
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
        private static MemberNamePromptSet LoadCore(string path)
        {
            _lastLoadError = null;

            MemberNamePromptSet external = BuildSet(TryReadFile(path),
                SourceExternal + "：" + path);

            if (external != null)
            {
                return external;
            }

            MemberNamePromptSet embedded = BuildSet(TryReadEmbedded(),
                SourceEmbedded + "（" + EmbeddedResourceName + "）");

            if (embedded != null)
            {
                return embedded;
            }

            SetLoadError("读不到提示语文件与内置资源，已使用内置默认提示语：" + path);

            return new MemberNamePromptSet(DefaultSystemPrompt, DefaultUserTemplate, SourceDefault);
        }

        /// <summary>把提示语文本解析成提示语集合；缺少 [system] / [user] 时返回 null。</summary>
        private static MemberNamePromptSet BuildSet(string text, string source)
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

            MemberNamePromptSet set = new MemberNamePromptSet(sections[SectionSystem],
                sections[SectionUser], source);

            if (set.SystemPrompt.Length == 0 || set.UserTemplate.Length == 0)
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
        private static void FlushSection(IDictionary<string, string> sections, string name, StringBuilder content)
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
                Assembly assembly = typeof(MemberNamePromptStore).Assembly;

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
