using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BoothZipInspector;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

/// <summary>
/// 画面で確かめが要る例外の状態（取り外しているドライブ・文字が変わった外付け・見つからない・壊れた zip・外した・古い版・
/// 確かめられない・BOOTH に無い・非公開・販売終了・未確定の束や札）を、今ある写しに**足すだけ**で作る（手順 `exceptions`）。
///
/// なぜ取り込み（`scan`）で作らないか：取り込みの周回は、写しにある全商品のファイルを見回って `missingSince`・`volumes` を書き、
/// `import-state.json`・`volumes.json` も書き直す。ユーザの普段のデータの写し（daily-ui）では、元の商品の記録を変えてしまう。
/// 取り込みでは「無いドライブ」「古い版」「外した」のような、時間をかけて起きた状態も作れない。
/// そこで記録の形（型）だけアプリと同じにして、`DataStore` の錠を通して足す。今ある記録は書き換えない。
///
/// BOOTH へ行かせない決め事（この手順で足した物が、裏の取得や先回りの対象にならないように）：
/// - 数字の ID は 9010 万番台（BOOTH の番号は8桁に届いていない）。予定日は 2099 年（⑦に乗らない）。絵の URL は持たない（⑤に乗らない）。
///   BOOTH のショップは持たせず、手で入れたショップ（`local-` の鍵）にする（バナーを取りに行かない）。対応アバターの検出は済んだ印を付ける
/// - 足した未確定には、未確定の自動検索の控え（`unresolved-searches.json`・候補0件）も足す。
///   控えが無いと、設定「未確定の候補を先に検索しておく」が入った写しでは起動の後に BOOTH を検索する（⑧）
///
/// 作り物のファイルは台本（`.claude/skills/ui-check/scripts/exception-fixtures.ps1`）が先に置く。ここは置かれたファイルを読んでハッシュと中身を取る。
/// 2回流しても増えない（同じ ID の商品・同じハッシュの未確定・同じ文字・同じ控えがあれば足さない）。
/// </summary>
internal static class ExceptionFixtures
{
    // 作り物の外付けの通し番号。8桁の16進で、今つながっているどのディスクとも違う値なら何でもよい
    private const string OfflineSerial = "5A3E0001";

    private const long FirstId = 90100001;

    public static async Task<int> RunAsync(DataStore store, string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("使い方: SandboxGen <保存先> exceptions <作り物のフォルダ> <無いドライブの文字> <表で読み替える文字> <番号で読み替える文字>");
            return 2;
        }

        var fx = Path.GetFullPath(args[0]).TrimEnd('\\');
        var offline = Letter(args[1]);
        var byTable = Letter(args[2]);
        var bySerial = Letter(args[3]);
        if (!Directory.Exists(fx))
        {
            Console.Error.WriteLine($"作り物のフォルダが無い: {fx}（先に台本で作る）");
            return 2;
        }

        // 3つの文字は、今つながっていない文字でなければ作れない（在る文字だと「取り外している」にも「読み替え」にもならない）
        foreach (var letter in new[] { offline, byTable, bySerial })
        {
            if (Directory.Exists(letter + "\\"))
            {
                Console.Error.WriteLine($"{letter} は今つながっている。別の文字を渡す");
                return 2;
            }
        }

        // 作り物の置き場が載っているディスクの番号。読み替えはこのディスクへ向ける（在る作り物のファイルが、別の文字で記録されている形）
        var homeLetter = fx[..2];
        var homeSerial = SerialOf(homeLetter + "\\");
        if (homeSerial is null)
        {
            Console.Error.WriteLine($"{homeLetter} の通し番号を読めない");
            return 2;
        }

        string F(string relative) => Path.Combine(fx, relative);
        string Q(string relative) => offline + "\\sample-外付け\\" + relative;
        string Moved(string letter, string relative) => letter + F(relative)[2..];

