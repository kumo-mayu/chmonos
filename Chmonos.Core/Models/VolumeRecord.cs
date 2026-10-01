namespace Chmonos.Core.Models;

/// <summary>
/// ドライブ文字と、記録したパスがそこにあったときのボリュームの組（<c>volumes.json</c>・ユーザ判断 2026-09-14）。
///
/// **外付けは挿す順でドライブ文字が変わる。**手元のファイルは <c>E:\…</c> のように文字込みで記録するので、
/// 同じ外付けが F: で見えたとき、フォルダビューは記録の <c>E:\…</c> を今の <c>F:\…</c> として出す。
/// 見分けはボリュームの通し番号（フォーマットしない限り変わらず、重なることもほぼ無い）。
/// ラベルは既定の名前（「ボリューム」「USB DRIVE」・空欄）のままのことが多く、2台が同じ名前になり得るので見分けに使わない（表示には使う）。
/// 手元のファイルの記録には何も足さない。増えるのは持っているボリュームの数だけの行。
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
