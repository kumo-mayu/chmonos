using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 手元のファイル・フォルダの記録の操作（フォルダの外し・付け替え、ファイルの付け外し・除外と戻す、見つからない印、
/// バリエーションの対応、古い版を忘れる）と、行き先の決まった未確定の片付け。
///
/// ItemService から分けた（点検24・ユーザ判断 2026-10-08「クラス分けは進めてくれ」）。どれも商品の記録と未確定・除外の一覧だけを使い、
/// BOOTH へは問い合わせない（BOOTH から取ってくる登録は ItemService に残す）。外した／除外した／見つからない／取り外しているドライブの区別は、
/// 移す前と同じ式のまま。公開の口は ItemService に残して1行で渡す
/// </summary>
internal sealed class LocalFileEditor(DataStore store)
{
    /// <summary>
    /// フォルダの紐付けを解除する。ファイルには触らない。
    ///
    /// zipを後から手に入れたときに要る。zipを取り込むと展開先は自動で対象から外れるが、
    /// フォルダ登録は残るので、容量が二重に乗ったままになる。
    /// </summary>
    public async Task<bool> UnregisterFolderAsync(
        string itemId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        // 商品の錠の中で今の一覧から外す（2026-10-05・file-lifecycle.md「気になった所」3）。前は錠の外で読んだ写しを
        // 取り込みの持ち物（localFiles も入る）として書いていたので、読んでから書くまでに取り込みが足したファイルが消え得た
        var normalized = Path.TrimEndingDirectorySeparator(folderPath);
        return await store.Items.ChangeLocalAsync(
            itemId,
            current => WithoutFolder(current.LocalFolders, normalized) is { } remaining
                ? current with { LocalFolders = remaining }
                : null,
            [LocalField.LocalFolders],
            cancellationToken);
    }

    /// <summary>
    /// 見つからない登録フォルダの場所を、人が「この場所にする」で選んだ場所に差し替える（見つからない・移動の点検 10-A・ユーザ判断 2026-10-05）。
    /// </summary>
    /// <remarks>
    /// フォルダは場所が同一性なので、前は移すと「見つかりません」のままで、登録を外して登録し直すしかなかった（登録した日時も失う）。
    /// 候補は「見つからないファイルを探す」が名前・ファイル数・大きさで見せ、ここは人が選んだ場所を書くだけ。
    /// 測る（大きなフォルダでは数十秒）のは錠の外で、書くのは錠の中の今の値に当てる。その間に登録が外されていれば何も書かない。
    /// 登録したときと同じく、新しい場所の下の未確定は片付ける（取り込みはフォルダを移すと中身を未確定に出す）。
    /// </remarks>
    public async Task<FolderRelocation> RelocateFolderAsync(
        string itemId,
        string fromPath,
        string toPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(toPath))
        {
            return FolderRelocation.TargetMissing;
        }

        var from = Path.TrimEndingDirectorySeparator(fromPath);
        var to = Path.TrimEndingDirectorySeparator(toPath);

        // 2つの登録が同じ場所（とその中）を指すと、走査が飛ばす範囲と容量が二重になる。候補から外してあるが、選ぶまでの間に登録され得る
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var others = new RegisteredFolderSet(loaded.Items
            .Where(item => item.Id != itemId)
            .SelectMany(item => item.Local.LocalFolders)
            .Select(folder => folder.Path));
        if (others.Contains(to))
        {
            return FolderRelocation.RegisteredElsewhere;
        }

        // 裏のスレッドで数える（登録と同じ理由）
        if (await Task.Run(() => RegisteredFolderSet.Measure(to, cancellationToken), cancellationToken) is not (int count, long bytes))
        {
            return FolderRelocation.Unreadable;
        }

        var now = DateTimeOffset.Now;
        var written = await store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var record = current.LocalFolders.FirstOrDefault(folder => string.Equals(
                    Path.TrimEndingDirectorySeparator(folder.Path), from, StringComparison.OrdinalIgnoreCase));
                if (record is null)
                {
                    return null;
                }

