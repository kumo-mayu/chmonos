namespace Chmonos.Core.Models;

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
    /// 属性の管理の並べ方（ユーザ指示 2026-09-19：タグの管理と揃える）。値はタグと同じ。
    /// 既定は手で並べた順——それまで並べ方はドラッグしか無く、`attributes.json` の並びは人が置いた順なので、
    /// 名前順を既定にすると表示の順と「名前順」の印が食い違う
    /// </summary>
    public string AttributeSort { get; init; } = "manual";

    /// <summary>
    /// 属性の管理で、この属性を持つ商品を縦に流すか（ユーザ指示 2026-09-19：値の順の並びが横固定だと、縦に読む人には追いにくい）。
    /// false なら左から右へ、true なら上から下へ。タグの管理は名前順なので持たない（ユーザ判断 同日）
    /// </summary>
    public bool AttributeItemsVertical { get; init; }

    /// <summary>
    /// 検索の絞り込みパネルを畳んでいるか。
    ///
    /// 畳んでも条件は生きたままなので、畳んだ姿には**効いている条件の数**を出す。
    /// 「なぜか商品が少ない」の原因が畳んだパネルの中にあると、探す場所が無くなる。
    /// </summary>
    public bool FilterPanelCollapsed { get; init; }

    /// <summary>
    /// 絞り込み欄の上の「保存した検索」の節を畳んでいるか（ユーザ判断 2026-10-04・案A3「節は畳める（畳んだ状態は覚える）」）。
    /// 使わない人には条件の並びを押し下げるだけの節なので、畳んだまま次も始める
    /// </summary>
    public bool SavedSearchesCollapsed { get; init; }

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

    /// <summary>
    /// ショップの中の画面でバナーを畳んでいるか（ユーザ指示 2026-09-29：バナーが場所を取りすぎる）。
    /// ショップごとではなく全部のショップで同じ値——1店ずつ畳み直すのは手間なので。既定は出す（今までと同じ見た目から始まる）
    /// </summary>
    public bool ShopBannerHidden { get; init; }

    /// <summary>
    /// フォルダビューの木で、商品のファイルの行を商品名で出すか（ユーザ判断 2026-10-01：ファイル名の所を商品名にする切り替え）。
    /// 既定はファイル名（今までと同じ見た目から始まる。エクスプローラに近い形が先に決まっていた）
    /// </summary>
    public bool FolderRowsShowItemName { get; init; }

    /// <summary>
    /// 改変の詳細の「使ったもの」を、入れた順の逆に見せるか（メモ26-①・ユーザ判断 2026-10-04）。
    /// 依存される物（シェーダー・ライブラリ）が上に来て、人が「使った」と思っている衣装が下に沈むので、逆にも見られるようにした。
    /// **見せる順だけ**で、記録の並びと「順にUnityへ送る」の順は入れた順のまま。全部の改変で同じ値（1つずつ切り替え直すのは手間）。
    /// 既定は入れた順（今までと同じ見た目から始まる）
    /// </summary>
    public bool ModificationMembersReversed { get; init; }

    /// <summary>
    /// 「この新着はもう知らせなくてよい」と言われたときの、新着の顔つき（ユーザ判断 2026-09-21・G16）。
    ///
    /// 監視フォルダに取り込むつもりの無いファイルがあると、起動のたびに同じ件数を知らされ続け、
    /// ファイルを消す以外に黙らせる手が無かった。
    /// **ファイルを1件ずつ覚えると、監視フォルダの大きさに比例して記録が膨らむ。**
    /// 新着の一覧をまとめた指紋（文字列1つ）だけを持てば、
    /// 「前と同じ顔ぶれなら黙る・何か増えたらまた言う」が満たせる。
    /// </summary>
    public string? DismissedWatchNew { get; init; }
}
