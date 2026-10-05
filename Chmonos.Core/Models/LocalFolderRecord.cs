namespace Chmonos.Core.Models;

/// <summary>
/// 商品として所有しているフォルダ1件。zipが手元に無く、展開したものだけが残っている場合に使う。
///
/// 同一性は<b>パス</b>で、ファイルのようなハッシュは持たない。
/// フォルダに内容ハッシュを与えると、中の1ファイルを触っただけで別物になり、
/// また未確定として湧いてしまう。831MBを毎回ハッシュする代償も見合わない。
/// 移動された場合は「見つかりません」として扱い、指し直してもらう。
///
/// 登録された配下はスキャン対象から外れる。所有の記録と、
/// 中身が未確定に溜まり続ける問題が、同時に片付く。
/// </summary>
public sealed record LocalFolderRecord
{
    public required string Path { get; init; }

    /// <summary>直近に数えたファイル数。スキャンのたびに数え直す。</summary>
    public int FileCount { get; init; }

    /// <summary>直近に数えた合計容量。ハッシュは計算せず、列挙して足すだけ。</summary>
    public long TotalBytes { get; init; }

    public DateTimeOffset RegisteredAt { get; init; }

    /// <summary>最後に存在を確かめた日時。見つからない状態が続けば要確認に出す。</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>
    /// 取り込みがこのフォルダを「無い」と見た日時（ユーザ判断 2026-10-04）。また見つかったら消す。無い間は最初に見た日時のまま。
    /// </summary>
    /// <remarks>
    /// 前は無いフォルダを取り込みが黙って飛ばすだけで、フォルダが消えても所持のまま、カードの印も検索の条件「見つからないファイル」も数えなかった。
    /// ファイルは取り込みが無い場所を外す（場所が空になる）ことで「無い」が記録に残るが、フォルダは場所が同一性なので外せない。
    /// だから「無い」と見たことを欄で残し、印・条件・統計はこの記録だけを見る（打つたびにディスクを見に行かない）。
    /// 見に行けない（ドライブがつながっていない）ときは書かない——ファイルの取り込みが外付けの上の場所を残すのと同じ考え。
    /// 計算では出せない（ディスクを見た結果）ので JSON に書く。
    /// </remarks>
    public DateTimeOffset? MissingSince { get; init; }

    /// <summary>
    /// 中の <c>.unitypackage</c> のフォルダからの場所（区切りは <c>/</c>。zip の中の場所と同じ書き方）。<see cref="FileCount"/> と同じく、数えるたびに書き直す（ユーザ判断 2026-10-05・メモ65-③）。
    /// </summary>
    /// <remarks>
    /// 展開してあるので、Unity へ送る・Unityで選択はフォルダの中のファイルをそのまま使える。前は送る候補を zip の中からしか集めず、フォルダだけの商品は送れなかった。
    /// **右クリックのメニューは開くたびに「送れる物があるか」を引く**ので、その場でフォルダの中を並べると、大きなフォルダや HDD で開くたびに待たされる。
    /// 登録したときと取り込みの数え直しは、もともと中を全部並べている（<see cref="FileCount"/>）ので、同じ1回で拾って残す。
    /// ディスクを見た結果で計算では出せないので JSON に書く。読むときは欠けていれば空。
    /// </remarks>
    public IReadOnlyList<string> UnityPackages { get; init; } = [];

    /// <summary>
    /// フォルダが載っているディスクの通し番号（2026-10-05・点検の3。ファイルの <see cref="LocalFileRecord.Volumes"/> と同じ考え）。
    /// 在ると見たときに書き足す。分からなければ書き出さない。
    /// </summary>
    public string? Volume { get; init; }
}
