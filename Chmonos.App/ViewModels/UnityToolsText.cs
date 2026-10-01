using Chmonos.App.Services;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// Unity Hub・VCC・ALCOM の一覧を名指しする文。**手元にある物の名前で呼ぶ**（公開前の点検 2026-10-01・ユーザ判断「そのように修正して良いです」）。
/// </summary>
/// <remarks>
/// ALCOM は VCC と同じ一覧を書く（alcom.md §2-2）ので、読み取りの上ではどちらも「VCC の一覧」になる。
/// それを「VCC」と出すと、ALCOM しか入れていない人には知らない名前になる。
/// 呼び名は改変の画面に出しているボタン（「VCCを開く」「ALCOMを開く」）に合わせる——同じ画面で、上のボタンは ALCOM、
/// 下の文は VCC と言い分かれていた。ボタンの出し分け（<see cref="ProjectManagerApps.Buttons"/>）は片方だけならその方、
/// 両方あれば設定の方なので、呼び名も同じ決まりになる。どちらも無いときは「VCC」（ボタンも「VCCを開く」を押せない形で出す）。
/// 文言を足すときは、画面ごとに名前を組まずにここへ足す（同じ種類の言い方が画面で食い違わないように）。
/// </remarks>
internal static class UnityToolsText
{
    /// <summary>VCC の形の一覧を、どちらの名前で呼ぶか。</summary>
    public static string ManagerName(UnityTools tools, ProjectManagerChoice choice)
        => tools.Buttons(choice).ShowAlcom ? "ALCOM" : "VCC";

    private static bool HasManager(UnityTools tools) => tools.HasVcc || tools.HasAlcom;

    /// <summary>
    /// プロジェクトの一覧が空のとき。改変の画面の Unityプロジェクトの見方と、改変の右側の「紐付ける先」で同じことを言う。
    /// **入っていないのか、入っているがプロジェクトが無いのかを言い分ける**（ユーザ判断 2026-09-13）。
    /// </summary>
    /// <remarks>
    /// 見つからなかった物（Hub だけの PC での VCC・ALCOM）は書かない。前は文末の括弧で足していたが、1つの文に
    /// 「空」「見つからない」「次にやること」の3つが載る（ui-writing.md：多くても2つ）。VCC・ALCOM が無いことは、
    /// 上の「VCCを開く」が押せない形と、その吹き出し（「VCCかALCOMを入れると…」）が言う。
    /// Hub が無いことは「Unityを開く」で要るときに言う（<see cref="UnityOpenText"/>）。
    /// 作る先は両方あれば「VCCかALCOM」——どちらで作っても同じ一覧に並ぶ。
    /// </remarks>
    public static string ProjectsEmpty(UnityTools tools, ProjectManagerChoice choice)
    {
        var name = ManagerName(tools, choice);
        var maker = tools.HasVcc && tools.HasAlcom ? "VCCかALCOM" : name;
        return (tools.HasHub, HasManager(tools)) switch
        {
            (false, false) =>
                "Unity HubもVCCもALCOMも見つかりませんでした。どれかを入れてプロジェクトを作るか開くと、ここに並びます。",
            (true, false) => "Unity Hubの一覧にプロジェクトがありません。Hubでプロジェクトを作るか開くと、ここに並びます。",
            (false, true) => $"{name}の一覧にプロジェクトがありません。{maker}でプロジェクトを作るか開くと、ここに並びます。",
            _ => $"Unity Hubと{name}の一覧にプロジェクトがありません。どちらかでプロジェクトを作るか開くと、ここに並びます。",
        };
    }

    /// <summary>「紐付ける先」の「読み直す」の吹き出し。どれも無くても読み直せる（入れて戻ってきたとき）。</summary>
    public static string RefreshHint(UnityTools tools, ProjectManagerChoice choice) => (tools.HasHub, HasManager(tools)) switch
    {
        (false, false) => "Unityプロジェクトの一覧を読み直します。",
        (true, false) => "Unity Hubの一覧を読み直します。",
        (false, true) => $"{ManagerName(tools, choice)}の一覧を読み直します。",
        _ => $"Unity Hubと{ManagerName(tools, choice)}の一覧を読み直します。",
    };

    /// <summary>候補に無いプロジェクトへ紐付け直すには、どこに足せばよいか（紐付けを差し替える確認の窓）。</summary>
    public static string AddProjectFirst(UnityTools tools, ProjectManagerChoice choice) => (tools.HasHub, HasManager(tools)) switch
    {
        (false, false) => "Unity HubかVCCかALCOMを入れて、プロジェクトを追加してください。",
        (true, false) => "先にUnity Hubにプロジェクトを追加してください。",
        (false, true) => $"先に{ManagerName(tools, choice)}にプロジェクトを追加してください。",
        _ => $"先にUnity Hubか{ManagerName(tools, choice)}にプロジェクトを追加してください。",
    };

    /// <summary>開いている Unity のプロジェクトの場所が、どの一覧からも引けなかった理由（「Unityで選択」）。</summary>
    public static string NotListed(UnityTools tools, ProjectManagerChoice choice) => (tools.HasHub, HasManager(tools)) switch
    {
        (false, false) => "Unity HubもVCCもALCOMも見つかりませんでした。",
        (true, false) => "Unity Hubの一覧に無いプロジェクトです。",
        (false, true) => $"{ManagerName(tools, choice)}の一覧に無いプロジェクトです。",
        _ => $"Unity Hubにも{ManagerName(tools, choice)}にも無いプロジェクトです。",
    };

    /// <summary>
    /// どこで見つけたか（改変の画面の右の欄の「情報元」）。こちらは入っているかではなく、実際に載っていた一覧で言う
    /// ——アプリを消しても一覧のファイルは残り、載っていた事実は変わらない。名前だけ手元の方に合わせる。
    /// </summary>
    public static string SourceValue(UnityProjectSource source, UnityTools tools, ProjectManagerChoice choice) => source switch
    {
        UnityProjectSource.Hub | UnityProjectSource.Vcc => $"Unity Hub・{ManagerName(tools, choice)}",
        UnityProjectSource.Hub => "Unity Hub",
        UnityProjectSource.Vcc => ManagerName(tools, choice),
        _ => "改変から紐付けたもの",
    };
}
