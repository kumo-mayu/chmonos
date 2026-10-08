using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using BoothZipInspector;

namespace Chmonos.Core.Scanning;

// 取り込みの梯子の取得（①②③④⑤⑥）：BOOTH から商品を取り、画像の待ち列を流す。途中の記録と、次に取り直す日も決める。
//
// ImportPipeline（約2,200行）を段ごとのファイルに分けた（点検24・ユーザ判断 2026-10-08）。クラスの説明は ImportPipeline.cs にある
public sealed partial class ImportPipeline
{
    /// <summary>
    /// 梯子を段ごとに降りる。**商品ごとに全部取るのではなく、段ごとに全商品を回る。**
    ///
    /// 理由は2つ。
    ///
    /// ひとつは**待ち時間を作業時間に変える**こと。実データでは1商品あたり
    /// JSON 1本・HTML 1本・画像 9.5本で、画像が全体の76%を占める。
    /// 商品ごとに取ると100商品で31分かかり、その間ずっと何も見えない。
    /// ①②だけなら5分で、そこには検索・絞り込み・統計に要るものが全部揃っている。
    ///
    /// もうひとつは**部分的な知識で作業を始めさせない**こと。対応アバターを選ぶとき、
    /// 候補の材料にはvariationの名前が入る。100商品のうち1商品しか知らない状態で
    /// 選ばせると候補が出揃わず、後から選び直すことになる。
    /// </summary>
    private async Task<FetchResult> FetchAsync(
        Dictionary<string, List<LocalFileRecord>> filesByItemId,
        ImportWorkSet work,
        ImportTotals totals,
        FilePresenceProbe probe,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var alreadyKnown = 0;
        var notFound = 0;
        var notFoundFiles = new List<UnresolvedFile>();
        var temporaryFailures = 0;
        var avatarItemsUpdated = 0;
        var avatarsFound = 0;
        string? avatarDetectError = null;
        var avatarDetectRan = false;

        // 商品に書いた壊れた zip を結果の数に入れる。BOOTH に無くて未確定へ戻した物・取れなかった物は、商品に入っていないので数えない
        void NoteBroken(string itemId, IEnumerable<LocalFileRecord> files)
        {
            foreach (var file in files.Where(file => file.ArchiveBroken))
            {
                totals.NoteBrokenOnItem(itemId, file.Hash);
            }
        }

        // 手元にある商品は通信が要らない。ファイルを足すだけなので、段に入る前に片付ける
        var pending = new List<(string ItemId, List<LocalFileRecord> Files)>();

        // 取得済みなのに説明が無い商品。①の後・②の前で閉じた取り込みの続き（U9）
        var withoutPage = new List<ItemRecord>();

        // 無い場所を外すかは見回りと同じ部品で見る（根はドライブごとに1回・3秒で打ち切り。点検の12）。
        // 根の覚えはこの段の間だけ（周回の頭からここまでに外付けを外していたら、頭の「つながっている」は古い）
        var presence = probe.Renewed();

        foreach (var (itemId, discovered) in filesByItemId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                pending.Add((itemId, discovered));
                continue;
            }

            // 取得済みのitemは触らない。中断して再実行した時に、ここが「続きから」を成立させる。
            //
            // 足すのは商品の錠の中で今の一覧に（2026-10-05・file-lifecycle.md「気になった所」3）。上で読んだ写しに足して
            // 書くと、読んでから書くまでの間に人が付けた種類・外す／戻す・見つからなくなった日時が古い値に戻る
            await _store.Items.ChangeLocalAsync(
                itemId,
                current => NotDetachedIn(current, discovered) is { Count: > 0 } files
                    ? current with { LocalFiles = LocalFileMerger.Merge(current.LocalFiles, files, presence) }
                    : null,
                [LocalField.LocalFiles],
                cancellationToken);

            alreadyKnown++;
            NoteBroken(itemId, discovered);

            // 前に取れなかった商品でも、今は商品があるならファイルはここで足された
            totals.NoteSettled(itemId);

            // 販売終了の商品はページも無いので戻さない（取りに行っても毎回失敗するだけ）
            if (!existing.Local.IsDelisted && !File.Exists(_store.Paths.ItemHtmlFile(itemId)))
            {
                withoutPage.Add(existing);
            }
        }

