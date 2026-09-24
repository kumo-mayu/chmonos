namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 中断した取り込みの記録（<c>import-state.json</c>）。
///
/// **①②の途中で閉じると、「IDは分かったがまだ取得していない商品」の一覧が消える。**
/// その一覧はメモリにしかなく、<c>unresolved.json</c> はIDが決まらなかったファイルしか持たない。
/// 次に取り込みを押せば走査からやり直して残りを取得するが、
/// **中断したこと自体は黙って起きる**ので、閉じた時に何件残っていたかをユーザは覚えていない。
///
/// 持つのは3項目だけ。設計方針の「保存するデータは増やさない」に反しない——
/// あれは**実態とずれる恐れのあるフラグを持たない**という意味（画像の取得済みフラグの話）で、
/// これは**過去に起きたことの記録**であり、突き合わせる相手がいないのでずれようがない。
///
/// 取り込みが最後まで終わったら消す。
/// </summary>
public sealed record ImportState
{
    /// <summary>取得できた商品の数。</summary>
    public int Done { get; init; }

    /// <summary>取得しようとしていた商品の数。</summary>
    public int Total { get; init; }

    public DateTimeOffset StoppedAt { get; init; }

    /// <summary>
    /// 何を対象にしていたか。**続きから進むボタンが、これを積み直して始める。**
    ///
    /// 持たないと「続きから」が押せない。取り込み対象は次の起動で空に戻る（履歴とは別物）ので、
    /// 人は履歴まで送って積み直す必要があり、「もう一度押すと続きから進みます」という案内が
    /// 押せないボタンを指していた（ユーザ指摘 2026-09-22）。
    /// 履歴を全部積むのでは、そのとき対象にしていなかったフォルダまで走査してしまう。
    /// </summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>
    /// ①で**BOOTH の不調（タイムアウト・5xx・読めない応答）で取れなかった商品**と、その手元のファイル
    /// （ユーザ判断 2026-09-23・#10「続きから」に残して取り直す）。
    ///
    /// 前はその回を飛ばすだけで、商品は作られず未確定にも入らず、ファイルがどこにも出てこなかった。
    /// しかもハッシュは走査の控えに入るので、監視も「新しいファイル」と数えず、黙って消えたように見えた。
    /// 取り込みが最後まで走っても、ここが空でなければ記録を消さない。取り直せた・取り込み直して片付いたら外す。
    /// </summary>
    public IReadOnlyList<UnfetchedItem> Unfetched { get; init; } = [];

    /// <summary>①が途中で止まったままか（最後まで走っていれば 0 / 0 で書く）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool WasInterrupted => Total > 0 && Done < Total;

    /// <summary>取れなかった商品（手で直した JSON の null も空として読む）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<UnfetchedItem> UnfetchedItems => Unfetched ?? [];

    /// <summary>
    /// 読む価値があるか。
    ///
    /// **<see cref="Done"/> が <see cref="Total"/> に届いていれば出さない。**
    /// この記録が warn しているのは「まだ取得していない商品が残っている」ことで、
    /// ①が全部終わった後（②以降）で閉じた場合は、失われたものが無い
    /// ——説明文も画像も、次の起動で自動的に続きから取りに行く。
    /// そこで「2 / 2 件まで進んで中断しました」と出すのは、
    /// 何も起きていないことを知らせているだけになる。
    /// BOOTH の不調で取れなかった商品が残っていれば、最後まで走っていても出す。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasProgress => WasInterrupted || UnfetchedItems.Count > 0;

    /// <summary>
    /// 「続きから進む」で積み直す物。前回の対象と、取れなかった商品のファイル。
    /// 最後まで走った回は対象を持たない（全部を走査し直さず、取れなかった物だけを読み直す）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> ResumeTargets
        => (Targets ?? []).Concat(UnfetchedItems.SelectMany(item => item.PathList))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 出す1行。
    ///
    /// 保存しないのは、**この2つは持っている数から必ず導ける**から。
    /// 書き出すと、数と文が食い違ったときにどちらが正しいか分からなくなる。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Text
    {
        get
        {
            var failed = UnfetchedItems.Count;
            if (WasInterrupted)
            {
                var text = $"前回は {Done} / {Total} 件まで進んで中断しました";
                return failed > 0 ? text + $"（うち {failed} 件はBOOTHの不調で取れませんでした）" : text;
            }

            // 次にやることまで書く（空表示とエラーには次の手を・ui-empty-and-errors.md）。
            // 不調はしばらくすると直るので、待ってから押せば取れる
            return failed > 0
                ? $"前回の取り込みで {failed} 件はBOOTHの不調で取れませんでした。少し待ってから「続きから進む」で取り直せます"
                : string.Empty;
        }
    }
}

/// <summary>①で取れなかった商品1件。</summary>
public sealed record UnfetchedItem
{
    public required string ItemId { get; init; }

    /// <summary>この商品と分かった手元のファイル。取り直すときに読み直し、監視は取り直すまで「新しい」と数える。</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> PathList => Paths ?? [];
}
