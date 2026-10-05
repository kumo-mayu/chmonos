namespace Chmonos.Core.Models;

/// <summary>
/// ドライブ文字と、記録したパスがそこにあったときのボリュームの組（<c>volumes.json</c>・ユーザ判断 2026-09-14）。
///
/// **外付けは挿す順でドライブ文字が変わる。**手元のファイルは <c>E:\…</c> のように文字込みで記録するので、
/// 同じ外付けが F: で見えたとき、フォルダビューは記録の <c>E:\…</c> を今の <c>F:\…</c> として出す。
/// 見分けはボリュームの通し番号（フォーマットしない限り変わらず、重なることもほぼ無い）。
/// ラベルは既定の名前（「ボリューム」「USB DRIVE」・空欄）のままのことが多く、2台が同じ名前になり得るので見分けに使わない（表示には使う）。
/// 1つの文字に1台しか覚えないので、2台の外付けが日によって同じ文字を使うと見分けられない。2026-10-05 から手元のファイル・登録したフォルダの記録も
/// 場所ごとに通し番号を持ち（<see cref="LocalFileRecord.Volumes"/>・<see cref="LocalFolderRecord.Volume"/>・点検の3）、持っていればそちらで見分ける。
/// この控えは番号をまだ持たない記録と、走査の控えの見分けに使う。
/// </summary>
public sealed record VolumeRecord
{
    /// <summary>ドライブ文字（<c>E:</c>）。</summary>
    public required string Letter { get; init; }

    /// <summary>ボリュームの通し番号（16進8桁）。</summary>
    public required string Serial { get; init; }

    public string? Label { get; init; }

    /// <summary>最後にこの組を確かめた時刻。</summary>
    public DateTimeOffset SeenAt { get; init; }
}

/// <summary>今つながっているボリューム1つ。</summary>
public sealed record MountedVolume(string Letter, string Serial, string? Label);

/// <summary>今つながっているボリュームを読む（Windows から。試験では作り物を渡す）。</summary>
public interface IVolumeReader
{
    IReadOnlyList<MountedVolume> Mounted();
}
