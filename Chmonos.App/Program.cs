using System.Runtime.CompilerServices;

namespace Chmonos.App;

/// <summary>
/// exe の入口。<c>--mcp</c> のときだけ、WPF にも保存先にも触れる前に MCP の通訳（<see cref="Mcp.McpProgram"/>）へ分かれる（docs/spec/mcp.md）。
///
/// App.xaml から作られる入口（<c>App.Main</c>）の前に置くのは、通訳では画面の起動の何も走らせないため：
/// <c>App</c> の静的な初期化は「保存先を開いてよい」印を立て、<c>OnStartup</c> は保存先の確かめ・多重起動の錠・ログの書き先・動いている一覧の札を進める。
/// 通訳は API に話すだけなので、どれも要らず、立てると画面の起動と取り違える（錠を取ると、その保存先の Chmonos が起動できなくなる）
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Mcp.McpProgram.IsRequested(args))
        {
            return Mcp.McpProgram.Run(args);
        }

        RunApp();

        // 入口が void だった頃と同じ終わりの番号（void の入口は Environment.ExitCode で終わる）
        return Environment.ExitCode;
    }

    /// <summary>
    /// 画面の起動（App.xaml の入口と同じ）。別の関数にして畳み込ませないのは、通訳の起動で WPF のアセンブリを読ませないため
    /// （関数を初めて動かすときに、その中で使う型のアセンブリが読まれる）
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp() => App.Main();
}
