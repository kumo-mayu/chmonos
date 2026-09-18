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
    /// 要確認で「未読のみ」を出しているか（ユーザ判断 2026-09-18：開き直すたびに戻るのが面倒）。
    /// 既定は未読のみ（溜まった既読に埋もれると「新しく起きたこと」が読めない）。
    /// </summary>
    public bool InboxUnreadOnly { get; init; } = true;

    /// <summary>
    /// タグの管理の並べ方（ユーザ指示 2026-09-18）。"name" 名前順／"count" 件数の多い順／"manual" 手で並べた順。
    /// 並べ方を選ぶと `userTags.json` の並びもその順に書き換える（検索の候補の並びも同じものを使うため）
    /// </summary>
    public string TagSort { get; init; } = "name";

    /// <summary>
    /// 検索の絞り込みパネルを畳んでいるか。
    ///
    /// 畳んでも条件は生きたままなので、畳んだ姿には**効いている条件の数**を出す。
    /// 「なぜか商品が少ない」の原因が畳んだパネルの中にあると、探す場所が無くなる。
    /// </summary>
    public bool FilterPanelCollapsed { get; init; }

    /// <summary>
    /// 検索の絞り込みに追加しているモジュールと、その値（ユーザ案 2026-09-15）。
    ///
    /// **値まで覚える**（ユーザ判断 2026-09-16。前は「なぜか商品が少ない」状態で始まるのを避けて種類だけ覚えていたが、
    /// 条件はパネルに並んで見えており、畳んでも効いている数を出すので、続きから始められる方を採った）。
    /// null は一度も保存していない（最初の起動）＝最低限のモジュール（所持・ユーザタグ・対応アバター）で始める。
    /// </summary>
    public IReadOnlyList<SearchModuleState>? SearchModules { get; init; }

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