        // 残り時間の見込み（U1）。①は新しい商品の数、②はそれに説明の無い取得済みを足した数
        work.PlanRequests(json: pending.Count, pages: pending.Count + withoutPage.Count);

        // 中断の記録は周回をまたいで足し合わせる（C12）
        totals.PlanFetch(pending.Count);

        // 走査が済んだことをここで書く。①で取る物が無い周回は①の中で書かないので、
        // 書かないと②③や画像の間に閉じた回に「途中で中断した」と嘘をつく
        await SaveProgressAsync(totals, work, cancellationToken);

        // ── ① 商品JSON（全商品）。ここが終われば検索も統計も成立する ──
        //
        // 段ごとに優先度を切り替える。人が押した操作はこれより上なので、
        // 取り込みの最中でも「このIDで確認」は待たされない
        using var metadataPriority = BoothClient.Prioritize(BoothPriority.Metadata);

        var fetched = new List<ItemRecord>();
        var shopIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        // 「追加した」の足跡は溜めて、一定件数ごとと①の終わりに書く（1件ずつ書くと recent.json の読み書きが件数の2乗になる。
        // 釣り合いの根拠は RecentStampBuffer.FlushEvery）。足跡を打つのは①だけなので、①を抜けるところが取り込みの区切り
        var addedStamps = new Services.RecentStampBuffer(_store.Recent);
        try
        {
            foreach (var (itemId, discovered) in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, ImportPhase.FetchingJson, ++done, pending.Count, itemId);

                // 応答の無い失敗が続いたら、残りは問い合わせずに「続きから」へ残す（ユーザ判断 2026-09-29）。
                // 取れなかった商品と同じ扱いなので、下の帯の「続きから進む」で取り直せる
                if (totals.Outage.IsStopped)
                {
                    temporaryFailures++;
                    work.PlanRequests(json: -1, pages: -1);
                    totals.NoteUnfetched(itemId, discovered);
                    continue;
                }

                var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
                work.PlanRequests(json: -1);
                totals.Outage.Note(jsonResult);

                if (jsonResult.Status == BoothFetchStatus.NotFound)
                {
                    // BOOTHに無い＝ファイルが無かったことにはならない。
                    // 買っていて手元にあるものなので、未確定へ戻して人に決めてもらう。
                    // 別のIDで再公開されていることもあり、そのときは候補検索が拾える。
                    //
                    // 1回の404で流すのは、**こちらが「非公開だ」と確定する必要がないから。**
                    // 一時的な障害だったなら次の取り込みで普通に確定するだけで、何も失われない
                    notFound++;
                    work.PlanRequests(pages: -1); // ②へは進まない
                    notFoundFiles.AddRange(discovered.Select(file => ToUnresolved(file, itemId)));
                    totals.NoteSettled(itemId);
                    continue;
                }

                // 一時失敗（タイムアウト・5xx・接続失敗）と、200 でも読めない応答（JSON でない・型が変わった）。
                // 後者を投げると、1件のために取り込み全体が止まっていた。
                // どちらも「続きから」に残して取り直せるようにする（#10）。その場で書くのは、この後に閉じても残すため
                if (!jsonResult.IsSuccess || jsonResult.Value is null
                    || BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, itemId: itemId) is not { } booth)
                {
                    temporaryFailures++;
                    work.PlanRequests(pages: -1); // ②へは進まない
                    totals.NoteUnfetched(itemId, discovered);
                    await SaveProgressAsync(totals, work, cancellationToken);
                    continue;
                }

                var item = new ItemRecord
                {
                    Id = itemId,
                    Booth = booth,
                    Local = new LocalBlock
                    {
                        // 見方を渡すのは、見つけた場所にディスクの通し番号を書くため（点検の3）
                        LocalFiles = LocalFileMerger.Merge([], discovered, presence),
                        NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                        LastFetchedAt = DateTimeOffset.Now,
                        NextFetchDueAt = NextFetchDue(itemId),
                    },
                };

