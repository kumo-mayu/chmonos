using System.IO.Compression;
using System.Text;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

/// <summary>手順 allnotices が使う、Program.cs の組み立て（命令の口・取り直し・商品を足す・取り込む）。</summary>
internal sealed record AllNoticesContext(
    DataStore Store,
    AppPaths Paths,
    OfflineClient Client,
    Func<UiCommand, Task<CommandResult>> Run,
    Func<ItemService> NewItemService,
    Func<int, Task<int>> AddItems,
    Func<string[], Task<int>> Scan,
    NotificationService Notifications,
    AttributeService Attributes);

/// <summary>
/// 通知の画面を読み合わせるための写し（notices-all）に、**通知の全種類**を足す（手順 `allnotices`。台本 `notices` の後に流す）。
///
/// なぜ状況から作るか：起動したとき・通知の画面を開いたときの見回り（一覧に無いタグ・フォルダ登録と zip・読めない商品・
/// 手で直した JSON・やりかけの記録・ページの作り・プロファイル）は「もう状況が無い」と見ると解消済みにする。
/// 知らせだけを書くと、開いた途端に解消済みに変わり、見たい形が残らない。そこで状況を置いてから、アプリと同じ検出・取り直し・命令で知らせを作らせる。
/// 知らせだけを書くのは、作る道が App にしか無い「展開先のファイルを取り込んだ」と、日付を戻せない保管庫の古い月、
/// それと「前の起動で続けられなかった」やりかけの知らせ（続きを済ませて解消済みにする前の姿）だけ。
///
/// BOOTH へは問い合わせない：取り直しは、つながらない相手に台本どおりの商品ページ（と「無い」の答え）を返させる。
/// 2回流しても増えない：やりかけの記録の知らせか保管庫の月があれば、もう足してあるとして何もしない
/// （同じ ID の既読への足し・保管庫への追記・復活の知らせの時刻入りの ID は、個別には重なりを防げないため）。
/// </summary>
internal static class AllNoticesFixtures
{
    /// <summary>台本 notices の changes が先頭の7件を使う。ここはその後ろから25件を種類ごとに専用で使う（壊す商品とほかの知らせを混ぜない）。</summary>
    private const int ChangesItems = 7;

    private const int OwnItems = 25;

    private const string Marker = ".chmonos-allnotices";

    private static readonly (string Heading, string[] Lines)[] BaseSections = [("■ 内容", ["作り物のファイル一式"])];

    public static async Task<int> RunAsync(AllNoticesContext c, string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("使い方: SandboxGen <保存先> allnotices <作り物のフォルダ>");
            return 2;
        }

        var store = c.Store;
        if (store.NotificationArchive.Months().Count > 0
            || store.Notifications.Load().Any(note => note.Id.StartsWith(PendingOperationRunner.NotificationPrefix + "allnotices-", StringComparison.Ordinal)))
        {
            Console.WriteLine("もう足してある（保管庫の月か、この手順のやりかけの知らせがある）。作り直すなら写しを消してから");
            return 0;
        }

        // 作り物の置き場。印の無い（ほかの物が置かれた）フォルダは消さない
        var fx = Path.GetFullPath(args[0]).TrimEnd('\\');
        if (Directory.Exists(fx) && Directory.EnumerateFileSystemEntries(fx).Any() && !File.Exists(Path.Combine(fx, Marker)))
        {
            Console.Error.WriteLine($"作り物の印の無いフォルダには書かない: {fx}");
            return 2;
        }

        if (Directory.Exists(fx))
        {
            Directory.Delete(fx, recursive: true);
        }

        Directory.CreateDirectory(fx);
        File.WriteAllText(Path.Combine(fx, Marker), "SandboxGen allnotices が作った作り物の置き場。作り直すときに消してよい");

        // ---- 商品を割り当てる ----
        var have = store.Items.EnumerateItemIds().Count(id => !LocalItemId.IsLocal(id));
        if (have < ChangesItems + OwnItems)
        {
            await c.AddItems(ChangesItems + OwnItems - have);
        }

