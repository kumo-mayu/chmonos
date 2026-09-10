namespace BoothAssetManager.Core.Models;

public enum ThumbnailSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// 設定（<c>settings.json</c>）。既定値はそのまま初回起動時の設定になる。
/// recordにしているのは、1項目だけ変えて保存し直す（<c>with</c>）用途があるため。
/// </summary>
public sealed record AppSettings
{
    // --- 表示 ---

    /// <summary>商品ページでタグを上部に置くか。BOOTHは下部だが、検索対象なので既定は上部。</summary>
    public bool TagsAtTop { get; init; } = true;

    public bool ShowSubTagsInList { get; init; }

    /// <summary>R-18を表示するか。オフのときは検索・ショップ件数から除くが、統計の金額には含める。</summary>
    public bool ShowAdult { get; init; } = true;

    /// <summary>非表示中の件数を検索結果に出すか。既定はオフ（人前で開いても気付かれないように）。</summary>
    public bool ShowHiddenCountInSearch { get; init; }

    public ThumbnailSize ThumbnailSize { get; init; } = ThumbnailSize.Medium;

    /// <summary>
    /// サムネイルにどの役割の画像を出すか。
    ///
    /// 既定は「デフォルト」。商品ごとの★の指名が効くのはこのときだけ。
    /// </summary>
    public ThumbnailRole ThumbnailRole { get; init; } = ThumbnailRole.Default;

    /// <summary>
    /// 商品ページのギャラリーを、サムネイル一覧に乗せるだけで切り替えるか。
    /// オフのときはクリックだけで切り替える。検索結果のカードは常にホバー切り替え。
    /// </summary>
    public bool GallerySwitchOnHover { get; init; } = true;

    /// <summary>
    /// サムネイルに乗ってから切り替わるまでの滞留時間（ミリ秒）。
    /// 一覧が複数行になると、下の行へ向かう途中の行を通過するだけで画像が変わってしまう。
    /// 止まったときだけ切り替えることで、移動方向にも枚数にも左右されなくなる。
    /// 0にすると即時切り替えになる。
    /// </summary>
    public int GalleryHoverDelayMs { get; init; } = 150;

    // --- 編集 ---

    /// <summary>編集キューを終えたあと、自動で検索画面へ戻るか。</summary>
    public bool ReturnToSearchWhenEditDone { get; init; } = true;

    /// <summary>自動で戻るまでの秒数。終わったことを読む間を置く。</summary>
    public int ReturnToSearchDelaySeconds { get; init; } = 3;

    // --- 取り込み ---

    /// <summary>取り込み元フォルダの履歴。ファイルが欠落した時の再スキャン範囲も兼ねる。</summary>
    public IReadOnlyList<string> ImportFolders { get; init; } = [];

    /// <summary>
    /// 監視対象フォルダ。起動時に、この中に新しいファイルが無いかを見る。
    ///
    /// <see cref="ImportFolders"/> とは意味が違うので別に持つ。
    /// あちらは「ここから取り込んだことがある」という履歴で、欠落復旧の再スキャン範囲。
    /// こちらは<b>ユーザが「ここを見ておいて」と指示した</b>もので、
    /// 起動時に勝手に走査してよい唯一の範囲。
    ///
    /// 履歴を監視に流用しないのは、**一度取り込んだだけのフォルダを
    /// 以後ずっと見に行くのは指示していない読み取り**だから。
    /// </summary>
    public IReadOnlyList<string> WatchedFolders { get; init; } = [];

    // --- 更新 ---

    /// <summary>商品情報の更新間隔（日）。</summary>
    public int RefreshIntervalDays { get; init; } = 7;

    /// <summary>更新予定日のばらつき（±日）。取得が特定の日に集中するのを防ぐ。</summary>
    public int RefreshJitterDays { get; init; } = 3;

    public bool NotifyOnUpdateByDefault { get; init; } = true;

    /// <summary>
    /// 使っていない間もBOOTHから取り続けるか。
    ///
    /// 前の取り込みで取り切れなかった画像を、次の起動で取り直す（梯子の⑤の再開）。
    /// 従量制の回線や、しばらく静かにしておきたい場面があるので切れるようにする。
    /// 通信の様子は常設の1行に出るので、**勝手に何かしていると見えるものには
    /// 止める手段が要る**。
    /// </summary>
    public bool ResumeFetchInBackground { get; init; } = true;

    /// <summary>非公開と判断するまでの404の連続回数。一時エラーはここに数えない。</summary>
    public int NotFoundThreshold { get; init; } = 3;

    /// <summary>
    /// 販売終了と見なした商品を確かめ直す間隔（日）。
    ///
    /// 止めないのは、止めた瞬間に「戻ったこと」を知る手段が無くなるため。
    /// 30日より延ばさないのは、**確かめる間隔が「復活している期間」より長いと
    /// 原理的に取り逃す**から。季節ものは1ヶ月ほどしか公開されない。
    /// 100件溜まっても年1,200リクエスト（ゲートを占めるのは年30分）で、
    /// 延ばして浮くのは年に十数分でしかない。
    /// </summary>
    public int DelistedRecheckDays { get; init; } = 30;

