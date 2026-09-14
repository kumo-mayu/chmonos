namespace BoothAssetManager.Core.Models;

/// <summary>
/// 画面が覚えている状態（<c>ui-state.json</c>）。
///
/// **設定（settings.json）から切り離した**（技術的負債 3-2・ユーザ判断 2026-09-14）。
/// ユーザが決める設定ではなく、前回の続きから始めるためにアプリが覚えているだけの値で、押すたびに書く。
/// 設定と同じファイルにあると、手で開いたときに「触るところ」に見え、畳むたびに設定ファイルを書き直していた。
/// 消しても困らない（次の起動が既定の見た目で始まるだけ）。
/// </summary>
public sealed record UiState
{
    /// <summary>ナビを畳んでいるか。</summary>
    public bool NavCollapsed { get; init; }

    /// <summary>
    /// 検索の絞り込みパネルを畳んでいるか。
    ///
    /// 畳んでも条件は生きたままなので、畳んだ姿には**効いている条件の数**を出す。
    /// 「なぜか商品が少ない」の原因が畳んだパネルの中にあると、探す場所が無くなる。
    /// </summary>
    public bool FilterPanelCollapsed { get; init; }

    /// <summary>
    /// 検索の絞り込みに積んでいる条件の種類。
    ///
    /// 種類だけを覚えて値は覚えない。値まで戻すと「なぜか商品が少ない」状態で始まり、
    /// 原因が畳まれた条件の中にあると気付けない。
    /// 起動したときにまず全件が見えている方が安全。
    /// </summary>
    public IReadOnlyList<string> SearchExtraFilters { get; init; } = [];

    /// <summary>
    /// 終了時のウィンドウの位置と大きさ。未保存（初回）は null。
    /// 復元時に、今あるモニタのどれとも重ならなければ捨てて中央に開く。
    /// </summary>
    public WindowPlacement? Window { get; init; }

    /// <summary>
    /// ドラッグで変えた画面の幅（px）。鍵は画面と場所（<c>"folder.list"</c>・<c>"edit.right"</c> など）。
    /// 無い物は既定の幅（ユーザ判断 2026-09-14：画面ごとに覚える。境目のダブルクリックと設定画面で戻す）。
    /// 動かせる範囲はアプリの <c>PaneWidths</c> にあり、手で書き換えた値もそこで範囲に収める。
    /// </summary>
    public IReadOnlyDictionary<string, double> PaneWidths { get; init; } = new Dictionary<string, double>();

    /// <summary>
    /// 商品をカードではなくリストで出している画面（<c>"search"</c>・<c>"folder"</c>）。無ければカード（ユーザ指示 2026-09-14）。
    /// リストの列の幅は <see cref="PaneWidths"/> に <c>"search.col.name"</c> のような鍵で入る。
    /// </summary>
    public IReadOnlyList<string> ItemListScreens { get; init; } = [];
}