        var ids = store.Items.EnumerateItemIds().Where(id => !LocalItemId.IsLocal(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var own = ids.Skip(ChangesItems).Take(OwnItems).ToList();
        string[] Take(int from, int count) => own.Skip(from).Take(count).ToArray();
        var tagItems = Take(0, 4);      // 消えたタグと属性（未読）・消えた小分類（未読）・既読・解消済み
        var variationItems = Take(4, 4); // 消えた（未読）・消えた（既読）・消えて戻った（解消済み＋復活の未読）・消えて戻った（復活を既読）
        var backItems = Take(8, 2);     // 非公開から戻った（自分で付けた名前あり・未読）・（既読）
        var folderItems = Take(10, 3);  // zip を入手（未読）・（既読）・（解消済み）
        var profileItems = Take(13, 3); // 変換ツールの置き場はあるのにプロファイルの見つからない商品
        var brokenItems = Take(16, 3);  // 読めない（控えあり）・（控えなし）・（直した＝解消済み）
        var pageItems = Take(19, 6);    // 見出しの無い説明へ取り直した商品（形式の変化の帯）

        var service = c.NewItemService();
        var refreshed = new List<string>();
        var bases = new Dictionary<string, ItemRecord>();
        foreach (var id in variationItems.Concat(backItems).Concat(pageItems))
        {
            bases[id] = (await store.Items.LoadAsync(id))!;
        }

        async Task<RefreshOutcome> RefreshAs(string id, ChangeVersion version)
        {
            c.Client.Script[id] = (version.ToJson(bases[id]), version.ToHtml());
            refreshed.Add(id);
            return await service.RefreshAsync(id);
        }

        async Task Must(string id, ChangeVersion version)
        {
            var outcome = await RefreshAs(id, version);
            if (outcome != RefreshOutcome.Updated)
            {
                throw new InvalidOperationException($"取り直せなかった: {id} → {outcome}");
            }
        }

        // ---- 1. 土台の版へ取り直す（説明が「作り物の商品です」に変わる知らせは、台本の changes と同じく残さない） ----
        var baseVersion = new ChangeVersion(Sections: BaseSections);
        foreach (var id in variationItems.Concat(backItems))
        {
            await Must(id, baseVersion);
        }

        // 形式の変化の帯：最近取り直した、本文が 200 字以上で見出しの無い商品が 5 件以上・8 割以上（NotificationService.DetectPageStructureAsync）
        var longText = Enumerable.Range(1, 8)
            .Select(n => $"確かめ用の作り物の商品です。この説明は見出しを持たない形にしてあり、読み取りが全滅したときの姿を作っています（{n} 行目）。")
            .ToArray();
        foreach (var id in pageItems)
        {
            await Must(id, new ChangeVersion(Description: longText));
        }

        var baseNotes = variationItems.Concat(backItems).Concat(pageItems).Select(id => $"item-updated:{id}").ToHashSet(StringComparer.Ordinal);
        await store.Notifications.UpdateAsync(list =>
        {
            list.RemoveAll(note => baseNotes.Contains(note.Id));
            return list;
        });

        // ---- 2. 消えたバリエーション・復活したバリエーション（取り直しが作る） ----
        var dropped = new ChangeVersion(ExtraVariations: 1, DropMainVariation: true, Sections: BaseSections);
        foreach (var id in variationItems)
        {
            await Must(id, dropped);
        }

        // 3・4 件目は元の版に戻す。「消えた」は解消済みになり、「復活した」が1件出る
        await Must(variationItems[2], baseVersion);
        await Must(variationItems[3], baseVersion);

        // ---- 3. 非公開と見なした商品が戻った（「無い」を3回でしきい値。AppSettings.NotFoundThreshold の既定） ----
        await store.Items.ChangeLocalAsync(backItems[0], local => local with { DisplayName = "作り物：自分で付けた名前" }, [LocalField.DisplayName]);
        bases[backItems[0]] = (await store.Items.LoadAsync(backItems[0]))!;
        foreach (var id in backItems)
        {
            c.Client.NotFound.Add(id);
            for (var n = 0; n < 3; n++)
            {
                await RefreshAs(id, baseVersion);
            }

            c.Client.NotFound.Remove(id);
            if (!(await store.Items.LoadAsync(id))!.Local.IsDelisted)
            {
                throw new InvalidOperationException($"非公開と見なされなかった: {id}");
            }

            await Must(id, baseVersion);
        }

        // ---- 4. タグの参照切れ（一覧に無い名前を商品に付けて、通知の画面を開いたときの検出を通す） ----
        await c.Run(new UiCommand.AddUserTag("作り物の分類", "残る小分類"));
        await SetTags(store, tagItems[0], [new UserTagAssignment { Top = "作り物の消えたタグ", Subs = [] }], new Dictionary<string, int> { ["作り物の消えた属性"] = 40 });
        await SetTags(store, tagItems[1], [new UserTagAssignment { Top = "作り物の分類", Subs = ["消えた小分類"] }], null);
        await SetTags(store, tagItems[2], [new UserTagAssignment { Top = "作り物の消えたタグ", Subs = [] }], null);
        await SetTags(store, tagItems[3], [new UserTagAssignment { Top = "作り物の後で作り直したタグ", Subs = [] }], null);
        await c.Run(new UiCommand.DetectOrphanReferences());
        await c.Run(new UiCommand.AddUserTag("作り物の後で作り直したタグ"));

        // ---- 5. フォルダ登録の商品に zip が入った（登録 → 隣に同じ名前の zip → 取り込みが測り直すときに見つける） ----
        var folders = new List<string>();
        for (var n = 0; n < folderItems.Length; n++)
        {
            var folder = Path.Combine(fx, "folders", $"SampleFolder{(char)('A' + n)}");
            var files = new Dictionary<string, byte[]>
            {
                ["はじめにお読みください.txt"] = Utf8($"作り物のフォルダ {n + 1}（展開した体の中身）"),
                [@"Textures\sample.bin"] = Enumerable.Range(0, 4096).Select(i => (byte)(i * (n + 3))).ToArray(),
            };
            foreach (var (name, data) in files)
            {
                var path = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }

            await c.Run(new UiCommand.RegisterFolder(folderItems[n], folder));
            WriteZip(folder + ".zip", files);
            folders.Add(folder);
        }

        // ---- 6. 展開先のファイルを取り込んだ（作る道は App の ImportRunner だけ。同じ ID と文で足す） ----
        // 取り込みが拾うのは決まった拡張子だけ（FolderScanner.TargetExtensions）。監視が見つけて自動で取り込むのは unitypackage
        var unpackedFiles = new Dictionary<string, byte[]>
        {
            ["SampleExtracted.unitypackage"] = BuildUnityPackage([("Assets/SampleExtracted/readme.txt", Utf8("作り物の展開先（zip と同じ中身）"))]),
        };
        var unpackedZip = Path.Combine(fx, "unpacked", "SampleExtracted.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(unpackedZip)!);
        WriteZip(unpackedZip, unpackedFiles);
        var targets = new List<string>();
        foreach (var (name, data) in unpackedFiles)
        {
            var path = Path.Combine(fx, "unpacked", "SampleExtracted", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
            // Windows が展開したファイルに付ける「展開元の zip」の記録
            File.WriteAllBytes(path + ":Zone.Identifier", Utf8($"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl={unpackedZip}\r\n"));
            targets.Add(path);
        }

        var origins = UnpackedFileResolver.FindOrigins(targets);
        if (origins.Count == 0)
        {
            throw new InvalidOperationException("展開先のファイルと見分けられなかった");
        }

        await c.Run(new UiCommand.AddNotification(new NotificationRecord
        {
            Id = "unpacked-imported",
            Kind = NotificationKind.UnpackedFilesImported,
            Title = "展開したフォルダの中のファイルを取り込みました",
            Detail = $"zipを展開したフォルダの中のファイルを {origins.Count} 件取り込みました。"
                + "元のzipで取り込み直すときは、そのzipを取り込んでから「展開先フォルダの削除」で片付けてください。",
            CreatedAt = DateTimeOffset.Now,
        }));

        // ---- 7. 変換ツールの置き場はあるのに、プロファイルのファイル名が今の決まりに合わない unitypackage ----
        var profileDir = Path.Combine(fx, "profiles");
        for (var n = 0; n < profileItems.Length; n++)
        {
            var package = BuildUnityPackage(
            [
                ($"Assets/OutfitRetargetingSystem/Editor/ProfilesV2/sample_profile_{n + 1}.json", Utf8($"{{\"note\":\"作り物のプロファイル {n + 1}（新しい名前の決まりの体）\"}}")),
                ("Assets/OutfitRetargetingSystem/Editor/ProfilesV2/readme.txt", Utf8($"作り物 {n + 1}")),
            ]);
            WriteZip(Path.Combine(profileDir, $"SampleProfile{n + 1}.zip"), new Dictionary<string, byte[]> { [$"SampleProfile{n + 1}.unitypackage"] = package });
        }

        // 取り込みそのもの。測り直しで 5 の zip を見つけ、6 の展開先のファイルと 7 の zip は未確定へ行く
        if (await c.Scan([.. targets, profileDir]) != 0)
        {
            throw new InvalidOperationException("取り込めなかった");
        }

        var unresolved = store.Unresolved.Load();
        for (var n = 0; n < profileItems.Length; n++)
        {
            var name = $"SampleProfile{n + 1}.zip";
            var file = unresolved.Single(candidate => candidate.Paths.Any(path => path.EndsWith(name, StringComparison.OrdinalIgnoreCase)));
            await c.Run(new UiCommand.AssignItemId(file.Hash, profileItems[n]));
        }

        // zip を入手した、の3件目は登録を外して解消済みにする（通知の画面を開いたときの検出と同じ道）
        await c.Run(new UiCommand.UnregisterFolder(folderItems[2], folders[2]));
        await c.Run(new UiCommand.DetectOrphanReferences());

        // ---- 8. 読めない商品の記録（控えのある物・無い物・直した物） ----
        await store.Items.ChangeLocalAsync(brokenItems[0], local => local with { Memo = "作り物：壊す前の控え（.prev）がある" }, [LocalField.Memo]);
        File.Delete(c.Paths.ItemCopyFile(brokenItems[1]));
        var resolvedOriginal = File.ReadAllBytes(c.Paths.ItemFile(brokenItems[2]));
        foreach (var id in brokenItems)
        {
            var path = c.Paths.ItemFile(id);
            var text = File.ReadAllText(path);
            // 書きかけで止まった体：途中で切れた JSON（読み取りが止まった行が「壊れている場所」に出る）
            File.WriteAllText(path, text[..(text.Length / 2)], new UTF8Encoding(false));
        }

        await c.Run(new UiCommand.DetectUnreadableItems());
        File.WriteAllBytes(c.Paths.ItemFile(brokenItems[2]), resolvedOriginal);
        await c.Run(new UiCommand.DetectUnreadableItems());

        // ---- 9. 手で直した JSON の食い違い（エクスプローラーで写した商品の記録。読み込みから外れるだけで、画面は止まらない） ----
        File.Copy(c.Paths.ItemFile(tagItems[0]), Path.Combine(c.Paths.ItemsDir, $"{tagItems[0]} - コピー.json"));
        await c.Notifications.DetectHandEditIssuesAsync();

        // ---- 10. 途中で止まった操作（やりかけの記録を置いて、起動時の続きを通す） ----
        await c.Run(new UiCommand.AddAttribute("作り物の属性 旧A"));
        await c.Run(new UiCommand.AddAttribute("作り物の属性 旧B"));
        var started = DateTimeOffset.Now.AddHours(-2);
        // 記録した後で一覧が変わった体（指紋が合わない）：続きは当てず、知らせが残る
        var changed = new PendingOperation
        {
            Id = "allnotices-changed", Kind = PendingOperationKind.RenameAttribute, StartedAt = started,
            OldName = "作り物の属性 旧A", NewName = "作り物の属性 新A", Keep = AttributeMergeValue.KeepTarget,
            MasterFingerprint = "0000000000000000", Holders = new Dictionary<string, string>(),
        };
        // 前の起動で続けられず、今回の起動で済んだ体：知らせは解消済みになる
        var fingerprint = await c.Attributes.FingerprintRenameAsync("作り物の属性 旧B", "作り物の属性 新B");
        var resumed = new PendingOperation
        {
            Id = "allnotices-resumed", Kind = PendingOperationKind.RenameAttribute, StartedAt = started.AddMinutes(1),
            OldName = "作り物の属性 旧B", NewName = "作り物の属性 新B", Keep = AttributeMergeValue.KeepTarget,
            MasterFingerprint = fingerprint.Master, Holders = fingerprint.Holders,
        };
        await store.PendingOperations.UpdateAsync(list => [.. list, changed, resumed]);
        await c.Run(new UiCommand.AddNotification(new NotificationRecord
        {
            Id = PendingOperationRunner.NotificationPrefix + resumed.Id, Kind = NotificationKind.UnfinishedOperation,
            Title = PendingOperationRunner.TitleOf(resumed), Detail = PendingOperationRunner.FailedDetail, CreatedAt = started.AddMinutes(5), IsStrong = true,
        }));
        await c.Run(new UiCommand.ResumePendingOperations());

        // ---- 11. 既読と解消済み（人が押す操作と同じ命令） ----
        var notes = store.Notifications.Load();
        await c.Run(new UiCommand.MarkNotificationsRead(
        [
            $"item-updated:{ids[2]}",
            $"orphan-tag:{tagItems[2]}",
            $"variation-gone:{variationItems[1]}",
            .. notes.Where(note => note.Id.StartsWith($"variation-back:{variationItems[3]}:", StringComparison.Ordinal)).Select(note => note.Id),
            $"item-back:{backItems[1]}",
            $"archive-found:{folders[1]}",
        ]));
        // 商品ページの変更には見回りが無いので、解消済みは人の操作（宛先の無い知らせを片付ける時と同じ命令）で付ける
        await c.Run(new UiCommand.ResolveNotifications([$"item-updated:{ids[4]}"]));

        // ---- 12. ナビの帯の2種類。帯は新しい方しか出ないので、形式の変化を後に作る ----
        await new ConversionScan(store, new UnityPackagePathStore(c.Paths)).RunAsync();
        if (!await c.Notifications.DetectPageStructureAsync())
        {
            throw new InvalidOperationException("形式の変化と見なされなかった");
        }

        // ---- 13. 保管した通知（2か月。日付を戻す道が無いので、古い既読・解消済みの知らせを組んで保管庫へ足す） ----
        await store.NotificationArchive.AppendAsync(await ArchivedNotesAsync(store, ids));

        // 取り直しは次の予定日を普段の間隔で置く。作り物の番号は BOOTH に無いので、来ない先へ戻す
        foreach (var id in refreshed.Distinct())
        {
            await store.Items.ChangeLocalAsync(
                id, local => local with { NextFetchDueAt = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero) }, [LocalField.NextFetchDueAt]);
        }

        return Report(store);
    }

    private static Task SetTags(DataStore store, string id, List<UserTagAssignment> tags, Dictionary<string, int>? attributes)
        => store.Items.ChangeLocalAsync(
            id,
            local => local with { UserTags = tags, Attributes = attributes ?? local.Attributes },
            attributes is null ? [LocalField.UserTags] : [LocalField.UserTags, LocalField.Attributes]);

    private static async Task<List<NotificationRecord>> ArchivedNotesAsync(DataStore store, List<string> ids)
    {
        var notes = new List<NotificationRecord>();
        var now = DateTimeOffset.Now;
        // 9月と10月（今月より前の2か月）。月の欄で選べる・新しい順に並ぶ・商品名で絞れるのを見る
        foreach (var monthsAgo in new[] { 1, 2 })
        {
            var month = new DateTimeOffset(now.Year, now.Month, 1, 12, 0, 0, now.Offset).AddMonths(-monthsAgo);
            for (var n = 0; n < 6; n++)
            {
                var id = ids[(n + monthsAgo * 3) % ids.Count];
                var title = (await store.Items.LoadAsync(id))?.DisplayName ?? id;
                var created = month.AddDays(2 + n * 4).AddHours(n);
                notes.Add((n % 3) switch
                {
                    0 => new NotificationRecord
                    {
                        Id = $"item-updated:{id}", Kind = NotificationKind.ItemUpdated, ItemId = id, Title = title,
                        Detail = "価格 ¥ 1,000 → ¥ 1,200", CreatedAt = created, IsRead = true,
                        Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 1,000", After = "¥ 1,200" }],
                    },
                    1 => new NotificationRecord
                    {
                        Id = $"variation-gone:{id}", Kind = NotificationKind.OrphanVariationLink, ItemId = id, Title = title,
                        Detail = "消えたバリエーション：通常版", CreatedAt = created, IsRead = true, IsResolved = true,
                    },
                    _ => new NotificationRecord
                    {
                        Id = $"orphan-tag:{id}", Kind = NotificationKind.OrphanTag, ItemId = id, Title = title,
                        Detail = "ユーザータグ：作り物の古いタグ", CreatedAt = created, IsResolved = true,
                    },
                });
            }
        }

        // 宛先の商品がもう無い知らせ（押すと「この商品は見つかりませんでした」）
        notes.Add(new NotificationRecord
        {
            Id = "item-updated:90099999", Kind = NotificationKind.ItemUpdated, ItemId = "90099999", Title = "作り物：今は無い商品",
            Detail = "商品名", CreatedAt = new DateTimeOffset(now.Year, now.Month, 1, 9, 0, 0, now.Offset).AddMonths(-1).AddDays(20), IsRead = true,
            Diffs = [new NotificationDiff { Field = "商品名", Before = "作り物：前の名前", After = "作り物：今は無い商品" }],
        });
        return notes;
    }

    /// <summary>種類ごとの数を出す。足りない種類があれば 1 で終える（作れたつもりで写しを渡さない）。</summary>
    private static int Report(DataStore store)
    {
        var notes = store.Notifications.Load();
        Console.WriteLine($"通知 {notes.Count} 件（未読 {notes.Count(note => !note.IsRead && !note.IsResolved)}）");
        foreach (var group in notes.GroupBy(note => note.Kind).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()} 件（未読 {group.Count(note => !note.IsRead && !note.IsResolved)}"
                + $"・既読 {group.Count(note => note.IsRead && !note.IsResolved)}・解消済み {group.Count(note => note.IsResolved)}"
                + $"・強い {group.Count(note => note.IsStrong)}・重ねた {group.Count(note => note.UpdatedAt is not null)}）");
        }

        var months = store.NotificationArchive.Months();
        Console.WriteLine($"保管庫: {months.Count} か月（{string.Join("・", months)}）・やりかけの記録 {store.PendingOperations.Load().Count} 件");

        var missing = Enum.GetValues<NotificationKind>().Where(kind => notes.All(note => note.Kind != kind)).ToList();
        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"作れなかった種類: {string.Join("・", missing)}");
            return 1;
        }

        return 0;
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static void WriteZip(string path, IReadOnlyDictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            using var stream = archive.CreateEntry(name.Replace('\\', '/')).Open();
            stream.Write(data);
        }
    }

    // unitypackage の形（Program.cs の BuildUnityPackage と同じ）：tar.gz の中に、資産ごとに GUID の名前のフォルダと pathname・asset・asset.meta
    private static byte[] BuildUnityPackage(IReadOnlyList<(string Path, byte[] Data)> assets)
    {
        static string Guid(string pathname) => Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(pathname)));

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new System.Formats.Tar.TarWriter(gzip, System.Formats.Tar.TarEntryFormat.Ustar, leaveOpen: true))
        {
            void Entry(string name, byte[] data)
                => tar.WriteEntry(new System.Formats.Tar.UstarTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });

            foreach (var (pathname, data) in assets)
            {
                var guid = Guid(pathname);
                Entry($"{guid}/pathname", Encoding.UTF8.GetBytes(pathname));
                Entry($"{guid}/asset", data);
                Entry($"{guid}/asset.meta", Encoding.UTF8.GetBytes($"fileFormatVersion: 2\nguid: {guid}\n"));
            }
        }

        return buffer.ToArray();
    }
}
