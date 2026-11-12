using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MSIFAITOOLS.Services;

namespace MSIFAITOOLS
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            PrepareMemberPartDatabase();

            Application.Run(new Form1());
        }

        /// <summary>
        /// 准备部材部品マスタ 的 SQLite 数据库（默认 <c>D:\MSIFDB\MSIF_MemberParts.db</c>）：
        /// 第一次启动时把 <c>部材部品\部材部品.csv</c> 导入「部材部品」表，之后只读数据库。
        /// 失败不影响启动（【AI 分析】时会再试一次，并把原因写进日志）。
        /// </summary>
        private static void PrepareMemberPartDatabase()
        {
            try
            {
                MemberPartCatalogLoader.LoadFor(null, new List<string>());
            }
            catch (Exception)
            {
                // 数据库 / CSV 都不可用时【AI 分析】只用图纸上的文字，这里静默跳过
            }
        }
    }
}