    /// <summary>要確認の履歴を残す件数。超えたら古い既読から捨てる。</summary>
    public int NotificationRetentionCount { get; init; } = 200;

    /// <summary>
    /// 検索の履歴を残す件数。
    ///
    /// 横に並ぶスロットなので、増やすほど端まで送る手間が増える。
    /// 名前を付けたものはこの件数では落とさない。
    /// </summary>
    public int SearchHistoryCount { get; init; } = Services.SearchHistory.DefaultLimit;

    // --- 取得のマナー ---

    /// <summary>BOOTHへのリクエスト間隔（ミリ秒）。直列で必ず間隔を空ける。</summary>
    public int FetchIntervalMs { get; init; } = 1500;

    /// <summary>
    /// 429を受けたときに自動で広げる間隔の上限（ミリ秒）。
    /// 「速すぎる」と言われたら次から遅くするのが筋なので、受けるたびに間隔を倍にして、ここで頭打ちにする。
    /// </summary>
    public int FetchIntervalMaxMs { get; init; } = 30000;

    /// <summary>
    /// Retry-Afterで指示された待ち時間に付き合う上限（秒）。
    /// これを超える指示は「今は相手にする気が無い」ということなので、待たずに諦めて次回の実行に回す。
    /// </summary>
    public int MaxRetryAfterWaitSeconds { get; init; } = 60;

    // --- 画像 ---

    /// <summary>
    /// 画像を取ってディスクに置くか。既定は入。
    ///
    /// 切ると梯子の**④⑤⑥がまるごと落ちる**（①②③だけになる）。
    /// 取得の76%が画像なので、切れば取り込みは4分の1の時間で終わる。
    /// 検索・絞り込み・統計は商品JSONだけで成立するので**機能は何も失われない**——
    /// 一覧とギャラリーが文字だけになる。
    ///
    /// 切り替えたあとは再起動を促す。取得の可否が起動時に組み立てたサービスへ
    /// 渡っているため。後から入にしたときは <c>ImageBacklog</c> がそのまま拾う
    /// （対象は商品JSONの枚数とディスクの差で決まるので、新しい仕組みは要らない）。
    /// </summary>
    public bool SaveImages { get; init; } = true;

    /// <summary>保存する画像の長辺（px）。</summary>
    public int ImageMaxEdgePixels { get; init; } = 384;

    /// <summary>
    /// ショップのバナーの長辺。BOOTHは960px幅で出しているので、それを上回る値にしてある。
    /// 原寸（4KB〜4MB）をそのまま置くと重いため、必ずここまで落とす。
    /// </summary>
    public int ShopBannerMaxEdgePixels { get; init; } = 1200;

    /// <summary>
    /// ショップのバナーを確かめ直すまでの日数。
    /// 後から付けたショップもあれば、差し替えられることもあるので、一度の結果で決めつけない。
    /// 毎回見に行くとページ1枚ぶんの通信が要るため、間隔を空ける。
    /// </summary>
    public int ShopBannerRecheckDays { get; init; } = 30;

    /// <summary>
    /// 復号済みサムネイルを保持する上限（MB）。超えたら最後に見てから古いものから捨てる。
    /// 保持しているのは圧縮前の生ピクセルで、ディスク上の30倍以上になる点に注意。
    /// </summary>
    public int ThumbnailCacheBudgetMb { get; init; } = 192;

    /// <summary>WebPの品質（0-100）。</summary>
    public int ImageQuality { get; init; } = 80;

    /// <summary>
    /// 「対応アバター」を宣言している見出し。ここに載る節のリンクだけを対応表明として読む。
    /// 見出しは出品者の自由記述なので、揺れが出たらユーザが足せるようにする。
    /// </summary>
    public IReadOnlyList<string> AvatarSupportHeadings { get; init; } =
        ["対応アバター", "対応モデル", "対応リスト", "対応表", "対応一覧", "Supported", "Compatible"];

    /// <summary>
    /// 読まない見出し。クレジット節のリンクは宣伝画像に使ったモデルへの謝辞で、
    /// 実測では34件中0件しかアバターではなかった。
    /// </summary>
    public IReadOnlyList<string> AvatarIgnoredHeadings { get; init; } =
        ["クレジット", "credit", "thanks", "さんくす", "使用素材", "利用規約", "規約",
         "注意", "更新", "履歴", "導入", "マニュアル", "同梱", "内容物"];

    /// <summary>対応アバターを検出し直すまでの日数。</summary>
    public int AvatarDetectRecheckDays { get; init; } = 90;

    // --- 画面が覚えている状態 ---
    //
    // 以下は設定画面には出さない。ユーザが決める設定ではなく、
    // 前回の続きから始めるためにアプリが覚えているだけの値なので、
    // 設定の一覧に混ぜると「触るところ」に見えてしまう。

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
}

/// <summary>ウィンドウの位置・大きさ・最大化。</summary>
public sealed record WindowPlacement
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    /// <summary>
    /// 最大化していたか。
    /// 位置と大きさは「最大化を解除したときの姿」を保存する。
    /// 最大化中の値を書くと、解除したときに画面いっぱいのまま戻らなくなる。
    /// </summary>
    public bool IsMaximized { get; init; }
}
