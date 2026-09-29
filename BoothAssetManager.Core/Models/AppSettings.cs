namespace BoothAssetManager.Core.Models;

/// <summary>
/// 設定（<c>settings.json</c>）。既定値はそのまま初回起動時の設定になる。
/// recordにしているのは、1項目だけ変えて保存し直す（<c>with</c>）用途があるため。
/// </summary>
public sealed record AppSettings
{
    // --- 表示 ---

    public bool ShowSubTagsInList { get; init; }

    /// <summary>R-18を表示するか。オフのときは検索・ショップ件数から除くが、統計の金額には含める。</summary>
    public bool ShowAdult { get; init; } = true;

    /// <summary>非表示中の件数を検索結果に出すか。既定はオフ（人前で開いても気付かれないように）。</summary>
    public bool ShowHiddenCountInSearch { get; init; }

    /// <summary>
    /// 商品カードの幅（DIP）。一覧の右下のスライダーで変え、カードを並べる画面すべてで共有する（ユーザ判断 2026-09-29）。
    /// 前の設定の「サムネイルの大きさ（小・中・大）」を置き換えた。228 はその「中」で、ずっと使ってきた大きさ。
    /// 範囲の外の値（手で書き換えた JSON）は、使う側（<c>CardMetrics</c>）が範囲に収めて読む
    /// </summary>
    public int CardWidth { get; init; } = 228;

    /// <summary>
    /// 商品のリストの1行の高さ（DIP）。行の頭の絵はこの高さに合わせて大きさが変わる。カードと同じスライダーで、
    /// リストで出しているときに変える（ユーザ判断 2026-09-29）。52 は前の既定（絵40＋上下の余白）
    /// </summary>
    public int ListRowHeight { get; init; } = 52;

    /// <summary>
    /// 表示の大きさ（%）。アプリ全体（主の窓・小窓・メニュー・吹き出し）を大きく・小さくする（ユーザ指示 2026-09-29）。
    /// 設定画面と Ctrl＋＋／Ctrl＋－／Ctrl＋0 で変える。段は <see cref="DisplayZoom.Steps"/>。
    /// 段にない値（手で書き換えた JSON）は、使う側が <see cref="DisplayZoom.Normalize"/> で近い段に丸めて読む
    /// </summary>
    public int DisplayZoomPercent { get; init; } = DisplayZoom.DefaultPercent;

    /// <summary>
    /// 表示の色（明るい／暗い／Windows に合わせる。ユーザ指示 2026-09-29）。変えるとその場で全体の色が変わる。
    /// 既定を Windows に合わせるにしたのは、暗い色で使っている人は Windows の側で既に選んでいるため
    /// </summary>
    public ColorThemeMode ColorTheme { get; init; } = ColorThemeMode.System;

    /// <summary>VCC と ALCOM の両方があるとき、改変の画面から開く方（ユーザ指示 2026-09-29）。</summary>
    public ProjectManagerChoice ProjectManager { get; init; } = ProjectManagerChoice.VccLink;

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

    /// <summary>
    /// 編集画面の上に、続く商品を小さな絵で並べるか（ユーザ指示 2026-09-12）。
    /// 何が続くかを見ながら進めたい人向けで、要らない人もいるので消せる。既定は出す。
    /// </summary>
    public bool ShowEditQueueStrip { get; init; } = true;

    // --- 取り込み ---

    /// <summary>
    /// 取り込み元の履歴（フォルダとファイル）。**何を読んだかを確かめるための記録で、取り込みの対象ではない**
    /// （ユーザ判断 2026-09-21・G3/G4）。取り込み画面の一覧から1件ずつ対象に積む。
    /// 監視の対象とは別（履歴にファイルが入っても監視はしない）。
    /// </summary>
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

    /// <summary>
    /// 窓や取り込み画面へ落としたら、そのまま取り込みを始めるか。**既定は入**（#38・ユーザ判断）。
    /// 落とす操作自体がはっきりした指示なので、もう一度「取り込みを開始」を押させる理由が無い。
    /// 走っている最中に落とした分は今の取り込みに積む。
    /// </summary>
    public bool StartImportOnDrop { get; init; } = true;

    /// <summary>
    /// 起動したとき、監視フォルダに新しいファイルがあれば取り込みを始めるか。**既定は切**（ユーザ判断）。
    /// 起動しただけで BOOTH への通信が走るのを嫌う人がいる。対象は監視フォルダの新着と、前回途中で止まった取り込みの続き
    /// （続きはユーザ判断 2026-09-29。起動時に勝手に走査してよいのは、監視フォルダと前回頼まれた対象だけ。履歴は積まない）。
    /// </summary>
    public bool StartImportOnLaunch { get; init; }

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
    public int FetchIntervalMs { get; init; } = MinFetchIntervalMs;

    /// <summary>
    /// 間隔の下限。**1.5秒より詰めない**（CLAUDE.md の絶対に破らないこと・ユーザ判断 2026-09-11）。
    /// 設定画面は以前500msまで下げられ、約束と食い違っていた。広げるのは自由。
    /// 試験では通信を待たせないため0を使うので、下限は <c>BoothClient</c> ではなく設定の入口で守る。
    /// </summary>
    public const int MinFetchIntervalMs = 1500;

