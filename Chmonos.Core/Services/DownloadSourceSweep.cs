using BoothZipInspector;
using BoothZipInspector.Models;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 前からのファイルの行に、ダウンロード元（<see cref="LocalFileRecord.DownloadSource"/>）を1回だけ読んで埋める起動時の見回り（ユーザ判断 2026-11-10）。
/// </summary>
/// <remarks>
/// - 読むのは欄の無い（まだ読んでいない）行だけ。読めば、BOOTH の物でなくても中身の空の物を書くので、次の起動からは読まない。
///   v1.3.0 に上げた最初の起動だけ全部の行を読み、その後は新しく未読になった行（古い未確定から選んだ物）だけになる。
///   Zone.Identifier を読むのは印の無いファイル2万件で約0.25秒（<see cref="ZoneIdentifierReader"/> の測り）なので、数千件なら1回で済ませる。
/// - **別の中身を読まない**：記録の場所に今ある物が同じ中身とは限らない（同じ文字に別のディスク・同じ名前で上書きした更新版をまだ取り込んでいない）。
///   違う中身の印を残すと「上げ直された前か後か」が逆になるので、在ると見た場所（ディスクの通し番号も照らす）で、大きさが記録と同じ、
///   走査の控えがその場所を知っていれば大きさ・日時・ハッシュも合うときだけ読む。合わない・無い・つながっていない行は書かず、次の起動でまた見る。
/// - 書くのは商品ごとの錠の中で、まだ読んでいない行にだけ（その間に取り込みが書いた値を上書きしない）。BOOTH には問い合わせない。画面には出さない。
/// </remarks>
/// <param name="newProbe">1回の見回りごとに作る見方（つながっていないドライブは見に行かない）。</param>
/// <param name="readZone">Zone.Identifier を読む口。試験が読んだ回数を数えるために差し替える。</param>
public sealed class DownloadSourceSweep(
    DataStore store,
    Func<FilePresenceProbe> newProbe,
    Func<string, ZoneIdentifierInfo>? readZone = null)
{
    private readonly Func<string, ZoneIdentifierInfo> _readZone = readZone ?? ZoneIdentifierReader.Read;

    /// <summary>見回る。書いた商品の ID を返す。全体を呼んだスレッドの外で回す。</summary>
    public Task<IReadOnlyList<string>> SweepAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => RunAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken)
    {
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var pending = loaded.Items
            .Where(item => item.Local.LocalFiles.Any(file => file.DownloadSource is null))
            .ToList();
        if (pending.Count == 0)
        {
            return [];
        }

        var probe = newProbe();
        var cached = new Dictionary<string, ScanCacheEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in store.ScanCache.Load())
        {
            cached[entry.Path] = entry;
        }

        var written = new List<string>();
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = new Dictionary<string, DownloadSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in item.Local.LocalFiles.Where(file => file.DownloadSource is null))
            {
                if (ReadOne(file, probe, cached) is { } source)
                {
                    found[file.Hash] = source;
                }
            }

            if (found.Count == 0)
            {
                continue;
            }

            var changed = await store.Items.ChangeLocalAsync(
                item.Id,
                current =>
                {
                    var any = false;
                    var files = current.LocalFiles
                        .Select(file =>
                        {
                            if (file.DownloadSource is not null || !found.TryGetValue(file.Hash, out var source))
                            {
                                return file;
                            }

                            any = true;
                            return file with { DownloadSource = source };
                        })
                        .ToList();
                    return any ? current with { LocalFiles = files } : null;
                },
                LocalOwners.Import,
                cancellationToken);

            if (changed)
            {
                written.Add(item.Id);
            }
        }

        return written;
    }

    /// <summary>
    /// 記録の中身が今ある場所を順に読み、BOOTH の値が読めた最初の物を返す。どこにも値が無ければ、読めた場所があり、
    /// 見に行けない場所（つながっていないドライブ・確かめられない場所）が無いときだけ空の物（読んだ印）。それ以外は null（書かない）。
    /// </summary>
    /// <remarks>
    /// 1か所目が印の無い写し（USB・クラウドから戻した物）で、2か所目が元のダウンロードということがある。1か所目の空で印を付けると、元の場所を二度と読まない。
    /// 外付けの上の場所も同じで、つながったときに読めるよう未読のまま残す。
    /// </remarks>
    private DownloadSource? ReadOne(LocalFileRecord file, FilePresenceProbe probe, IReadOnlyDictionary<string, ScanCacheEntry> cached)
    {
        var readEmpty = false;
        var unseen = false;
        foreach (var path in file.Paths)
        {
            var presence = probe.PlaceOf(path, volume: PlaceVolumes.Of(file, path));
            if (presence != FilePresence.Present)
            {
                unseen |= presence is FilePresence.OnDetachedDrive or FilePresence.Unverifiable;
                continue;
            }

            long size;
            DateTimeOffset modified;
            try
            {
                var info = new FileInfo(path);
                size = info.Length;
                modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unseen = true;
                continue;
            }

            if (size != file.SizeBytes)
            {
                continue;
            }

            // 走査の控えがこの場所を知っていれば、控えた時から変わっておらず、同じ中身のときだけ（控えの無い場所は大きさだけで見る）
            if (cached.TryGetValue(path, out var entry)
                && (entry.SizeBytes != size || entry.ModifiedAtUtc != modified
                    || !string.Equals(entry.Hash, file.Hash, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var source = DownloadSources.From(_readZone(path));
            if (!source.IsEmpty)
            {
                return source;
            }

            readEmpty = true;
        }

        return readEmpty && !unseen ? new DownloadSource() : null;
    }
}
