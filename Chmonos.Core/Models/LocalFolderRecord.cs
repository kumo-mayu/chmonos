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
}