                // 1件ずつ保存する。ここで中断しても、取れたぶんはそのまま残る。
                //
                // **「新しい」と判断した時点と書く時点がずれている**（ユーザ判断 2026-09-21・L13）。
                // BOOTH から取る数秒〜数分の間に、未確定の「このIDで登録」が同じ商品を作ることがあり、
                // 丸ごと書くと人が入れた名前・購入記録が消えていた。
                // **在るかを商品の錠の中で見て**、あれば取ってきた `booth` と見つけたファイルだけを今の値に重ねる。
                // 前は錠の外で読み直し、無ければ丸ごと保存していたので、見てから書くまでの間に人の保存が同じ商品を作ると、
                // 人が入れた名前・メモ・購入記録を消していた（2026-10-02。CreateWhileSomeoneSavesTests）
                var created = false;
                await _store.Items.CreateOrChangeLocalAsync(
                    itemId,
                    () =>
                    {
                        created = true;
                        return item;
                    },
                    current => created || NotDetachedIn(current, discovered) is not { Count: > 0 } files
                        ? current
                        : current with { LocalFiles = LocalFileMerger.Merge(current.LocalFiles, files, probe.Renewed()) },
                    LocalOwners.Import,
                    cancellationToken,
                    item.Booth);

                // 「追加」の足跡。**itemのJSONには書かない**（足跡で埋めないため）。
                // 既にある商品には打てないので、そちらは「不明」のまま残る——
                // 後から作った時刻を騙るより、無いと言う方がよい。
                // 時刻は保存した今のもの（書くのは後でも、1件ずつ書いていたときと同じ時刻が残る）
                await addedStamps.AddAsync(itemId, Services.RecentKind.Added, DateTimeOffset.Now);

                fetched.Add(item);
                added++;
                NoteBroken(itemId, discovered);

                // 検索と件数にはもう出してよい。編集は③が済むまで待たせる（U8・U10）
                work.NoteAdded(itemId);

                // どこまで進んだかを残す（理由は SaveProgressAsync）
                totals.NoteFetched();
                totals.NoteSettled(itemId);
                await SaveProgressAsync(totals, work, cancellationToken);

