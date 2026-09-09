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
    /// 読む価値があるか。
    ///
    /// **<see cref="Done"/> が <see cref="Total"/> に届いていれば出さない。**
    /// この記録が warn しているのは「まだ取得していない商品が残っている」ことで、
    /// ①が全部終わった後（②以降）で閉じた場合は、失われたものが無い
    /// ——説明文も画像も、次の起動で自動的に続きから取りに行く。
    /// そこで「2 / 2 件まで進んで中断しました」と出すのは、
    /// 何も起きていないことを知らせているだけになる。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasProgress => Total > 0 && Done < Total;

    /// <summary>
    /// 出す1行。
    ///
    /// 保存しないのは、**この2つは持っている数から必ず導ける**から。
    /// 書き出すと、数と文が食い違ったときにどちらが正しいか分からなくなる。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Text => $"前回は {Done} / {Total} 件まで進んで中断しました";
}
