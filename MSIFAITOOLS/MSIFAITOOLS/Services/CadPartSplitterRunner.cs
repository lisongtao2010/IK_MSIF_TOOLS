using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// CadPartSplitter 批量分割的执行结果。
    /// </summary>
    internal sealed class CadPartSplitResult
    {
        public CadPartSplitResult()
        {
            ExitCode = -1;
        }

        /// <summary>始终为 true：分割代码就在本程序里，不存在「找不到外部程序」的情况。</summary>
        public bool ExeFound { get; set; }

        /// <summary>实际执行分割的程序路径（就是本程序自己的 exe 路径）。</summary>
        public string ExePath { get; set; }

        /// <summary>退出码为 0 时为 true（含「该文件夹下没有可处理的 PDF」这种情况）。</summary>
        public bool Success { get; set; }

        /// <summary>等价于原命令行退出码。0 = 全部成功，1 = 输入无效，2 = 有失败项。</summary>
        public int ExitCode { get; set; }

        /// <summary>失败原因（成功时为 null）。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>输出末尾若干行（失败时便于排查）。</summary>
        public string OutputTail { get; set; }

        /// <summary>CadPartSplitter 自己输出的汇总行（形如「共 N 张：成功 X，失败 Y，零件合计 Z 个」）。</summary>
        public string Summary { get; set; }

        /// <summary>本次执行耗时（毫秒）。</summary>
        public long ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// 在本进程内执行「零件分割」（原 <c>CadPartSplitter</c> 工程的源码已整体并入本程序）。
    ///
    /// 合并前这里是 <c>Process.Start</c> 拉起独立的 <c>CadPartSplitter.exe</c>，原因有两个：
    ///   * 两边平台不同（本程序 AnyCPU，分割工程 x64 —— OpenCvSharp4 只带 x64 原生库）；
    ///   * 两边原生 DLL 撞名（Docnet.Core 的 pdfium.dll 与 PdfiumViewer 的 pdfium.dll 同名）。
    /// 合并后这些冲突都已消除：
    ///   * 本程序改为 x64（<c>MSIFAITOOLS.csproj</c> 里 <c>PlatformTarget</c>），与 OpenCvSharp4 一致；
    ///   * PDF 渲染统一走 PdfiumViewer，不再引用 Docnet.Core，pdfium.dll 只留一份；
    ///   * PdfPig 统一到 0.1.16；
    ///   * 分割源码（<c>CadPartSplitter</c> 命名空间）直接编译进 MSIFAITOOLS.exe，
    ///     所以这里改成直接调用 <see cref="CadPartSplitter.BatchRunner.Run"/>，不再有子进程。
    ///
    /// 对调用方的约定保持不变：
    ///   * 输出目录仍是 <c>&lt;输入文件夹&gt;\AiAppData\&lt;图纸名&gt;</c>；
    ///   * 逐行日志照样回调给 <c>onLog</c>（含 <c>[DONE]</c> / <c>[批量完成]</c> 汇总行）；
    ///   * 退出码语义不变：0 = 全部成功（含「没有可处理的 PDF」），1 = 输入无效，2 = 有失败项。
    /// </summary>
    internal static class CadPartSplitterRunner
    {
        /// <summary>原独立程序的名称（仅保留给界面提示文案引用，已不再启动它）。</summary>
        public const string ExeFileName = "CadPartSplitter.exe";

        /// <summary>原「显式指定 CadPartSplitter.exe 路径」的环境变量（合并后不再使用，保留以免调用方编译不过）。</summary>
        public const string ExePathEnvironmentVariable = "CADPARTSPLITTER_EXE";

        /// <summary>最多缓存多少行输出（只用于失败时提示，不落盘，防止内存无限增长）。</summary>
        private const int MaxBufferedLines = 4000;

        /// <summary>失败时拼进错误信息的末尾行数。</summary>
        private const int TailLineCount = 8;

        /// <summary>
        /// 执行者路径：分割代码已编译进本程序，这里返回本程序自身的 exe 路径（供界面显示）。
        /// 方法名沿用旧名，调用方无须改动；不再查找任何外部 exe。
        /// </summary>
        public static string FindExePath()
        {
            try
            {
                System.Reflection.Assembly entry = System.Reflection.Assembly.GetEntryAssembly();

                if (entry != null && !string.IsNullOrEmpty(entry.Location))
                {
                    return entry.Location;
                }
            }
            catch (Exception)
            {
                // 取不到入口程序集时退回 BaseDirectory 拼装
            }

            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MSIFAITOOLS.exe");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 在本进程内分割该文件夹下的所有 PDF（原 <c>CadPartSplitter.exe &lt;文件夹&gt;</c> 的等价行为）。
        ///
        /// 说明：
        ///   * 同步阻塞，几十毫秒到数分钟（取决于 PDF 数量），界面线程请放在 Task.Run 里调用；
        ///   * 输出目录与合并前一致：&lt;文件夹&gt;\AiAppData\&lt;图纸名&gt;，与本工具的分解输出目录相同，文件名互不冲突；
        ///   * 逐行日志回调给 <paramref name="onLog"/>（可能就是调用线程，界面更新请自行 Invoke）；
        ///   * 任何异常都不向外抛出，以 <see cref="CadPartSplitResult.Success"/> = false +
        ///     <see cref="CadPartSplitResult.ErrorMessage"/> 返回。
        /// </summary>
        /// <param name="folder">待处理的 PDF 文件夹。</param>
        /// <param name="onLog">逐行日志回调，可为 null。</param>
        /// <returns>执行结果。</returns>
        public static CadPartSplitResult RunBatch(string folder, Action<string> onLog)
        {
            CadPartSplitResult result = new CadPartSplitResult();
            List<string> lines = new List<string>();
            Stopwatch watch = Stopwatch.StartNew();

            // 源码已并入本程序：不再存在「找不到外部程序」这种情况
            result.ExeFound = true;
            result.ExePath = FindExePath();

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                watch.Stop();
                result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
                result.ExitCode = 1;
                result.ErrorMessage = "PDF 文件夹不存在：" + folder;
                return result;
            }

            // 逐行收集：既回调给界面，也留着拼失败提示 / 汇总行（与合并前读 stdout 的做法一致）
            Action<string> onLine = delegate(string line)
            {
                if (line == null)
                {
                    return;
                }

                lock (lines)
                {
                    lines.Add(line);

                    if (lines.Count > MaxBufferedLines)
                    {
                        lines.RemoveAt(0);
                    }
                }

                if (onLog != null)
                {
                    try
                    {
                        onLog(line);
                    }
                    catch (Exception)
                    {
                        // 界面刷新失败不影响分割
                    }
                }
            };

            try
            {
                CadPartSplitter.DetectorOptions options = new CadPartSplitter.DetectorOptions();

                // 输出根目录留空 → <输入文件夹>\AiAppData，每张图纸再各自一个子文件夹
                options.OutputDirectory = null;
                options.Logger = onLine;

                onLine("[INFO] 零件分割：直接调用本程序内的分割模块（不再启动 CadPartSplitter.exe）");
                onLine("[INFO] 输出目录："
                       + CadPartSplitter.DetectorOptions.GetDefaultBatchOutputDirectory(folder));

                // 与合并前完全一致：仅当前文件夹、不递归、页码 0、DPI 300（DetectorOptions 的默认值）
                List<CadPartSplitter.BatchItemResult> items =
                    CadPartSplitter.BatchRunner.Run(folder, options, false, null);

                int ok = 0;

                foreach (CadPartSplitter.BatchItemResult item in items)
                {
                    if (item.Success)
                    {
                        ok++;
                    }
                }

                int failed = items.Count - ok;

                if (items.Count == 0)
                {
                    onLine("[DONE] 该文件夹下没有可处理的 PDF。");
                }
                else if (failed > 0)
                {
                    // 汇总行由 BatchRunner 自己输出（[批量完成] …），这里只补失败清单，方便界面定位
                    onLine("[WARN] 失败清单：");

                    foreach (CadPartSplitter.BatchItemResult item in items)
                    {
                        if (!item.Success)
                        {
                            onLine("        " + Path.GetFileName(item.InputFile) + " → " + item.ErrorMessage);
                        }
                    }
                }

                result.ExitCode = failed > 0 ? 2 : 0;
            }
            catch (Exception ex)
            {
                result.ExitCode = 2;
                result.ErrorMessage = ex.GetType().Name + "：" + ex.Message;

                onLine("[ERROR] " + result.ErrorMessage);
            }

            if (watch.IsRunning)
            {
                watch.Stop();
            }

            result.ElapsedMilliseconds = watch.ElapsedMilliseconds;

            lock (lines)
            {
                result.OutputTail = BuildTail(lines);
                result.Summary = FindSummary(lines);
            }

            result.Success = result.ExitCode == 0;

            if (!result.Success && string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                result.ErrorMessage = DescribeExitCode(result.ExitCode);
            }

            return result;
        }

        /// <summary>
        /// 把退出码翻译成人话（取值范围见 CadPartSplitter.CommandLineRunner）。
        /// </summary>
        private static string DescribeExitCode(int exitCode)
        {
            switch (exitCode)
            {
                case 1:
                    return "输入无效（退出码 1）";

                case 2:
                    return "有部分 PDF 分割失败（退出码 2），详见上方 [CAD] 日志";

                default:
                    return "进程异常结束（退出码 " + exitCode + "）";
            }
        }

        /// <summary>
        /// 取输出末尾若干行，用于失败提示。
        /// </summary>
        private static string BuildTail(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }

            int start = Math.Max(0, lines.Count - TailLineCount);
            StringBuilder builder = new StringBuilder();

            for (int i = start; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                builder.AppendLine(lines[i]);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// 从输出中找出最后一条汇总行（「[DONE] …」或「[批量完成] …」）。
        /// </summary>
        private static string FindSummary(List<string> lines)
        {
            if (lines == null)
            {
                return null;
            }

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("[DONE]", StringComparison.Ordinal) ||
                    line.StartsWith("[批量完成]", StringComparison.Ordinal))
                {
                    return line.Trim();
                }
            }

            return null;
        }

    }
}