                // 同じ商品が選んだ場所を既に登録していれば、1つにまとめる（同じ場所の登録が2つ並ばないように）
                return current with
                {
                    LocalFolders =
                    [
                        .. current.LocalFolders.Where(folder =>
                        {
                            var path = Path.TrimEndingDirectorySeparator(folder.Path);
                            return !string.Equals(path, from, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(path, to, StringComparison.OrdinalIgnoreCase);
                        }),
                        record with
                        {
                            Path = to,
                            FileCount = count,
                            TotalBytes = bytes,
                            LastSeenAt = now,
                            MissingSince = null,
                        },
                    ],
                };
            },
            [LocalField.LocalFolders],
            cancellationToken);

        if (!written)
        {
            return FolderRelocation.RecordGone;
        }

        await RemoveUnresolvedUnderAsync(to, cancellationToken);
        return FolderRelocation.Moved;
    }

    /// <summary>その場所の登録を除いた一覧。除く物が無ければ null。</summary>
    private static List<LocalFolderRecord>? WithoutFolder(IReadOnlyList<LocalFolderRecord> folders, string normalized)
    {
        var remaining = folders
            .Where(folder => !string.Equals(
                Path.TrimEndingDirectorySeparator(folder.Path), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return remaining.Count == folders.Count ? null : remaining;
    }

    /// <summary>
    /// 登録したフォルダの配下にあった未確定を取り除く。行き先が決まったため。
    ///
    /// 配下に1件も無ければ書かない。前は「在るかを見るために読む → 在れば錠の中でもう一度読んで書く」の2回読みで、
    /// 1回目は命令の頭で画面のスレッドを止めていた。錠の中で今の一覧を見て、外す物が無ければ書かずに抜ける（読むのは1回）
    /// </summary>
    internal Task RemoveUnresolvedUnderAsync(string folderPath, CancellationToken cancellationToken)
    {
        var registered = new RegisteredFolderSet([folderPath]);
        return store.Unresolved.TryUpdateAsync(
            current => current.RemoveAll(file => file.Paths.Any(registered.Contains)) > 0 ? current : null,
            cancellationToken);
    }

    /// <summary>
    /// 未確定の一覧から1件外す。
    ///
    /// **錠の中で今の一覧から外す**（技術的負債 1-2）。一覧は取り込みも書くので、始めに読んだ写しを書き戻すと、
    /// その間に取り込みが足した物が消える（逆に取り込みがこちらの変更を消すのは <see cref="UnresolvedMerge"/> で防ぐ）。
    /// 一覧に無ければ書かない（商品に戻す・zipで登録し直すでは、未確定に居ないのが普通。同じ中身を書き直すだけで、
    /// 未確定が数万件あると数十MBの書き出しになる）。
    /// </summary>
    internal Task RemoveUnresolvedAsync(string hash, CancellationToken cancellationToken)
        => store.Unresolved.TryUpdateAsync(
            current => current.RemoveAll(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0
                ? current
                : null,
            cancellationToken);

    /// <summary>
    /// 既にどこかのitemが持っているファイルを、未確定の一覧から取り除く。
    ///
    /// 確定は「itemを保存」→「未確定から削除」の2段階で、その間に落ちると
    /// 両方に存在する状態が残る。順序を逆にはできない（先に消してitemの保存に
    /// 失敗すると、ファイルの記録ごと失う方が明らかに悪い）。
    /// 重複は害が小さく後から均せるので、開くたびにここで均す。
    /// </summary>
    public async Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default)
    {
        // 「未確定」を開くたびに画面のスレッドから呼ばれる。記録は裏で読む（8万件で、開くたびに 150〜290ms 止まっていた）
        var unresolved = await store.Unresolved.LoadAsync(cancellationToken);
        if (unresolved.Count == 0)
        {
            return 0;
        }

        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var owned = loaded.Items
            .SelectMany(item => item.Local.AttachedFiles)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!unresolved.Any(file => owned.Contains(file.Hash)))
        {
            return 0;
        }

        // 商品を全部読む間に取り込みが一覧を書くことがあるので、錠の中で今の一覧から外す（技術的負債 1-2）
        var removed = 0;
        await store.Unresolved.UpdateAsync(
            current =>
            {
                removed = current.RemoveAll(file => owned.Contains(file.Hash));
                return current;
            },
            cancellationToken);
        return removed;
    }

    /// <summary>
    /// 展開フォルダで登録していた商品を、隣に現れたzipの方で登録し直す（ユーザ指示 2026-09-18）。
    ///
    /// フォルダ登録はzipが手元に無いときの受け皿で、zipが手に入ったら役目を終える。
    /// 以前は「フォルダの登録を外す」だけで、zipは人が取り込み直すしかなかった——
    /// **押した後に商品のファイルが1つも無くなる**ので、何が起きたのか分からなくなっていた。
    ///
    /// zipを1本だけハッシュして商品に付け、同時にフォルダの登録を外す。
    /// 取り込み全体を回さないのは、親フォルダに何百件あっても数えるだけで時間がかかるため。
    /// **ディスクのファイルには触らない。**
    /// </summary>
    public async Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(
        string itemId,
        string folderPath,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default)
    {
        var item = await store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ItemMissing, null);
        }

        var archivePath = Scanning.RegisteredFolderSet.FindArchiveFor(folderPath);
        if (archivePath is null)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ArchiveMissing, null);
        }

        var name = Path.GetFileName(archivePath);
        // 外した行は「付いている」に数えない（2026-10-05・file-lifecycle.md「気になった所」7）。前は外した zip を「登録済み」と読んで
        // フォルダの登録だけ外し、商品の手元の物が無くなっていた。外していた zip なら下へ進み、突き合わせで印を下ろして付け直す
        // （押した方が新しい判断。ユーザ判断 2026-10-05）
        var already = item.Local.OwnedFiles.Any(file => file.Paths.Any(
            path => string.Equals(path, archivePath, StringComparison.OrdinalIgnoreCase)));

        if (already)
        {
            // zipは既に付いている。あとはフォルダの登録を外すだけ
            await UnregisterFolderAsync(itemId, folderPath, cancellationToken);
            return new ArchiveSwapOutcome(ArchiveSwapResult.AlreadyRegistered, name);
        }

        if (await ReadHandFileAsync(archivePath, cancellationToken) is not { } record)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ArchiveUnreadable, name);
        }

        var hash = record.Hash;
        switch (await HandAttachBlockAsync(itemId, hash, liftExclusion, takeFromOtherItems, cancellationToken))
        {
            case { Excluded: true }:
                return new ArchiveSwapOutcome(ArchiveSwapResult.Excluded, name);
            case { Holders: { Count: > 0 } holders }:
                return new ArchiveSwapOutcome(ArchiveSwapResult.OwnedElsewhere, name) { Holders = holders };
        }

        var normalized = Path.TrimEndingDirectorySeparator(folderPath);

        // ファイルとフォルダを1回で、商品の錠の中で今の値に当てて書く。2回に分けると、間に人が触った入力が消える。
        // 上で読んだ写しは使わない（2026-10-05・file-lifecycle.md「気になった所」3）：zip のハッシュに数秒〜かかる間に
        // 取り込みが足したファイル・付けた種類・外す／戻す・見つからなくなった日時が、写しで書くと古い値に戻っていた。
        // 重いハッシュは錠の外で済ませ、錠の中では足し合わせるだけ
        var written = await store.Items.ChangeLocalAsync(
            itemId,
            current => current with
            {
                LocalFiles = Scanning.LocalFileMerger.MergeByHand(current.LocalFiles, [record]),
                LocalFolders = WithoutFolder(current.LocalFolders, normalized) ?? current.LocalFolders,
            },
            [LocalField.LocalFiles, LocalField.LocalFolders],
            cancellationToken);

        // ハッシュの間に商品が消されていたら、未確定からも外さない（行き先が無くなったので）
        if (!written)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ItemMissing, name);
        }

        // 未確定に同じzipが居たなら、行き先が決まったので外す（除外を解く・ほかの商品から外すも、こちらに付けた後で）
        await AfterHandAttachAsync(itemId, hash, liftExclusion, takeFromOtherItems, cancellationToken);
        await RemoveUnresolvedUnderAsync(normalized, cancellationToken);

        return new ArchiveSwapOutcome(ArchiveSwapResult.Registered, name);
    }

    /// <summary>
    /// 商品ページから、選んだ・落としたファイルをこの商品に結ぶ（ユーザ指示 2026-10-06）。
    ///
    /// 作者が前の商品を消して同じ物を新しいIDで出し直すと、ファイルの手掛かり（Zone.Identifier・ファイル名・zip の中の URL）は
    /// 古いIDを指したままなので、取り込みでは古い商品の方へ行くか未確定に出る。人が「これはこの商品の物」と言える道が要る。
    ///
    /// 決まりは「zipで登録し直す」（<see cref="SwapFolderForArchiveAsync"/>）と同じ：重いハッシュは錠の外、書くのは商品の錠の中で今の一覧へ
    /// <see cref="Scanning.LocalFileMerger.MergeByHand"/>（人の登録と同じ足し方。外していた物は印が下り、無い場所は外さない）。
    /// 除外・ほかの持ち主は人が決めたことなので、何も書かずに返して画面に聞かせ、頼まれたときだけ立てて呼び直させる。
    /// **ディスクのファイルには触らない。BOOTH へも行かない。**
    /// </summary>
    public async Task<FileAttachOutcome> AttachFileAsync(
        string itemId,
        string path,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(path);

        // 拡張子では絞らない（ユーザ判断 2026-10-07）。取り込みの拡張子の線（FolderScanner.TargetExtensions）は、フォルダごと落としたときに
        // 関係の無い物を拾わないためで、人が商品を指して足す物には要らない。走査に映らない拡張子でも、取り込みは場所ごとに在るかで見るので
        // 場所を外さない（LocalFileMerger）
        if (!store.Items.Exists(itemId))
        {
            return new FileAttachOutcome(FileAttachResult.ItemMissing, name);
        }

        if (!File.Exists(path))
        {
            return new FileAttachOutcome(FileAttachResult.FileMissing, name);
        }

        if (await ReadHandFileAsync(path, cancellationToken) is not { } record)
        {
            return new FileAttachOutcome(FileAttachResult.FileUnreadable, name);
        }

        switch (await HandAttachBlockAsync(itemId, record.Hash, liftExclusion, takeFromOtherItems, cancellationToken))
        {
            case { Excluded: true }:
                return new FileAttachOutcome(FileAttachResult.Excluded, name);
            case { Holders: { Count: > 0 } holders }:
                return new FileAttachOutcome(FileAttachResult.OwnedElsewhere, name) { Holders = holders };
        }

        // 在るかを見るのも書くのも錠の中の今の一覧で。この場所のまま外さずに持っていれば書かない（同じ物を2回選んだ・落とした）
        var already = false;
        var written = await store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                already = current.LocalFiles.Any(file => !file.Detached
                    && string.Equals(file.Hash, record.Hash, StringComparison.OrdinalIgnoreCase)
                    && file.Paths.Any(known => string.Equals(known, path, StringComparison.OrdinalIgnoreCase)));
                return already
                    ? null
                    : current with { LocalFiles = Scanning.LocalFileMerger.MergeByHand(current.LocalFiles, [record]) };
            },
            [LocalField.LocalFiles],
            cancellationToken);

        if (already)
        {
            return new FileAttachOutcome(FileAttachResult.AlreadyAttached, name);
        }

        // ハッシュの間に商品が消されていたら、未確定からも外さない（行き先が無くなったので）
        if (!written)
        {
            return new FileAttachOutcome(FileAttachResult.ItemMissing, name);
        }

        await AfterHandAttachAsync(itemId, record.Hash, liftExclusion, takeFromOtherItems, cancellationToken);
        return new FileAttachOutcome(FileAttachResult.Attached, name);
    }

    /// <summary>
    /// 人が選んだファイル1本を読み、商品のファイルの記録にする（錠の外で呼ぶ。大きいファイルはハッシュに数秒かかる）。
    /// 読めなければ null。中身の一覧は zip だけ読む（取り込みの <c>InspectFile</c> と同じ）。
    /// </summary>
    private static Task<LocalFileRecord?> ReadHandFileAsync(string path, CancellationToken cancellationToken)

        // 画面のスレッドから来る命令なので、大きさ・zip の目録の読み取りもスレッドの外で行う（外付け・ネットワークで待たされないように）
        => Task.Run<LocalFileRecord?>(async () =>
        {
            try
            {
                var size = new FileInfo(path).Length;
                var hash = await Scanning.FileHasher.ComputeSha256Async(path, cancellationToken);
                IReadOnlyList<string> contents = [];
                var broken = false;

                // 中のファイル名は、欠落復旧の照合と動作環境の推測に使う。読めなければ空で進む
                if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        contents = BoothZipInspector.ZipInspector.Inspect(path).Summary.Files
                            .Select(entry => entry.RelativePath)
                            .ToList();
                    }
                    catch (InvalidDataException)
                    {
                        // 形式が合わない＝壊れている。取り込みと同じ印を付ける（ほかのアプリが開いていた・権限が無いは、壊れているとは言えない）
                        broken = true;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                return new LocalFileRecord
                {
                    Hash = hash,
                    Paths = [path],
                    SizeBytes = size,
                    Contents = contents,
                    ArchiveBroken = broken,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }, cancellationToken);

    /// <summary>人の登録を止める事情（除外・ほかの持ち主）。頼まれた方は見ない。</summary>
    private readonly record struct HandAttachBlock(bool Excluded, IReadOnlyList<ArchiveHolder> Holders);

    /// <summary>
    /// 除外とほかの持ち主は、人が決めたこと。黙って上書きせず、何も書かずに返して画面に聞かせる（ユーザ判断 2026-10-05・
    /// file-lifecycle.md「気になった所」7）。前は見ずに付けていたので、除外した zip が除外のまま持ち物になり、
    /// ほかの商品が持つ zip は2つの商品の持ち物になって容量も二重に数えていた
    /// </summary>
    private async Task<HandAttachBlock> HandAttachBlockAsync(
        string itemId, string hash, bool liftExclusion, bool takeFromOtherItems, CancellationToken cancellationToken)
    {
        var excluded = await store.Excluded.LoadAsync(cancellationToken);
        if (!liftExclusion && excluded.Any(entry => string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)))
        {
            return new HandAttachBlock(true, []);
        }

        if (takeFromOtherItems)
        {
            return new HandAttachBlock(false, []);
        }

        return new HandAttachBlock(false, await OtherHoldersAsync(itemId, hash, cancellationToken));
    }

    /// <summary>
    /// こちらに付けた後の片付け：頼まれていれば除外を解き、ほかの商品から外し、未確定から消す。
    /// 除外を解くのも、ほかの商品から外すのも、こちらに付けられた後。逆にすると、付ける前に商品が消されていたとき
    /// 除外も持ち主も失ったファイルが残る。この順なら、途中で落ちても二重に持つだけで、どこからも消えない
    /// </summary>
    private async Task AfterHandAttachAsync(
        string itemId, string hash, bool liftExclusion, bool takeFromOtherItems, CancellationToken cancellationToken)
    {
        if (liftExclusion)
        {
            await store.Excluded.TryUpdateAsync(
                current => current.RemoveAll(entry => string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0
                    ? current
                    : null,
                cancellationToken);
        }

        if (takeFromOtherItems)
        {
            // 見てから書くまでに持ち主が変わっていることがあるので、それぞれの錠の中で今の一覧に印を付ける（DetachFileAsync と同じ形）。
            // 行は消さずに外した印にする：手掛かりが指せば次の取り込みでそちらへ戻ってしまうのを止め、商品ページで「この商品に戻す」もできる。
            // 手元の物が無くなっても商品は消さない（消すかは人が商品ページで決める）
            foreach (var holder in await OtherHoldersAsync(itemId, hash, cancellationToken))
            {
                await store.Items.ChangeLocalAsync(
                    holder.ItemId,
                    current => current.LocalFiles.Any(file =>
                            !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
                        ? current with
                        {
                            LocalFiles = [.. current.LocalFiles
                                .Select(file => !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
                                    ? file with { Detached = true }
                                    : file)],
                        }
                        : null,
                    LocalOwners.Import,
                    cancellationToken);
            }
        }

        await RemoveUnresolvedAsync(hash, cancellationToken);
    }

    /// <summary>
    /// この中身を持っている（外していない）ほかの商品。外した行は持ち物ではないので数えない（ReattachFileAsync と同じ見方）。
    /// 全件を読むのは人が押した1回だけで、取り込みの道では呼ばない。
    /// </summary>
    private async Task<IReadOnlyList<ArchiveHolder>> OtherHoldersAsync(
        string itemId, string hash, CancellationToken cancellationToken)
    {
        bool Holds(LocalFileRecord file) => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return loaded.Items
            .Where(other => other.Id != itemId && other.Local.AttachedFiles.Any(Holds))
            .Select(other => new ArchiveHolder(
                other.Id,
                other.DisplayName,
                !HoldsAnything(other.Local with
                {
                    LocalFiles = [.. other.Local.LocalFiles.Where(file => !Holds(file))],
                })))
            .ToList();
    }

    /// <summary>
    /// ファイルがどの種類のものかを付け直す（#40）。
    ///
    /// **商品の錠の中で、今の一覧の種類だけを書き換える。**LocalFiles は取り込みも書くので、
    /// 画面が開いた時点の写しを渡すと、その間に足されたファイルやパスが消える。
    /// 前は錠の外で読み直して一覧ごと書いていて、読んでから書くまでの間に取り込みが足したファイルが消えていた
    /// （2026-10-04 に試験で再現）。変え方を渡して錠の中で当てれば、その一瞬も無くなる。
    /// </summary>
    public async Task<bool> SetFileVariationsAsync(
        string itemId,
        IReadOnlyDictionary<string, long?> variationByHash,
        CancellationToken cancellationToken = default)
    {
        // ハッシュは大文字で持っているが、呼び出し側の表記に左右されないようにする
        var wanted = new Dictionary<string, long?>(variationByHash, StringComparer.OrdinalIgnoreCase);

        // 書かなかったのが「商品が無い」か「変える物が無い」かを分ける（後者は成功として返す）
        var found = false;
        var written = await store.Items.ChangeLocalAsync(
            itemId,
            local =>
            {
                found = true;
                var changed = false;
                var files = local.LocalFiles
                    .Select(file =>
                    {
                        if (!wanted.TryGetValue(file.Hash, out var variationId) || file.VariationId == variationId)
                        {
                            return file;
                        }

                        changed = true;
                        return file with { VariationId = variationId };
                    })
                    .ToList();

                return changed ? local with { LocalFiles = files } : null;
            },
            LocalOwners.FileVariations,
            cancellationToken);

        return written || found;
    }

    /// <summary>
    /// 使おうとして見た在る・無い（商品ページ・開く・Unityへ送る）を、ファイルの「見つからなくなった日時」に当てる（ユーザ判断 2026-10-04）。
    ///
    /// 前は商品ページだけがその場でディスクを見て「見つかりません」を出し、記録は書かなかったので、
    /// 同じ商品がカードの印・検索の条件・統計には出ず、画面どうしで食い違っていた。
    /// **書くのは日時だけ**で、場所・種類・メモなど人が入れた値には触れない。商品の錠の中で今の一覧に当て、
    /// 見たときと場所が変わったファイルには当てない（<see cref="FileMissingMarks.Apply"/>）。変わる物が無ければ書かない。
    /// </summary>
    public async Task<bool> NoteFilePresenceAsync(
        string itemId,
        IReadOnlyCollection<FileSighting> sightings,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return await store.Items.ChangeLocalAsync(
            itemId,
            local => FileMissingMarks.Apply(local.LocalFiles, sightings, now) is { } files
                ? local with { LocalFiles = files }
                : null,
            LocalOwners.FilePresence,
            cancellationToken);
    }

    /// <summary>
    /// 古い版の記録を片付ける（商品ページの「古い版の記録を片付ける」・2026-10-05・点検の8）。
    ///
    /// **外した印（<see cref="LocalFileRecord.Detached"/>）ではなく、行ごと消す。**外した印は「手掛かりが同じ商品へ戻すのを止める」ための物で、
    /// 古い版はどこにも無いので止める相手がいない。印にすると灰色の行が残り、片付けたことにならない。
    /// 古い版をまたどこかに置けば、次の取り込みで手掛かりから戻り得る（設定の「外した記録を消す」と同じ）。
    /// 錠の中の今の値で、まだ古い版のときだけ消す（見てから押すまでに、取り込みが古い版を別の所で見つけて場所を足していれば消さない）。
    /// </summary>
    public async Task<bool> ForgetOldVersionAsync(string itemId, string hash, CancellationToken cancellationToken = default)
        => await store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var files = current.LocalFiles
                    .Where(file => !(file.IsOldVersion && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                return files.Count == current.LocalFiles.Count ? null : current with { LocalFiles = files };
            },
            [LocalField.LocalFiles],
            cancellationToken);

    /// <summary>
    /// ファイルをこの商品から外し、未確定へ戻す。
    ///
    /// **IDは書き換えない。**商品IDはファイル名にもフォルダ名にもなっていて、
    /// 他の商品からも名前で参照されているので、書き換えると参照が全部迷子になる。
    /// やりたいことは「このファイルの行き先が違う」なので、ファイルの側を動かす。
    ///
    /// **行は消さずに外した印（<see cref="LocalFileRecord.Detached"/>）を付ける**（ユーザ判断 2026-09-12）。
    /// 手掛かりでこの商品に紐付いていたこと自体は確かなので、消すと何を外したのかが見えなくなる。
    /// 印は、手掛かりから商品IDが決まるファイルが**次の取り込みで同じ商品へ戻ってしまう**のも止める
    /// （外す操作が要るのはまさに手掛かりが間違っている場合）。以前は別の detached.json に持っていた。
    /// </summary>
    public async Task<DetachOutcome> DetachFileAsync(
        string itemId,
        string hash,
        bool deleteItemWhenEmpty,
        CancellationToken cancellationToken = default)
    {
        var item = await store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return DetachOutcome.Missing;
        }

        var target = item.Local.LocalFiles.FirstOrDefault(file =>
            !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return DetachOutcome.Missing;
        }

        // 実体が残っているものだけ未確定へ戻す。
        // 既に消えているファイルを並べても、紐付け直す相手がいない
        var alive = target.Paths.Where(File.Exists).ToList();
        if (alive.Count > 0)
        {
            var modified = DateTimeOffset.Now;
            try
            {
                modified = new DateTimeOffset(File.GetLastWriteTimeUtc(alive[0]), TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 日時が読めなくても未確定には出したいので、今の時刻で通す
            }

            // ダウンロード元の記録（Zone.Identifier）は、未確定の記録を作るここで読む（ユーザ判断 2026-09-30）。
            // 商品の記録は持っておらず、未確定の画面は開くたびには読み直さない（UnresolvedOrigin）。
            // 入れないと、展開した中身は次に取り込み直すまで元zipの束に入らず、フォルダで束ねられる。
            // 人の操作1回につき1ファイルで、読めなければ無いものとして返る
            var zone = BoothZipInspector.ZoneIdentifierReader.Read(alive[0]);

            var entry = new UnresolvedFile
            {
                Hash = target.Hash,
                Paths = alive,
                SizeBytes = target.SizeBytes,
                ModifiedAtUtc = modified,
                FirstSeenAt = DateTimeOffset.Now,
                Contents = target.Contents,
                ZoneHostUrl = zone.HostUrl,
                ZoneReferrerUrl = zone.ReferrerUrl,

                // 開けなかった印は商品の記録から引き継ぐ。未確定の画面は開き直して確かめないので、
                // ここで落とすと次の取り込みまで普通の未確定に見える
                ArchiveBroken = target.ArchiveBroken,
            };

            // 錠の中で今の一覧に足す（技術的負債 1-2）
            await store.Unresolved.UpdateAsync(
                current =>
                {
                    if (!current.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        current.Add(entry);
                    }

                    return current;
                },
                cancellationToken);
        }

        // 在るかを見るのは落ちたネットワークドライブなら数秒かかり、未確定の錠も待つ。
        // その間に取り込みが同じ商品へファイルやフォルダを足すことがあるので、
        // 外す印は書く直前の今の一覧に付け、空になったかも今の値で見る
        var becameEmpty = false;
        var written = await store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                if (!current.LocalFiles.Any(file =>
                        !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                var files = current.LocalFiles
                    .Select(file => !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
                        ? file with { Detached = true }
                        : file)
                    .ToList();

                becameEmpty = !HoldsAnything(current with { LocalFiles = files });
                return current with { LocalFiles = files };
            },
            LocalOwners.Import,
            cancellationToken);

        if (!written)
        {
            return DetachOutcome.Missing;
        }

        // 商品ごと消すと、外した印も一緒に消える（次の取り込みで手掛かりが指せば、また作られる）。
        // **空かは消す錠の中でもう一度見る。**印を書いてから消すまでの間に取り込みがファイルを足すと、
        // 前は足された分ごと消していた。足されていれば消さずに、外しただけとして返す
        if (becameEmpty && deleteItemWhenEmpty)
        {
            return await store.Items.DeleteIfAsync(itemId, item => !HoldsAnything(item.Local), cancellationToken)
                ? DetachOutcome.ItemDeleted
                : DetachOutcome.Detached;
        }

        return becameEmpty ? DetachOutcome.ItemNowEmpty : DetachOutcome.Detached;
    }

    /// <summary>
    /// 手元に何か持っているか（所持の答え <see cref="LocalBlock.IsOwned"/>。外したファイル・上書きで残った古い版は数えない。フォルダ登録は数える）。
    /// 古い版だけが残る外し方も「空になった」として、画面が最後のファイルのときと同じく残し方を聞けるようにする（⑤-B）。
    /// </summary>
    private static bool HoldsAnything(LocalBlock local) => local.IsOwned;

    /// <summary>
    /// 外したファイルをこの商品に戻す（商品ページの灰色の行の「この商品に戻す」・ユーザ判断 2026-09-12）。
    /// 未確定からは取り除く。**外した後で別の商品へ紐付け直していたら戻さない**——同じファイルが
    /// 2つの商品の持ち物になり、容量も二重に数える。
    /// </summary>
    public async Task<ReattachOutcome> ReattachFileAsync(
        string itemId,
        string hash,
        CancellationToken cancellationToken = default)
    {
        var item = await store.Items.LoadAsync(itemId, cancellationToken);
        var target = item?.Local.LocalFiles.FirstOrDefault(file =>
            file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (item is null || target is null)
        {
            return ReattachOutcome.Missing;
        }

        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        if (loaded.Items.Any(other => other.Id != itemId
                && other.Local.AttachedFiles.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))))
        {
            return ReattachOutcome.OwnedElsewhere;
        }

        // 全件を読んで確かめる間に取り込みが一覧を書き換えることがあるので、書く直前の今の一覧で戻す
        var written = await store.Items.ChangeLocalAsync(
            itemId,
            current => current.LocalFiles.Any(file =>
                    file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
                ? current with
                {
                    LocalFiles = [.. current.LocalFiles
                        .Select(file => file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
                            ? file with { Detached = false }
                            : file)],
                }
                : null,
            LocalOwners.Import,
            cancellationToken);

        if (!written)
        {
            return ReattachOutcome.Missing;
        }

        await RemoveUnresolvedAsync(hash, cancellationToken);

        return ReattachOutcome.Reattached;
    }

    /// <summary>
    /// ファイルを管理対象から外す。未確定一覧からも取り除く。1個でも数千個でも1回で書く。
    ///
    /// 前は1個ごとに2つの記録を丸ごと読み書きしていて、フォルダごと外すと 500 個で約 16 秒・5,000 個で約 2分34秒かかった
    /// （件数の2乗で伸び、1個ごとにディスクへ書き切る分も重なる。`docs/research/large-files-2026-09-30.md`「除外の記録を測った」）。
    /// どちらの記録も錠の中で今の値に当てるので、読んでから書くまでの間に取り込みが書いた分は消えない。
    ///
    /// **除外の記録に足してから、未確定から外す。**逆にすると、未確定から消えた後で除外に書けなかったとき、
    /// ファイルがどちらの記録にも無くなる（次の取り込みまで見えない）。この順なら、途中で落ちても未確定に残るだけで、もう一度押せば済む。
    /// </summary>
    public async Task<IReadOnlyList<string>> ExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return [];
        }

        // 足した物を覚えて返す。戻すときに前からの除外まで消さないため（2026-10-05・file-lifecycle.md「気になった所」18）
        List<string> added = [];
        await store.Excluded.TryUpdateAsync(
            excluded =>
            {
                // 既に在る中身は足さない（外したときの日時と理由は、先に外したときの物を残す）。同じ一覧の中の重なりも1件にする
                var known = excluded.Select(entry => entry.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var now = DateTimeOffset.Now;

                // 錠の中の変え方は書き込みに失敗すると呼び直されることがあるので、毎回空から数える
                added = [];
                foreach (var file in files)
                {
                    if (known.Add(file.Hash))
                    {
                        excluded.Add(new ExcludedEntry
                        {
                            Hash = file.Hash,
                            Paths = file.Paths,
                            ExcludedAt = now,
                            Reason = reason,
                        });
                        added.Add(file.Hash);
                    }
                }

                return added.Count > 0 ? excluded : null;
            },
            cancellationToken);

        var hashes = files.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await store.Unresolved.TryUpdateAsync(
            current => current.RemoveAll(file => hashes.Contains(file.Hash)) > 0 ? current : null,
            cancellationToken);

        return added;
    }

    /// <summary>
    /// 未確定の画面で外した直後に戻す（ユーザ判断 2026-09-17：戻す場所が設定の「隠したもの」だけだった）。
    /// 設定の「解除」はファイルから未確定の記録を作り直す（候補は控えの手掛かりだけ）。ここでは外す前の未確定の記録（候補・元zipの記録を含む）をそのまま戻す。
    /// </summary>
    public async Task UndoExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        IReadOnlyCollection<string> excludedHashes,
        CancellationToken cancellationToken = default)
    {
        // 消すのは今回足した除外だけ（2026-10-05・file-lifecycle.md「気になった所」18）。除外は既にあるハッシュを足さないので、
        // 一覧のハッシュで全部消すと、前から除外していた物の記録（日時・理由）まで消えていた
        var hashes = excludedHashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await store.Excluded.TryUpdateAsync(
            excluded => excluded.RemoveAll(entry => hashes.Contains(entry.Hash)) > 0 ? excluded : null,
            cancellationToken);

        await store.Unresolved.UpdateAsync(
            current =>
            {
                foreach (var file in files)
                {
                    if (!current.Any(entry => string.Equals(entry.Hash, file.Hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        current.Add(file);
                    }
                }

                return current;
            },
            cancellationToken);
    }
}
