using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 監視対象フォルダに、まだ見ていないファイルが増えていないかを見る。
///
/// **これを起動時に走らせてよいのは、ユーザが「ここを見ておいて」と指示したフォルダだけを見るから。**
/// ①②を自動で始めない根拠は「フォルダの走査はユーザが指示していない読み取りになる」ことだった
/// （docs/history/import-concurrency.md §4）。監視対象はその指示そのものなので、その理由が消える。
/// 裏を返すと、**監視対象に入っていないフォルダは今まで通り走査しない。**
///
/// 見つけるところまでで止め、取り込みは押させる。
/// 走査は手元のディスクを読むだけだが、取り込みはBOOTHへの通信で、
/// 1件あたり十数秒かかる。起動した瞬間に黙って始めると、
/// ユーザがこれからやろうとしていた操作と行列を取り合う。
/// </summary>
public sealed class FolderWatch
{
    private readonly DataStore _store;
    private readonly FolderScanner _scanner;

    public FolderWatch(DataStore store, FolderScanner? scanner = null)
    {
        _store = store;
        _scanner = scanner ?? new FolderScanner();
    }

    /// <summary>
    /// 監視対象の中で、まだ一度も見ていないファイルを数える。
    ///
    /// 「見た」の判定は走査キャッシュ（パス・サイズ・更新日時）で行う。
    /// **ハッシュまでは計算しない。**同じ3つが揃っていれば前と同じファイルなので、
    /// 起動のたびに数百MBを読み直す必要が無い。
    /// </summary>
    public async Task<WatchResult> FindNewAsync(
        IReadOnlyList<string> folders,
        CancellationToken cancellationToken = default)
    {
        if (folders.Count == 0)
        {
            return new WatchResult { Folders = [], NewFiles = [] };
        }

        var cache = new ScanCacheIndex(_store.ScanCache.Load());
        var exclusions = new ExclusionFilter(_store.Excluded.Load());
        var registered = await LoadRegisteredAsync(cancellationToken);

        // BOOTH の不調で取れなかった商品のファイルは、ハッシュが控えに載っていても「新しい」と数える（#10）。
        // 数えないと、取り直すまで商品にも未確定にも入っていないのに、監視からは片付いたように見える
        var unfetched = _store.ImportState.Load().UnfetchedItems
            .SelectMany(item => item.PathList)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newFiles = new List<string>();
        var seenFolders = new List<string>();

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
            {
                // 外付けを外している間は「増えていない」と見なす。
                // 消えたことにして監視から外すと、つなぎ直したときに戻す手が要る
                continue;
            }

            seenFolders.Add(folder);

            var result = _scanner.Scan(folder, cancellationToken);
            foreach (var file in result.Files)
            {
                // 外したパスでも、控えと大きさ・更新日時が合わなければ（落とし直した更新版）新しいと数える。
                // 外したのは中身で、場所ではない（ユーザ判断 2026-09-23）。数えないと起動時の取り込みが拾わない
                if (exclusions.IsExcludedWithoutHashing(file, cache) || registered.Contains(file.Path))
                {
                    continue;
                }

                if (unfetched.Contains(file.Path) || !cache.TryGetHash(file.Path, file.SizeBytes, file.ModifiedAtUtc, out _))
                {
                    newFiles.Add(file.Path);
                }
            }
        }

        return new WatchResult { Folders = seenFolders, NewFiles = newFiles };
    }

    /// <summary>商品に紐付けたフォルダ。その中はもう管理済みなので、増えたとは数えない。</summary>
    private async Task<RegisteredFolderSet> LoadRegisteredAsync(CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return new RegisteredFolderSet(
            loaded.Items.SelectMany(item => item.Local.LocalFolders).Select(folder => folder.Path));
    }
}

/// <summary>監視対象を見た結果。</summary>
public sealed class WatchResult
{
    /// <summary>実際に見られたフォルダ。今そこに無いもの（外付けを外している間など）は入らない。</summary>
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>まだ一度も見ていないファイル。</summary>
    public required IReadOnlyList<string> NewFiles { get; init; }

    public bool HasNew => NewFiles.Count > 0;
}
