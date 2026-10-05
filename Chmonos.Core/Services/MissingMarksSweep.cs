using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 記録しているファイルとフォルダの場所を全部見て、「見つからなくなった日時」（<c>missingSince</c>）を付け外しする見回り。
/// 取り込みのたびの見回りと、起動したときの裏の見回り（ユーザ判断 2026-10-05）が、同じここを通る。
/// </summary>
/// <remarks>
/// 取り込みでしか見ていなかった頃は、手で zip を消しても次に取り込むまで印・検索の条件「見つからないファイル」・統計に出ず、
/// 取り込まない人には記録が古いままだった（2026-10-05 の確認で、友人の写しの条件に1件も当たらなかった）。
///
/// - 決まりは取り込みと同じ：在るかはドライブごとにまとめて見る（<see cref="FilePresenceProbe"/>。つながっていないドライブの上は見に行かず書かない・
///   届かない共有の根は打ち切る）。書くのは変わった商品だけ、商品ごとの錠の中で今の値に当てる（<see cref="FileMissingMarks"/>）。場所は外さない。
/// - **見回りは1本ずつ**（<see cref="EnterAsync"/>）。起動時の見回りと取り込みの見回り（取り込みの最初の周回のフォルダの数え直しを含む）が重なると、
///   同じ商品を同じ答えで二度書き、ディスクも二度見る。後から来た方は先の方が済むのを待ち、錠の中で今の値と同じなら書かない。
/// - BOOTH には問い合わせない。
/// </remarks>
public sealed class MissingMarksSweep
{
    private readonly DataStore _store;
    private readonly Func<FilePresenceProbe> _newProbe;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="newProbe">1回の見回りごとに作る見方（ドライブは後でつながることがあるので、覚えたまま持ち越さない）。試験が差し替える。</param>
    public MissingMarksSweep(DataStore store, Func<FilePresenceProbe>? newProbe = null)
    {
        _store = store;
        _newProbe = newProbe ?? (() => new FilePresenceProbe());
    }

    /// <summary>見回りの番を取る。取り込みがフォルダを数え直す間もこれで待ち合わせる。</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Turn(_gate);
    }

    /// <summary>
    /// 起動時の見回り：ファイルとフォルダの両方。書いた商品の ID を返す（画面が検索の写しとカードへ知らせる）。
    /// 全体を呼んだスレッドの外で回す（全件の読み込みとディスクの確かめを画面のスレッドに載せない）。
    /// </summary>
    public Task<IReadOnlyList<string>> SweepAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => RunAsync(folders: true, probe: null, cancellationToken), cancellationToken);

    /// <summary>
    /// 取り込みの見回り：ファイルだけ（フォルダは取り込みが大きさの数え直しと一緒に書く。<c>ImportPipeline.LoadOwnedAsync</c>）。
    /// </summary>
    /// <param name="probe">
    /// 取り込みが登録フォルダの判定に使った見方（同じ周回の中で使い回す）。落ちた共有の根を、フォルダの判定とファイルの見回りで
    /// 二度待たない（1回3秒）。無ければ新しく作る。
    /// </param>
    public Task<IReadOnlyList<string>> NoteFilesAsync(FilePresenceProbe? probe = null, CancellationToken cancellationToken = default)
        => RunAsync(folders: false, probe, cancellationToken);

    /// <summary>
    /// 新しい見方（取り込みが登録フォルダの判定に使う。見回りと同じ部品・同じ打ち切りにするため。spec background-and-network.md）。
    /// 試験が差し替えた作り方もここを通る。
    /// </summary>
    public FilePresenceProbe NewProbe() => _newProbe();


    private async Task<IReadOnlyList<string>> RunAsync(bool folders, FilePresenceProbe? probe, CancellationToken cancellationToken)
    {
        using var turn = await EnterAsync(cancellationToken);

        // 番を待っている間に取り込みが書いた分も見るよう、読むのは番を取ってから
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        probe ??= _newProbe();
        var now = DateTimeOffset.Now;
        var written = new List<string>();

        foreach (var item in loaded.Items)
        {
            var hasFolders = folders && item.Local.LocalFolders.Count > 0;
            if (item.Local.LocalFiles.Count == 0 && !hasFolders)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 外したファイルも見る（記録は事実なので。印と条件は外したファイルを数えない・ユーザ判断 2026-10-05 もこのまま）
            var fileSightings = item.Local.LocalFiles
                .Select(file => new FileSighting(file.Hash, file.Paths, probe.Of(file.Paths)))
                .ToList();
            var folderSightings = hasFolders
                ? item.Local.LocalFolders
                    .GroupBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => probe.OfFolder(group.Key), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, FilePresence>(StringComparer.OrdinalIgnoreCase);

            // 変わる物が無ければ錠も取らない（数千件のうち、ほとんどは前回と同じ）
            if (FileMissingMarks.Apply(item.Local.LocalFiles, fileSightings, now) is null
                && FileMissingMarks.ApplyFolders(item.Local.LocalFolders, folderSightings, now) is null)
            {
                continue;
            }

            var changed = await _store.Items.ChangeLocalAsync(
                item.Id,
                current =>
                {
                    var files = FileMissingMarks.Apply(current.LocalFiles, fileSightings, now);
                    var marked = FileMissingMarks.ApplyFolders(current.LocalFolders, folderSightings, now);
                    return files is null && marked is null
                        ? null
                        : current with
                        {
                            LocalFiles = files ?? current.LocalFiles,
                            LocalFolders = marked ?? current.LocalFolders,
                        };
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

    private sealed class Turn(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