    /// <summary>
    /// 約束の範囲に戻す。1.5秒より短く保存された設定もここで直る。
    /// </summary>
    public AppSettings Normalized()
    {
        var result = this;
        if (result.FetchIntervalMs < MinFetchIntervalMs)
        {
            result = result with { FetchIntervalMs = MinFetchIntervalMs };
        }

        // 手で消した・壊れた settings.json でショートカットが丸ごと null になっていたら既定に戻す
        if (result.Shortcuts is null)
        {
            result = result with { Shortcuts = new ShortcutSettings() };
        }

        return result;
    }

    /// <summary>
    /// ショートカットの割り当て（#43）。設定画面で変えられる（ユーザ判断）。
    /// 既定：保存して次へ＝Ctrl+Enter、スキップ＝Ctrl+Shift+→、前へ＝Ctrl+Shift+←、検索欄へ＝Ctrl+F、戻る＝Alt+←。
    /// </summary>
    public ShortcutSettings Shortcuts { get; init; } = new();

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
    /// 切り替えは保存した直後の取得から効く（サービスは今の設定を毎回読む。<c>SettingsSource</c>）。
    /// 後から入にしたとき、今ある商品の分は次の起動で <c>ImageBacklog</c> がそのまま拾う
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
    /// 改変に貼る写真の長辺（px）。**商品画像とは別に持つ。**
    ///
    /// 用途が違う。商品画像は一覧に並ぶサムネイルだが、改変の写真は
    /// **見て「何を使ったか」を思い出すためのもの**なので、細部が要る。
    ///
    /// 768にした理由は表示の枠。改変詳細の大きい画像は420px（DIP）で、
    /// 150%のDPIでは630物理pxになる。**500pxだとそこで拡大されてぼやける。**
    ///
    /// 容量は問題にならない。実測（2048pxの写真・webp q80）で
    /// 384px=6.2KB／500px=8.6KB／**768px=14.0KB**／原寸=45.6KB。
    /// 500から768へ上げる代償は1枚あたり5.4KBで、
    /// 400枚貼っても6MB——アセット本体が2.5GBある中では無視できる。
    /// </summary>
    public int ModificationImageMaxEdgePixels { get; init; } = 768;

    /// <summary>
    /// 改変の写真を原寸で保存するか。**既定は切。**
    ///
    /// 原寸は4Kのスクリーンショットで1枚200KB前後に伸びる。
    /// 逃げ道としては要るが、既定にすると気付かないうちに膨らむ。
    /// </summary>
    public bool SaveModificationImagesAtOriginalSize { get; init; }

    /// <summary>
    /// ショップのバナーを確かめ直すまでの日数。
    /// 後から付けたショップもあれば、差し替えられることもあるので、一度の結果で決めつけない。
    /// 毎回見に行くとページ1枚ぶんの通信が要るため、間隔を空ける。
    /// </summary>
    public int ShopBannerRecheckDays { get; init; } = 30;

    /// <summary>
    /// 復号済みサムネイルを保持する上限（MB）。超えたら最後に見てから古いものから捨てる。
    /// 保持しているのは圧縮前の生ピクセルで、ディスク上の30倍以上になる点に注意。
    ///
    /// 既定は132MB（下の <see cref="DefaultThumbnailCacheBudgetMb"/>）。192MBでは2000件を最後までスクロールすると
    /// それだけで作業セットが600MBを超え（#71）、32MBでは速く流して戻ったときに読み直しでカクついた（U12）。
    /// 設定画面には出していない。settings.json に書いた値をそのまま使う
    /// </summary>
    public int ThumbnailCacheBudgetMb { get; init; } = DefaultThumbnailCacheBudgetMb;

    /// <remarks>
    /// U12（ユーザ判断 2026-09-11）：一覧として不便なので、あと100MB使ってよい。32MBでは
    /// 速く流して戻ったときに読み直しが起き、カクついた。作業セットの目標は300〜400MB。
    /// </remarks>
    public const int DefaultThumbnailCacheBudgetMb = 132;

    /// <summary>WebPの品質（0-100）。</summary>
    public int ImageQuality { get; init; } = 80;

    /// <summary>
    /// 「対応アバター」を宣言している見出し。ここに載る節のリンクだけを対応表明として読む。
    /// 見出しは出品者の自由記述なので、揺れが出たらユーザが足せるようにする。
    /// </summary>
    public IReadOnlyList<string> AvatarSupportHeadings { get; init; } = DefaultAvatarSupportHeadings;

