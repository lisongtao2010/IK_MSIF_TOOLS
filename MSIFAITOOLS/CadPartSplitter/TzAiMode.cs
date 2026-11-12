namespace CadPartSplitter
{
    /// <summary>
    /// Excel 部品表（<c>classify\tz.json</c>）的 <b>AI 判定与补足</b> 触发方式。
    ///
    /// <para>
    /// 为什么要它：Excel 部品表的版式各家不同（表头拆行 / 列名改写 / 分块版式 / 合并单元格 /
    /// 多块表并排），<c>TzJsonBuilder</c> 的两套规则（5 列标题 + 分块版式）覆盖不到时
    /// 只能一条明细都收不到。AI 只看「这张表被读成了什么」的文本，判断哪些行是明细、
    /// 每个字段是什么，再把结果交给代码回原表核对（见 <see cref="TzAiExtractor"/>）。
    /// </para>
    ///
    /// <para>
    /// <b>默认 Off</b>：AI 会花时间与费用、还可能判错，所以只在显式打开时才调用
    /// （命令行 <c>--tz-ai</c> / <c>--tz-ai always</c>）。
    /// </para>
    /// </summary>
    public enum TzAiMode
    {
        /// <summary>不调用 AI（默认）：只走 <c>TzJsonBuilder</c> 的两套规则。</summary>
        Off = 0,

        /// <summary>
        /// 兜底：只有规则**一条明细都没收到**（或某张表没收到）时才请 AI 判一次。
        /// 规则已经收出明细的表不调 AI —— 省钱，也避免 AI 把规则正确的结果改差。
        /// </summary>
        Fallback = 1,

        /// <summary>
        /// 每张目标表都请 AI 复核一遍：规则收出的明细保留，AI 另外判出、规则漏掉的明细补进来
        /// （按「表名 + 行号 + 品種 + 部品名 + 部品コード」去重）。
        /// 适合版式特别复杂、又要求「宁可多收不漏」的批次；调用次数与费用最高。
        /// </summary>
        Always = 2
    }
}