                // アイコンのURLは商品JSONにしか入っていないので、ここで控えて⑥で取りに行く
                if (item.Booth.Shop is { ThumbnailUrl.Length: > 0 } shop)
                {
                    shopIcons[shop.Subdomain] = shop.ThumbnailUrl;
                }
            }
        }
        finally
        {
            // 中止・例外で抜けるときも溜めた分を書く（取れて保存した商品の「追加した」を捨てない）
            await addedStamps.FlushAsync();
        }

        // ── ② 商品ページHTML（全商品）。対応アバターの節と説明文 ──
        //
        // 取得済みでも説明が無い商品はここへ戻す。②③の途中で閉じてから押し直すと、
        // ①が済んだ商品は「取得済み」で飛ばされ、説明が⑦の取り直しの日まで埋まらなかった（U9）
        var pages = fetched.Concat(withoutPage).ToList();
        done = 0;

        for (var index = 0; index < pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ①と同じく、応答の無い失敗が続いたら残りは問い合わせない。説明の無い商品は、
            // 「続きから進む」で対象を走査し直したときに②へ戻る（取得済みで説明が無い商品は②の対象・U9）
            if (totals.Outage.IsStopped)
            {
                work.PlanRequests(pages: -(pages.Count - index));
                break;
            }

            var item = pages[index];
            Report(progress, ImportPhase.FetchingHtml, ++done, pages.Count, item.Id);

            var htmlResult = await _client.GetItemHtmlAsync(item.Id, cancellationToken);

            // 見込みは問い合わせが済んでから減らす（①と揃える）。取っている最中の1件は残りに数える
            work.PlanRequests(pages: -1);
            totals.Outage.Note(htmlResult);
            if (!htmlResult.IsSuccess || htmlResult.Value is null)
            {
                // 節が取れなくても商品自体は使える。次の段へ進む
                continue;
            }

            var extraction = H2SectionExtractor.Extract(htmlResult.Value);

            // 節は、書く直前に読み直した今の booth に足す。①で取った写しに足して丸ごと書くと、
            // ①と②の間（数分になることもある）に人が「商品情報を取り直す」を押したとき、
            // 取り直した新しい booth が①の古い物に戻っていた。
            // 今の方が新しく取れていれば、節もそちらが同じ時に取った物なので触らない
            var fetchedAt = item.Booth.FetchedAt;
            BoothBlock? written = null;
            await _store.Items.ChangeBoothAsync(
                item.Id,
                current => current.FetchedAt > fetchedAt
                    ? null
                    : written = current with { H2Sections = extraction.Sections },
                cancellationToken);

            if (written is not null)
            {
                pages[index] = item = item with { Booth = written };
            }
            else if (await _store.Items.LoadAsync(item.Id, cancellationToken) is { } newer)
            {
                // 後の③と画像の列は、取り直した新しい画像の一覧で進める
                pages[index] = item = newer;
                continue;
            }
            else
            {
                continue;
            }

            // 説明が無い商品でも空のファイルを置く。置かないと「まだ取っていない」と見分けが付かず、
            // 取り込むたびに取り直しに来る
            await _store.Items.SaveDescriptionHtmlAsync(item.Id, extraction.DescriptionHtml ?? string.Empty, cancellationToken);
        }

        // 画像の問い合わせの見込みは、**②が終わった時点で**数えておく（手元にある絵は取りに行かないので多めに出る）。
        // 以前は周回の最後（③の後）に足していたので、①②③の間ずっと画像の残りが 0 のままで、
        // 「画像を取り終わるまで」が「編集できるまで」と同じ数字になっていた（ユーザ指摘 2026-09-21）。
        // 画像の枚数は②まで済めば分かるので、そこで数える
        //
        // 応答の無い失敗が続いて打ち切ったなら、この回は画像も取りに行かない（取れない物を1件ずつ再試行で待つだけになる）。
        // 取り残した画像は起動時の⑤か「足りない情報を取得」で取れる（ImageBacklog は手元の JSON とディスクの差で対象を決める）。
        // 打ち切った記録は③の間に閉じても残るよう、ここで書いておく
        var takesImages = _images.SavesImages && !totals.Outage.IsStopped;
        if (totals.Outage.IsStopped)
        {
            await SaveProgressAsync(totals, work, cancellationToken);
        }

        if (takesImages)
        {
            var planned = pages.Where(item => item.Booth.Images.Count > 0).ToList();
            work.PlanRequests(
                thumbnails: planned.Count,
                gallery: planned.Sum(item => item.Booth.Images.Count) - planned.Count,
                icons: shopIcons.Count);
        }

        // ── ③ 対応アバターの検出 ──
        //
        // 画像より先に置く。対応アバターを選ぶのは人の作業で、その候補が出揃っている
        // ことの方が、絵が見えていることより先に要る。
        // 通信が要るのは「対応表明で名前が出たが、手元に持っていないアバター」だけなので、
        // ここを④の前に置いても待ちはほとんど伸びない。
        if (_avatars is not null && pages.Count > 0)
        {
            using var detectPriority = BoothClient.Prioritize(BoothPriority.Detection);
            avatarDetectRan = true;

            try
            {
                var detected = await _avatars.DetectAsync(new DetectProgressAdapter(progress), cancellationToken);
                avatarItemsUpdated = detected.ItemsUpdated;
                avatarsFound = detected.AvatarsFound;
                // 問い合わせを打ち切った回は、止まった理由として言う（つながっていない／BOOTHが不調）
                avatarDetectError = Services.FailureText.Outage(detected.Outage);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 取り込みは成立している。検出はアバター画面からやり直せるので、止めずに知らせるだけ
                Diagnostics.AppLog.Error("取り込みの後の対応アバターの検出", exception);
                avatarDetectError = Services.FailureText.Cause(exception);
            }
        }

        // ③の段は終わった（失敗しても、検出を使わない設定でも）。この周回の商品を編集に出す
        work.NoteDetectionDone(fetched.Select(item => item.Id));

        // ④1枚目 ⑤残りの画像 ⑥ショップのアイコン は、周回の外の画像の列で取る（U5・DrainImagesAsync）。
        // 周回の中で取り切ると、その間に積まれたフォルダは⑥が終わるまで何も始まらない。
        // 画像を取らない設定なら何も積まない（梯子は①②③で終わり。検索・絞り込み・統計は JSON だけで成立する）
        //
        // **並びは検索・編集の待ち行列と同じ規則にそろえる**（ItemOrder.ByAcquired。2026-09-21 ユーザ判断）。
        // 走査した順のままだと、絵が埋まっていく順と、人が上から片付けていく順が無関係になり、
        // 待ち行列の先頭の商品の絵だけがいつまでも来ない、という見え方になっていた
        var withImages = takesImages
            ? ItemOrder.ByAcquired(pages.Where(item => item.Booth.Images.Count > 0), descending: true).ToList()
            : [];

        return new FetchResult
        {
            Added = added,
            AlreadyKnown = alreadyKnown,
            NotFound = notFound,
            NotFoundFiles = notFoundFiles,
            TemporaryFailures = temporaryFailures,
            WithImages = withImages,
            ShopIcons = takesImages ? shopIcons : new Dictionary<string, string>(),
            AvatarItemsUpdated = avatarItemsUpdated,
            AvatarsFound = avatarsFound,
            AvatarDetectError = avatarDetectError,
            AvatarDetectRan = avatarDetectRan,
        };
    }

    /// <summary>
    /// どこまで進んだかを残す。閉じた時に何件残っていたかをユーザは覚えていない。
    /// ①の途中で閉じると「IDは分かったがまだ取得していない商品」の一覧は消えるので、
    /// 件数だけでも残しておかないと、中断したこと自体が黙って起きる。
    /// BOOTH の不調で取れなかった商品も一緒に書く（#10）
    /// </summary>
    /// <param name="scanning">走査・ID の特定に入るところか（<see cref="ImportState.Scanning"/>）。</param>
    private Task SaveProgressAsync(
        ImportTotals totals, ImportWorkSet work, CancellationToken cancellationToken, bool scanning = false)
        => _store.ImportState.SaveAsync(
            new ImportState
            {
                Done = totals.FetchedTotal,
                Total = totals.PendingTotal,
                StoppedAt = DateTimeOffset.Now,
                Targets = work.Accepted,
                Unfetched = totals.Unfetched,
                Stopped = totals.Outage.Stopped,
                Scanning = scanning,
            },
            cancellationToken);

    /// <summary>
    /// 次回の取得予定。全itemが同じ日に期限切れにならないよう、商品IDから決まるばらつきを足す。
    /// 乱数ではなくIDから決めているのは、同じitemなら何度計算しても（起動し直しても）同じ日になるようにするため（<see cref="RefreshJitter"/>）。
    /// </summary>
    private DateTimeOffset NextFetchDue(string itemId)
        => DateTimeOffset.Now.AddDays(_settings.RefreshIntervalDays + RefreshJitter.Days(itemId, _settings.RefreshJitterDays));

    /// <summary>
    /// 商品へ紐付けたファイルを、未確定のファイルへ戻す。
    ///
    /// 手掛かりから決まった商品IDは<b>候補として載せる</b>。
    /// 「このファイルは 1234567 を指しているが、BOOTHには無い」と読める形にするため。
    /// </summary>
    private static UnresolvedFile ToUnresolved(LocalFileRecord file, string itemId)
    {
        var modified = DateTimeOffset.Now;
        var path = file.Paths.FirstOrDefault();
        string? hostUrl = null;
        string? referrerUrl = null;

        if (path is not null)
        {
            try
            {
                modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 日時が読めなくても未確定には出したいので、今の時刻で通す
            }

            // ふつうの未確定と同じくダウンロード元を読み直す（file-lifecycle.md 気になった所9）。
            // 商品の記録は Zone の欄を持たないので、前はここで落ち、毎回この道を通るので取り込み直しても付かず、
            // 展開した中身が元zip の束（ZoneReferrerUrl）に入らなかった。商品から外して戻すとき（DetachFile）も読み直している
            var zone = ZoneIdentifierReader.Read(path);
            hostUrl = zone.HostUrl;
            referrerUrl = zone.ReferrerUrl;
        }

        return new UnresolvedFile
        {
            Hash = file.Hash,
            Paths = file.Paths,
            SizeBytes = file.SizeBytes,
            ModifiedAtUtc = modified,
            FirstSeenAt = DateTimeOffset.Now,
            Contents = file.Contents,
            ZoneHostUrl = hostUrl,
            ZoneReferrerUrl = referrerUrl,
            CandidateItemIds = [itemId],

            // 開けなかった印は記録ごと引き継ぐ（前は商品の記録が印を持たず、戻すときに付け直していた）
            ArchiveBroken = file.ArchiveBroken,

            // BOOTH の答えを残す（ユーザ判断 2026-10-06）。未確定の画面が、選んだときからそのIDのまま登録する形を出せるように
            NotOnBooth = new BoothNotFoundNote { ItemId = itemId, CheckedAt = DateTimeOffset.Now },
        };
    }

    /// <summary>
    /// 画像の列を1件ずつ取る（U5）。④1枚目 → ⑤残り → ⑥ショップのアイコン の順。
    ///
    /// 1件ごとに <paramref name="work"/> を見て、積まれたフォルダがあればその場で戻る。
    /// 残りの問い合わせの見込み（U1）もここで減らす。
    /// 周回の中で取り切っていた頃は、積んだ分は⑥が終わるまで何も始まらなかった
    /// （設計では「積まれたら①②が最優先」と決めてあったのに、実装が合っていなかった）。
    /// 優先度は段ごとに切り替える。人が押した通信はどの段よりも上なので待たされない。
    /// </summary>
    /// <returns>落とせた画像の枚数。</returns>
    private async Task<int> DrainImagesAsync(
        ImageQueue queue,
        ImportWorkSet work,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloaded = 0;

        // 画像の段でも、届かない失敗が3件続いたらこの回の残りは取りに行かない（ユーザ判断 2026-09-29）。
        // 取らなかった絵は印も置かないので、起動時の⑤か「足りない情報を取得」で取れる（ImageBacklog は手元の JSON とディスクの差で対象を決める）
        while (!queue.IsEmpty && !work.HasPending && !queue.Outage.IsStopped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ④ 1枚目。一覧のカードは1枚目しか使わない（マウスを乗せて初めてギャラリーを組む）ので、1枚あれば一覧は完成する
            if (queue.Thumbnails.First is { } next)
            {
                queue.Thumbnails.RemoveFirst();
                var item = next.Value;
                Report(progress, ImportPhase.FetchingThumbnails, ++queue.ThumbnailsDone, queue.ThumbnailsTotal, item.Booth.Name);

                using (BoothClient.Prioritize(BoothPriority.Thumbnail))
                {
                    // 取れなくても商品は画面に出す。出さないとその商品は永久に見えない
                    if (await _images.SyncOneAsync(item.Id, item.Booth.Images[0], queue.Outage, cancellationToken))
                    {
                        downloaded++;
                    }
                }

                // 見込みは取り終えてから減らす（①②と揃える）
                work.PlanRequests(thumbnails: -1);

                queue.Galleries.Enqueue(item);
                queue.GalleriesTotal++;
                continue;
            }

            // ⑤ 残りの画像。商品ごとにまとめて取ると、その商品を開いたときに揃っている確率が上がる
            if (queue.Galleries.TryDequeue(out var gallery))
            {
                Report(progress, ImportPhase.FetchingGallery, ++queue.GalleriesDone, queue.GalleriesTotal, gallery.Booth.Name);

                using (BoothClient.Prioritize(BoothPriority.Gallery))
                {
                    downloaded += (await _images.SyncAsync(gallery.Id, gallery.Booth.Images, queue.Outage, cancellationToken)).Downloaded;
                }

                work.PlanRequests(gallery: -(gallery.Booth.Images.Count - 1)); // 1枚目は④で数えた

                continue;
            }

            // ⑥ ショップのアイコン。使うのはショップ画面と作者名の横だけで、無くても名前で用は足りる
            var (subdomain, thumbnailUrl) = queue.ShopIcons.First();
            queue.ShopIcons.Remove(subdomain);
            Report(progress, ImportPhase.FetchingShopIcons, ++queue.IconsDone, queue.IconsTotal, subdomain);

            using (BoothClient.Prioritize(BoothPriority.ShopIcon))
            {
                await _images.SyncShopIconAsync(subdomain, thumbnailUrl, queue.Outage, cancellationToken);
            }

            work.PlanRequests(icons: -1);
        }

        return downloaded;
    }

    /// <summary>
    /// 周回の外で取る画像の列（U5）。
    ///
    /// 新しい周回の1枚目は**先頭**へ入れる。積んだ物が早く一覧に出ることの方が、
    /// 前の周回の2枚目より先に要る。件数は周回をまたいで数える（画面には1本の進み具合として出す）。
    /// </summary>
    private sealed class ImageQueue
    {
        public LinkedList<ItemRecord> Thumbnails { get; } = new();

        public Queue<ItemRecord> Galleries { get; } = new();

        public Dictionary<string, string> ShopIcons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int ThumbnailsDone { get; set; }

        public int ThumbnailsTotal { get; set; }

        public int GalleriesDone { get; set; }

        public int GalleriesTotal { get; set; }

        public int IconsDone { get; set; }

        public int IconsTotal { get; set; }

        public bool IsEmpty => Thumbnails.Count == 0 && Galleries.Count == 0 && ShopIcons.Count == 0;

        /// <summary>
        /// ④⑤⑥の結果を続けて数える。①②とは分けて持つ——画像は別の置き場（pximg）から来るので、
        /// 画像だけが落ちていても①②を止める理由にならず、その逆も同じ
        /// </summary>
        public BoothOutageWatch Outage { get; } = new();

        public void Enqueue(IReadOnlyList<ItemRecord> withImages, IReadOnlyDictionary<string, string> shopIcons)
        {
            // 並びは保ったまま先頭へ
            for (var index = withImages.Count - 1; index >= 0; index--)
            {
                Thumbnails.AddFirst(withImages[index]);
            }

            ThumbnailsTotal += withImages.Count;

            foreach (var (subdomain, url) in shopIcons)
            {
                if (ShopIcons.TryAdd(subdomain, url))
                {
                    IconsTotal++;
                }
            }
        }
    }

    private sealed class FetchResult
    {
        public int Added { get; init; }

        public int AlreadyKnown { get; init; }

        public int NotFound { get; init; }

        /// <summary>BOOTHに無かったので未確定へ戻すファイル。ここで捨てると手元から消える。</summary>
        public IReadOnlyList<UnresolvedFile> NotFoundFiles { get; init; } = [];

        public int TemporaryFailures { get; init; }

        /// <summary>画像の列（④⑤）に積む商品。画像を取らない設定なら空。</summary>
        public IReadOnlyList<ItemRecord> WithImages { get; init; } = [];

        /// <summary>画像の列（⑥）に積むショップのアイコン。サブドメイン → アイコンのURL。</summary>
        public IReadOnlyDictionary<string, string> ShopIcons { get; init; } = new Dictionary<string, string>();

        public int AvatarItemsUpdated { get; init; }

        public int AvatarsFound { get; init; }

        public string? AvatarDetectError { get; init; }

        public bool AvatarDetectRan { get; init; }
    }
}
