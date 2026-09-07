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

    // --- 取り込み ---

    /// <summary>取り込み元フォルダの履歴。ファイルが欠落した時の再スキャン範囲も兼ねる。</summary>
    public IReadOnlyList<string> ImportFolders { get; init; } = [];

    // --- 更新 ---

    /// <summary>商品情報の更新間隔（日）。</summary>
    public int RefreshIntervalDays { get; init; } = 7;

    /// <summary>更新予定日のばらつき（±日）。取得が特定の日に集中するのを防ぐ。</summary>
    public int RefreshJitterDays { get; init; } = 3;

    public bool NotifyOnUpdateByDefault { get; init; } = true;

    /// <summary>非公開と判断するまでの404の連続回数。一時エラーはここに数えない。</summary>
    public int NotFoundThreshold { get; init; } = 3;

    /// <summary>要確認の履歴を残す件数。超えたら古い既読から捨てる。</summary>
    public int NotificationRetentionCount { get; init; } = 200;

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

    /// <summary>保存する画像の長辺（px）。</summary>
    public int ImageMaxEdgePixels { get; init; } = 384;

    /// <summary>
    /// 復号済みサムネイルを保持する上限（MB）。超えたら最後に見てから古いものから捨てる。
    /// 保持しているのは圧縮前の生ピクセルで、ディスク上の30倍以上になる点に注意。
    /// </summary>
    public int ThumbnailCacheBudgetMb { get; init; } = 192;

    /// <summary>WebPの品質（0-100）。</summary>
    public int ImageQuality { get; init; } = 80;
}