        var now = DateTimeOffset.Now;
        var missingSince = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(9));
        var offlineSeen = new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.FromHours(9));
        var delistedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(9));

        // ---- 商品 ----
        var localOnlyHash = await HashAsync(F(@"手元\sample-local-only.zip"));
        var localId = LocalItemId.For(localOnlyHash);

        var items = new List<ItemRecord>
        {
            // 1. 商品ページの手元のファイルの行を、1つの商品にそろえる（札と押せる物の違いを並べて見る）
            Booth(FirstId, "作り物：手元のファイルの行がそろった商品", "商品ページの手元のファイルの行（札）を並べて見るための作り物です。", now,
                variations: [new BoothVariation { Id = 901000011, Name = "通常版", Price = 1000, Status = "addable_to_cart", Type = "digital" },
                             new BoothVariation { Id = 901000012, Name = "差分版", Price = 500, Status = "addable_to_cart", Type = "digital" }]) with
            {
                Local = Base(now, "ふつう・同じ中身が2か所・壊れたzip・外した・古い版と新しい版・確かめられない・見つからない（日時あり／場所が空）・取り外しているドライブ・2か所のうち1つが取り外しているドライブ") with
                {
                    LocalFiles =
                    [
                        await Existing(F(@"手元\sample-rows-ok.zip")) with { VariationId = 901000011 },
                        await Existing(F(@"手元\sample-rows-dup.zip"), F(@"写し\sample-rows-dup.zip")),
                        await Existing(F(@"手元\sample-rows-broken.zip"), broken: true),
                        await Existing(F(@"手元\sample-rows-detached.zip")) with { Detached = true },
                        await Existing(F(@"手元\sample-rows-update.zip")),
                        OldVersion("rows-update-old", F(@"手元\sample-rows-update.zip"), new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.FromHours(9))),
                        await Existing(F(@"読めない\sample-rows-locked.zip")),
                        Missing("rows-gone", F(@"手元\sample-rows-gone.zip"), missingSince),
                        Missing("rows-nowhere", null, null),
                        Offline("rows-offline", Q("sample-rows-offline.zip")),
                        await Existing(F(@"手元\sample-rows-dup2.zip")) is var dup2
                            ? dup2 with
                            {
                                Paths = [.. dup2.Paths, Q("sample-rows-dup2.zip")],
                                Volumes = new Dictionary<string, string> { [dup2.Paths[0]] = homeSerial, [Q("sample-rows-dup2.zip")] = OfflineSerial },
                            }
                            : throw new InvalidOperationException(),
                    ],
                },
            },

            // 2. 持っているファイルが全部見つからない（カードの印・右クリックの開く／Unity が薄い）
            Booth(FirstId + 1, "作り物：ファイルが全部見つからない商品", "見つからないファイルだけを持つ作り物です。", now) with
            {
                Local = Base(now, "ファイルが全部見つからない（日時の付いた物と、場所が空の物）") with
                {
                    LocalFiles = [Missing("allgone-1", F(@"手元\sample-allgone-1.zip"), missingSince), Missing("allgone-2", null, null)],
                },
            },

            // 3. 取り外しているドライブの上だけ（無くなったとは言わない。フォルダビューで灰色の外付け）
            Booth(FirstId + 2, "作り物：取り外しているドライブの商品", "外付けのドライブの上にだけある作り物です。", now) with
            {
                Local = Base(now, $"ファイルとフォルダが {offline} （今つながっていない外付け）の上にある") with
                {
                    LocalFiles = [Offline("offline-a", Q("sample-offline-a.zip"))],
                    LocalFolders =
                    [
                        new LocalFolderRecord
                        {
                            Path = Q("SampleOfflineFolder"), FileCount = 12, TotalBytes = 48_000_000, RegisteredAt = offlineSeen, LastSeenAt = offlineSeen,
                            UnityPackages = ["SampleOfflineFolder.unitypackage"], Volume = OfflineSerial,
                        },
                    ],
                },
            },

            // 4. ドライブ文字が変わった外付け（volumes.json の表で今の文字へ読み替える。記録は書き換えない）
            Booth(FirstId + 3, "作り物：ドライブ文字が変わった商品（表で読み替え）", "記録した文字と今の文字が違う作り物です。", now) with
            {
                Local = Base(now, $"{byTable} として記録したファイルとフォルダ。{byTable} は {homeLetter} と同じ通し番号と控えてあるので、{homeLetter} の下に出れば正しい") with
                {
                    LocalFiles = [await ExistingAs(F(@"手元\sample-remap-table.zip"), Moved(byTable, @"手元\sample-remap-table.zip"))],
                    LocalFolders = [await FolderAs(F(@"手元\SampleRemapFolder"), Moved(byTable, @"手元\SampleRemapFolder"), now, volume: null)],
                },
            },

            // 5. ドライブ文字が変わった外付け（記録の場所ごとの通し番号で読み替える。表に行は無い）
            Booth(FirstId + 4, "作り物：ドライブ文字が変わった商品（番号で読み替え）", "記録の通し番号で今の文字を探す作り物です。", now) with
            {
                Local = Base(now, $"{bySerial} として記録したファイル。場所ごとの通し番号が {homeLetter} のディスクを指すので、{homeLetter} の下に出れば正しい") with
                {
                    LocalFiles =
                    [
                        await ExistingAs(F(@"手元\sample-remap-serial.zip"), Moved(bySerial, @"手元\sample-remap-serial.zip")) is var moved
                            ? moved with { Volumes = new Dictionary<string, string> { [moved.Paths[0]] = homeSerial } }
                            : throw new InvalidOperationException(),
                    ],
                },
            },

            // 6. 登録したフォルダが見つからない（「見つからないファイルを探す…」の候補の対象）
            Booth(FirstId + 5, "作り物：登録フォルダが見つからない商品", "フォルダごと登録して、そのフォルダが無くなった作り物です。", now) with
            {
                Local = Base(now, "フォルダごと登録したフォルダが、つながっているドライブの上で見つからない") with
                {
                    LocalFolders =
                    [
                        new LocalFolderRecord
                        {
                            Path = F(@"消えた\SampleMissingFolder"), FileCount = 8, TotalBytes = 12_000_000, RegisteredAt = missingSince.AddDays(-10),
                            LastSeenAt = missingSince.AddDays(-1), MissingSince = missingSince, Volume = homeSerial,
                        },
                    ],
                },
            },

            // 7. zip と unitypackage が入ったフォルダを登録（フォルダの行に［Unity ▾］）
            Booth(FirstId + 6, "作り物：zipの入ったフォルダを登録した商品", "フォルダごと登録した作り物です。", now) with
            {
                Local = Base(now, "フォルダごと登録。中に zip と unitypackage がある") with
                {
                    LocalFolders = [await FolderAs(F(@"フォルダ登録\SampleFolderItem"), F(@"フォルダ登録\SampleFolderItem"), now, volume: homeSerial)],
                },
            },

            // 8. 壊れた zip だけ
            Booth(FirstId + 7, "作り物：壊れたzipだけの商品", "開けない zip を持つ作り物です。", now) with
            {
                Local = Base(now, "zip の目録が読めない（途中で切れたダウンロードの形）") with
                {
                    LocalFiles = [await Existing(F(@"手元\sample-broken-only.zip"), broken: true)],
                },
            },

            // 9. 古い版だけ（未所持。置き換わった場所に別の中身が未確定で残っている＝未確定の「同じ場所にあった」の相手）
            Booth(FirstId + 8, "作り物：古い版だけが残った商品", "同じ名前で別の中身に上書きされた作り物です。", now) with
            {
                Local = Base(now, "古い版の行だけ（所持に数えない）。置き換わった場所の新しい中身は未確定にある") with
                {
                    LocalFiles = [OldVersion("samepath-old", F(@"未確定\sample-unres-samepath.zip"), new DateTimeOffset(2026, 11, 8, 19, 0, 0, TimeSpan.FromHours(9)))],
                },
            },

            // 10. 外したファイルだけ（灰色の行。最後のファイルを外して「残す」を選んだ形）
            Booth(FirstId + 9, "作り物：外したファイルだけの商品", "ファイルを外して商品だけ残した作り物です。", now) with
            {
                Local = Base(now, "この商品から外したファイルの行だけ（所持に数えない）") with
                {
                    LocalFiles = [await Existing(F(@"手元\sample-detached-only.zip")) with { Detached = true }],
                },
            },

            // 11. 確かめられない場所だけ（読む権限の無いフォルダ。見つからないとは書かない）
            Booth(FirstId + 10, "作り物：確かめられない場所の商品", "読む権限の無いフォルダにある作り物です。", now) with
            {
                Local = Base(now, "読む権限を外したフォルダの中のファイル（片付けは Remove-ChmonosFixtures）") with
                {
                    LocalFiles = [await Existing(F(@"読めない\sample-locked-only.zip"))],
                },
            },

            // 12. 販売終了（一度は取れた商品が BOOTH から消えた）。展開したフォルダのある zip と、ほかの商品と共有するファイルも持つ
            Booth(FirstId + 11, "作り物：販売終了の商品", "BOOTH から消えた作り物です。", delistedAt) with
            {
                Local = Base(now, "販売終了（3回続けて見つからなかった）。zip の隣に展開したフォルダがある。1つのファイルは BOOTH に無い商品と共有") with
                {
                    LocalFiles =
                    [
                        await Existing(F(@"手元\sample-delisted.zip")),
                        await Existing(F(@"展開あり\sample-unpacked.zip")),
                        await Existing(F(@"手元\sample-shared.zip")),
                    ],
                    LastFetchedAt = delistedAt,
                    ConsecutiveNotFoundCount = 3,
                    IsDelisted = true,
                },
            },

            // 13. 商品 ID は分かるが BOOTH で見つからないまま登録（未確定の「このIDで登録」の非公開の形。ItemService.UnpublishedItem と同じ状態）
            new ItemRecord
            {
                Id = (FirstId + 12).ToString(),
                Booth = new BoothBlock(),
                Local = Base(now, "IDのまま登録した非公開の商品（BOOTH から一度も取れていない）") with
                {
                    DisplayName = "作り物：BOOTHで非公開のまま登録した商品",
                    LocalFiles = [await Existing(F(@"手元\sample-unpublished.zip"))],
                    LastFetchedAt = null,
                    ConsecutiveNotFoundCount = 3,
                    IsDelisted = true,
                },
            },

            // 14. BOOTH に無い商品（仮 ID）。名前・カテゴリ・ショップは手で入れた形
            new ItemRecord
            {
                Id = localId,
                Booth = new BoothBlock(),
                Local = Base(now, "BOOTHに無い商品（仮のID）。1つのファイルは販売終了の商品と共有") with
                {
                    DisplayName = "作り物：BOOTHに無い商品",
                    Category = "3D衣装",
                    LocalFiles = [await Existing(F(@"手元\sample-local-only.zip")), await Existing(F(@"手元\sample-shared.zip"))],
                    // 仮IDは⑦に乗らないので予定日を持たない（ItemService.RegisterLocalItemAsync と同じ）
                    NextFetchDueAt = null,
                    LastFetchedAt = null,
                },
            },
        };

        var addedItems = 0;
        foreach (var item in items)
        {
            if (store.Items.Exists(item.Id))
            {
                continue;
            }

            // 錠の中で新しい商品を作る道（CLAUDE.md「Items.SaveAsync を直接呼ぶのは、新しい商品を作るときだけ」）
            await store.Items.SaveAsync(item);
            addedItems++;

            // BOOTH から取った形の商品には説明の HTML も置く（②を通った印。無いと、取り込みがこの商品に当たったとき②を取りに行く。
            // 説明は 200 字未満にしてあるので、「形式の変化」の数にも入らない）
            if (item.Booth.FetchedAt is not null)
            {
                await store.Items.SaveDescriptionHtmlAsync(item.Id, $"<p>{System.Net.WebUtility.HtmlEncode(item.Booth.Description)}</p>");
            }
        }

        // ---- 未確定 ----
        var bundleA = await LooseUnder(F(@"未確定\SampleBundleA"), zone: F(@"未確定\sample-bundle-a.zip"), now);
        var bundleB = await LooseUnder(F(@"未確定\SampleBundleB"), zone: F(@"消えた\sample-bundle-b.zip"), now);
        List<UnresolvedFile> unresolved =
        [
            await Unresolved(now, F(@"未確定\sample-unres-broken.zip"), broken: true),
            (await Unresolved(now, F(@"未確定\sample-unres-private.zip"))).WithNotOnBooth(
                new BoothNotFoundNote { ItemId = "90100099", CheckedAt = new DateTimeOffset(2026, 11, 10, 22, 0, 0, TimeSpan.FromHours(9)) }) is var notOnBooth
                ? Copy(notOnBooth, candidates: ["90100099"])
                : throw new InvalidOperationException(),
            await Unresolved(now, F(@"未確定\sample-bundle-a.zip")),
            .. bundleA,
            .. bundleB,
            Copy(await Unresolved(now, F(@"未確定\sample-unres-samepath.zip")), samePath: [(FirstId + 8).ToString()]),
            await Unresolved(now, F(@"未確定\sample-unres-twoplaces.zip"), F(@"写し\sample-unres-twoplaces.zip")),
            Copy(await Unresolved(now, F(@"未確定\sample-unres-candidate.zip")), candidates: [(FirstId + 12).ToString()]),
            new UnresolvedFile
            {
                Hash = FakeHash("unres-offline"), Paths = [Q("sample-unres-offline.zip")], SizeBytes = 3_400_000,
                ModifiedAtUtc = offlineSeen.ToUniversalTime(), FirstSeenAt = offlineSeen, Contents = ["SampleOffline/readme.txt"],
            },
        ];

        var addedUnresolved = 0;
        await store.Unresolved.UpdateAsync(current =>
        {
            var known = current.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = unresolved.Where(file => known.Add(file.Hash)).ToList();
            addedUnresolved = added.Count;
            return [.. current, .. added];
        });

        // ---- 未確定の自動検索の控え（⑧が BOOTH を検索しないように） ----
        // 引く対象は画面と同じ決め方（ResolveViewModel.SearchTargetFor）：zip 自身か、記録の展開元の zip。
        // この手順の未確定は全部どちらかの形にしてある（ばらのファイルは展開元を持たせた）ので、フォルダの判定に頼らない
        var targets = unresolved
            .GroupBy(file => UnresolvedOrigin.For(file)?.ArchivePath ?? file.Paths[0], StringComparer.OrdinalIgnoreCase)
            .Select(group => new CandidateSearchEntry { Target = group.Key, Files = [.. group.Select(file => file.Hash)], SearchedAt = now, Candidates = [] })
            .ToList();
        var addedSearches = 0;
        await store.UnresolvedSearches.UpdateAsync(current =>
        {
            var known = current.Searches.Select(entry => entry.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = targets.Where(entry => known.Add(entry.Target)).ToList();
            addedSearches = added.Count;
            return current with { Searches = [.. current.Searches, .. added] };
        });

        // ---- ドライブ文字の控え ----
        // 無いドライブはラベルで名乗る（フォルダビューの灰色の見出し）。表で読み替える文字は、作り物の置き場のディスクの番号を指す。
        // 番号で読み替える文字は、表に行を持たせない（記録の場所ごとの番号だけで読む道を見るため）
        var volumes = new[]
        {
            new VolumeRecord { Letter = offline, Serial = OfflineSerial, Label = "作り物の外付け", SeenAt = offlineSeen },
            new VolumeRecord { Letter = byTable, Serial = homeSerial, Label = "作り物の読み替え", SeenAt = offlineSeen },
        };
        var addedVolumes = 0;
        await store.Volumes.UpdateAsync(current =>
        {
            var known = current.Select(volume => volume.Letter).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = volumes.Where(volume => known.Add(volume.Letter)).ToList();
            addedVolumes = added.Count;
            return [.. current, .. added];
        });

        Console.WriteLine($"例外の作り物を足した: 商品 {addedItems}/{items.Count} 件・未確定 {addedUnresolved}/{unresolved.Count} 件・自動検索の控え {addedSearches}/{targets.Count} 件・ドライブ文字 {addedVolumes}/{volumes.Length} 行");
        Console.WriteLine($"文字: 無いドライブ {offline}（番号 {OfflineSerial}）・表で読み替え {byTable}→{homeLetter}・番号で読み替え {bySerial}→{homeLetter}（番号 {homeSerial}）・仮ID {localId}");
        return 0;
    }

    private static string Letter(string text)
    {
        var letter = text.Trim().TrimEnd(':', '\\').ToUpperInvariant();
        if (letter.Length != 1 || !char.IsAsciiLetterUpper(letter[0]))
        {
            throw new ArgumentException($"ドライブ文字ではない: {text}");
        }

        return letter + ":";
    }

    private static ItemRecord Booth(long id, string name, string description, DateTimeOffset fetchedAt, IReadOnlyList<BoothVariation>? variations = null) => new()
    {
        Id = id.ToString(),
        Booth = new BoothBlock
        {
            FetchedAt = fetchedAt,
            Name = name,
            Description = description,
            PublishedAt = fetchedAt.AddDays(-30),
            PriceText = "¥ 1,000",
            Url = $"https://example.invalid/items/{id}",
            Tags = ["作り物"],
            Category = new BoothCategory { Id = 208, Name = "3D衣装", ParentName = "3Dモデル" },
            Variations = variations ?? [new BoothVariation { Id = id * 10 + 1, Name = "通常版", Price = 1000, Status = "addable_to_cart", Type = "digital" }],
        },
    };

    private static LocalBlock Base(DateTimeOffset now, string memo) => new()
    {
        Memo = $"作り物（例外の確かめ用）：{memo}",
        Shop = new LocalShop { Name = "作り物のショップ", Subdomain = LocalShopKey.For("作り物のショップ") },
        AcquiredAt = new DateOnly(2026, 11, 1),
        AvatarsDetectedAt = now,
        LastFetchedAt = now,
        // 作り物の番号は BOOTH に無い。期限が来て取り直すと BOOTH へ問い合わせ、「見つからない」が積もるので、来ない先へ置く
        NextFetchDueAt = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static async Task<LocalFileRecord> Existing(string path, string? second = null, bool broken = false)
        => await ExistingAs(path, path, second, broken);

    /// <summary>在るファイルを読んで、記録には <paramref name="recordedPath"/> と書く（ドライブ文字が変わった形）。</summary>
    private static async Task<LocalFileRecord> ExistingAs(string path, string recordedPath, string? second = null, bool broken = false)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"作り物のファイルが無い（台本で先に作る）: {path}");
        }

        return new LocalFileRecord
        {
            Hash = await HashAsync(path),
            Paths = second is null ? [recordedPath] : [recordedPath, second],
            SizeBytes = info.Length,
            Contents = broken ? [] : ContentsOf(path),
            ArchiveBroken = broken,
            RegisteredAt = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(9)),
        };
    }

    private static LocalFileRecord Missing(string key, string? path, DateTimeOffset? since) => new()
    {
        Hash = FakeHash(key),
        Paths = path is null ? [] : [path],
        SizeBytes = 2_500_000,
        Contents = [$"Sample/{key}.unitypackage", "Sample/readme.txt"],
        MissingSince = since,
    };

    private static LocalFileRecord OldVersion(string key, string replacedPath, DateTimeOffset at) => new()
    {
        Hash = FakeHash(key),
        Paths = [],
        SizeBytes = 2_200_000,
        Contents = ["Sample/old.unitypackage", "Sample/readme.txt"],
        Replaced = new ReplacedVersion(replacedPath, at),
    };

    private static LocalFileRecord Offline(string key, string path) => new()
    {
        Hash = FakeHash(key),
        Paths = [path],
        SizeBytes = 6_800_000,
        Contents = [$"Sample/{key}.unitypackage", "Sample/readme.txt"],
        Volumes = new Dictionary<string, string> { [path] = OfflineSerial },
    };

    private static Task<LocalFolderRecord> FolderAs(string folder, string recordedPath, DateTimeOffset now, string? volume)
    {
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        return Task.FromResult(new LocalFolderRecord
        {
            Path = recordedPath,
            FileCount = files.Length,
            TotalBytes = files.Sum(file => new FileInfo(file).Length),
            RegisteredAt = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(9)),
            LastSeenAt = now,
            UnityPackages = [.. files.Where(file => file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))],
            Volume = volume,
        });
    }

    private static async Task<UnresolvedFile> Unresolved(DateTimeOffset now, string path, string? second = null, bool broken = false)
    {
        var info = new FileInfo(path);
        return new UnresolvedFile
        {
            Hash = await HashAsync(path),
            Paths = second is null ? [path] : [path, second],
            SizeBytes = info.Length,
            ModifiedAtUtc = info.LastWriteTimeUtc,
            FirstSeenAt = now,
            Contents = broken ? [] : ContentsOf(path),
            ArchiveBroken = broken,
        };
    }

    /// <summary>フォルダの下のばらのファイルを、展開元の zip の記録つきの未確定にする（エクスプローラの「すべて展開」の形）。</summary>
    private static async Task<List<UnresolvedFile>> LooseUnder(string folder, string zone, DateTimeOffset now)
    {
        var list = new List<UnresolvedFile>();
        foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            list.Add(await Unresolved(now, path) is var file ? Copy(file, zone: zone) : throw new InvalidOperationException());
        }

        return list;
    }

    /// <summary>init の欄を差し替えた写し（UnresolvedFile は record でないので with が使えない）。</summary>
    private static UnresolvedFile Copy(UnresolvedFile file, IReadOnlyList<string>? candidates = null, IReadOnlyList<string>? samePath = null, string? zone = null)
        => new UnresolvedFile
        {
            Hash = file.Hash,
            Paths = file.Paths,
            SizeBytes = file.SizeBytes,
            ModifiedAtUtc = file.ModifiedAtUtc,
            FirstSeenAt = file.FirstSeenAt,
            Contents = file.Contents,
            ZoneHostUrl = file.ZoneHostUrl,
            ZoneReferrerUrl = zone ?? file.ZoneReferrerUrl,
            CandidateItemIds = candidates ?? file.CandidateItemIds,
            SamePathItemIds = samePath ?? file.SamePathItemIds,
            ArchiveBroken = file.ArchiveBroken,
        }.WithNotOnBooth(file.NotOnBooth);

    private static IReadOnlyList<string> ContentsOf(string path)
        => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? [.. ZipInspector.Inspect(path).Summary.Files.Select(entry => entry.RelativePath)]
            : [];

    private static async Task<string> HashAsync(string path) => await FileHasher.ComputeSha256Async(path);

    /// <summary>無いファイルの記録に付けるハッシュ。在るファイルと重ならない（重なると見回りが「ほかの場所に在る」と見て場所を外す）。</summary>
    private static string FakeHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("chmonos-exception-fixture:" + key)));

    private static string? SerialOf(string root)
        => GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0) ? serial.ToString("X8") : null;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string rootPathName, StringBuilder? volumeNameBuffer, int volumeNameSize, out uint volumeSerialNumber,
        out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder? fileSystemNameBuffer, int nameSize);
}
