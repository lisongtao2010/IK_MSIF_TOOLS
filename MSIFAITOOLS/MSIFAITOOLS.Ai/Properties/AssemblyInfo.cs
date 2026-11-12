using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// -----------------------------------------------------------------------------
// AI 调用层（MSIFAITOOLS.Ai）
//
// 这里只放和 AI 接口交互的代码：连接配置、HTTP 客户端、提示语加载。
// 不引用 WinForms，也不认识界面层的任何类型；依赖方向永远是
//   MSIFAITOOLS（WinForms，exe） → MSIFAITOOLS.Ai（dll)
// -----------------------------------------------------------------------------

[assembly: AssemblyTitle("MSIFAITOOLS.Ai")]
[assembly: AssemblyDescription("MSIFAITOOLS 的 AI 调用层（OpenAI 兼容接口的配置 / HTTP 客户端 / 提示语加载）")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("MSIFAITOOLS.Ai")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// 这些类型原本都在 MSIFAITOOLS.exe 内部，声明为 internal。
// 拆成独立程序集后，为了不改动界面层几十个调用点（也不需要把它们改成 public），
// 用 InternalsVisibleTo 把界面层设为唯一授权的「友好程序集」。
// 将来若别的宿主（如 CadPartSplitter 独立工程）也要用，在这里再加一行即可。
[assembly: InternalsVisibleTo("MSIFAITOOLS")]

[assembly: ComVisible(false)]
[assembly: Guid("7f3b9c41-2d8e-4a6b-9c15-8e0d4b6a1f72")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