    /// <summary>
    /// 対応を宣言する見出しの既定。**保存済みの設定にも、使うときに合わせて足す**
    /// （<see cref="AvatarSupportHeadings"/> は settings.json に丸ごと保存されるので、
    /// 既定に語を足しても今の利用者には届かない）。
    ///
    /// 検索用〜セットアップ済は 2026-09-11 に足した。所持207件の実データで、
    /// 「🔍検索用🔍」（アバターの商品IDで探す人向けに対応アバターのURLを並べる）や
    /// 「プリセットについて」「位置設定済アバター」の下に対応の一覧が置かれていた。
    /// </summary>
    public static IReadOnlyList<string> DefaultAvatarSupportHeadings { get; } =
    [
        "対応アバター", "対応モデル", "対応リスト", "対応表", "対応一覧", "Supported", "Compatible",
        "検索用", "プリセット", "位置設定済", "設定済みアバター", "セットアップ済",
    ];

    /// <summary>
    /// 読まない見出し。クレジット節のリンクは宣伝画像に使ったモデルへの謝辞で、
    /// 実測では34件中0件しかアバターではなかった。
    /// </summary>
    public IReadOnlyList<string> AvatarIgnoredHeadings { get; init; } =
        ["クレジット", "credit", "thanks", "さんくす", "使用素材", "利用規約", "規約",
         "注意", "更新", "履歴", "導入", "マニュアル", "同梱", "内容物"];

    /// <summary>対応アバターを検出し直すまでの日数。</summary>
    public int AvatarDetectRecheckDays { get; init; } = 90;

    // 画面が覚えている状態（ナビ・絞り込み欄の畳み方・積んだ条件・窓の位置）は ui-state.json（UiState）へ分けた（技術的負債 3-2）
}

/// <summary>
/// ショートカットの割り当て。**キーの書き方は人が読める文字で持つ**（"Ctrl+Enter"・"Alt+Left"）。
/// settings.json を開いて直せるようにするため。空文字は「割り当てなし」。
/// </summary>
public sealed record ShortcutSettings
{
    /// <summary>編集画面の「保存して次へ」。</summary>
    public string SaveAndNext { get; init; } = "Ctrl+Enter";

    /// <summary>
    /// 編集画面の「スキップ」。**文字の欄の中でも働く**（ユーザ判断 2026-09-20・B11）。
    /// 以前の Ctrl+→ は文字の欄では1語ずつ動く操作で、編集画面はほぼ常に欄の中にいるため一度も効かなかった。
    /// Ctrl+Shift+→ は欄では1語ずつ選ぶ操作だが、選ぶのは Shift+→ とマウスで足りるので、こちらを譲ってもらう。
    /// </summary>
    public string Skip { get; init; } = "Ctrl+Shift+Right";

    /// <summary>
    /// 編集画面の「← 前へ」（ユーザ判断 2026-09-28）。**スキップと違い、文字の欄の中では働かない。**
    /// 欄の中の Ctrl+Shift+← は1語ずつ選ぶ操作で、スキップと両方を取ると欄で語を選ぶ手が矢印キーから消える。
    /// 前へ戻るのは入力を終えてからで足りる、とユーザが決めた。
    /// 前の版の settings.json にはこの項目が無い。読むと既定が入る
    /// </summary>
    public string Previous { get; init; } = "Ctrl+Shift+Left";

    /// <summary>画面の中の文字を探す帯を出す（どの画面でも同じ・B2）。</summary>
    public string FindInPage { get; init; } = "Ctrl+F";

    /// <summary>直前の画面へ戻る（どの画面でも。編集画面の「前の1件へ」は「← 前へ」ボタンと <see cref="Previous"/>）。</summary>
    public string Back { get; init; } = "Alt+Left";

    /// <summary>戻った先からまた進む（ユーザ指示 2026-09-20・M7。ブラウザと同じ形）。</summary>
    public string Forward { get; init; } = "Alt+Right";

    /// <summary>
    /// 表示を1段大きくする（ユーザ指示 2026-09-29）。ブラウザや多くのアプリと同じ Ctrl＋＋。
    /// OemPlus は US 配列の「=」、JIS 配列の「;」のキーで、Shift を足した「＋」とテンキーの＋でも働く
    /// （<c>Shortcuts.Matches</c>）。**文字の欄の中でも働く**——欄でこのキーに割り当てのある操作は無い。
    /// 前の版の settings.json にはこの項目が無い。読むと既定が入る
    /// </summary>
    public string ZoomIn { get; init; } = "Ctrl+OemPlus";

    /// <summary>表示を1段小さくする。Ctrl＋－（テンキーの－でも働く）。</summary>
    public string ZoomOut { get; init; } = "Ctrl+OemMinus";

    /// <summary>表示の大きさを 100% に戻す。Ctrl＋0（テンキーの0でも働く）。</summary>
    public string ZoomReset { get; init; } = "Ctrl+D0";
}

/// <summary>
/// ウィンドウの位置・大きさ・最大化。**位置と大きさは画面の画素（物理ピクセル）**で持つ（2026-09-29）。
/// 前は WPF の DIP で持っていたが、モニターごとの拡大率（Per-Monitor V2）では DIP の座標がモニターの間で重なり、
/// 拡大率の違うモニターで閉じると別の場所・別の大きさで開いた。
/// </summary>
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
