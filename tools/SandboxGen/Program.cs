using System.Text;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

// 確かめ用の写し（サンドボックス）を、作り物のデータで組み立てる。
//
// なぜ要るか：写しは、担当が画面を操作して作った状態をそのまま使い回していた。確かめで書き換わると元へ戻せず、
// sandboxes.md の説明と中身がずれていった（2026-09-30）。台本から作り直せれば、壊しても数十秒で同じ状態に戻る。
//
// 決め事：
// - **書くのはアプリと同じ道**（DataStore・CommandHandler・ImportPipeline）。JSON を手で組まないので、
//   データの形が変わっても、ここは作り直すだけで新しい形になる（古い形に合わせる直しが要らない）
// - **BOOTH へは問い合わせない。**取り込みには「つながらない相手」を渡す。呼ばれた回数を数えて最後に出す
//   （作り物のファイルは手掛かりが無いので 0 回のはず。0 でなければ、そのファイルには手掛かりが付いている）
// - 名前は全部作り物。商品 ID は 9000 万番台（BOOTH の番号は8桁に届いていない）。ショップも URL も実在しない物
// - 本番（%LOCALAPPDATA%\Chmonos）と friendtest には書かない
//
// 使い方: SandboxGen <保存先> <手順> [引数…]
//   init                               空の保存先を作る（通信を切った設定を書く）。既に商品があれば断る
//   items <件数>                       作り物の商品を足す（絵つき。BOOTH から取った体の商品）
//   scan <フォルダ>…                   フォルダを取り込む（アプリの取り込みそのもの）
//   register <パスの一部> <表示名> [大分類/小分類] [メモ]
//                                      未確定のファイルを「BOOTHに無い商品」として登録し、タグとメモを付ける
//   attach <パスの一部> <商品の番号>   未確定のファイルを、作り物の商品（1 始まり）に紐付ける
//   settings <鍵>=<値>…               設定を変える（importFolders・watchedFolders は ; で区切る）
//   manage                             管理の画面の台にする（タグ・属性・知らせ・改変を、今ある商品に大量に入れる）
//   changes                            商品ページの変化の知らせ（変わった行・名前・価格・販売終了）を、商品7件に作る
//                                      （アプリの取り直し＝ItemService.RefreshAsync に、台本どおりの JSON と HTML を返して作る）
//   show                               今の数を出す（商品・未確定・取り込み元）

Console.OutputEncoding = Encoding.UTF8;
if (args.Length < 2)
{
    Console.Error.WriteLine("使い方: SandboxGen <保存先> <init|items|scan|register|attach|settings|manage|changes|show> [引数…]");
    return 2;
}

var root = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
var step = args[1];
var rest = args.Skip(2).ToArray();

var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
string[] forbidden = [Path.Combine(local, "Chmonos"), Path.Combine(local, "Chmonos"), Path.Combine(local, "Chmonos-friendtest"), Path.Combine(local, "Chmonos-sandboxes", "friendtest")];
if (forbidden.Any(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine($"ここには書かない（本番・friendtest）: {root}");
    return 2;
}

var paths = new AppPaths(root);
if (step != "init" && !File.Exists(paths.SettingsFile))
{
    Console.Error.WriteLine($"保存先がまだ無い: {root}（先に init）");
    return 2;
}

paths.EnsureCreated();
// アプリが開いている写しには書かない（アプリと同じ錠。開いていれば取れない）
using var instanceLock = SingleInstanceLock.TryAcquire(paths);
if (instanceLock is null)
{
    Console.Error.WriteLine($"アプリがこの保存先を開いている: {root}");
    return 2;
}

var store = new DataStore(paths);
var settingsService = new SettingsService(store);
AppSettings Settings() => store.Settings.Load();
var client = new OfflineClient();
var images = new ImagePipeline(client, paths, Settings);
var unityPackages = new UnityPackageCatalog(store, new UnityPackagePathStore(paths));
var pipeline = new ImportPipeline(store, client, images, Settings, null, unityPackages);
var itemService = new ItemService(store, client, images, Settings);
var handler = new CommandHandler(
    pipeline, itemService, new EditService(store), null, null, new NotificationService(store, Settings),
    new UserTagService(store), new AttributeService(store), new ModificationService(store, images), null, unityPackages,
    settingsService, null, null, images, client, store.VideoTitles, store.ShopNotes, new MissingFileFinder(store), null, store.ImportState);

var imageCache = new Dictionary<(int Hue, int Index), byte[]>();

var code = step switch
{
    "init" => await InitAsync(),
    "items" => await AddItemsAsync(int.Parse(rest[0])),
    "scan" => await ScanAsync(rest),
    "register" => await RegisterAsync(rest),
    "attach" => await AttachAsync(rest[0], int.Parse(rest[1])),
    "settings" => await ChangeSettingsAsync(rest),
    "manage" => await FillManageAsync(),
    "changes" => await ChangesAsync(),
    "show" => await ShowAsync(),
    _ => Unknown(step),
};

if (client.Calls > 0)
{
    Console.WriteLine($"!! BOOTH へ問い合わせようとした: {client.Calls} 回（つながらない相手なので通信はしていない。手掛かりの付いたファイルがある）");
}

return code;

static int Unknown(string step)
{
    Console.Error.WriteLine($"不明な手順: {step}");
    return 2;
}

async Task<CommandResult> RunAsync(UiCommand command)
{
    var result = await handler.ExecuteAsync(command);
    if (result is CommandResult.Failed failed)
    {
        throw new InvalidOperationException($"{command.GetType().Name} が失敗: {failed.Message}");
    }

    return result;
}

async Task<int> InitAsync()
{
    if (store.Items.EnumerateItemIds().Count > 0)
    {
        Console.Error.WriteLine($"もう商品がある: {root}（作り直すなら、写しを消してから）");
        return 2;
    }

    // 確かめで何度も起動し直すので、起動しただけで BOOTH へ問い合わせる物を切る。
    // 並行で起動する決まり（ui-kit の Start-ChmonosApp）も、この2つが切れていることを見る
    await store.Settings.SaveAsync(new AppSettings
    {
        ResumeFetchInBackground = false,
        StartImportOnLaunch = false,
    });
    Console.WriteLine($"作った: {root}（使っていない間の取得・起動時の取り込みは切ってある）");
    return 0;
}

async Task<int> AddItemsAsync(int count)
{
    // 分類は BOOTH の分類表の名前（実在の区分。商品やショップの名前ではない）。
    // 偏りは実際の買い方に寄せる（衣装が多い）——分類が均等だと、分類で絞る画面の見え方が実物と離れる。
    // 番号は 3Dキャラクター（208）だけ実物と同じ。ほかは分類表の並びから振った作り物（画面は名前で見分ける）
    (int Id, string Name)[] categories = [(208, "3Dキャラクター"), (209, "3D衣装"), (209, "3D衣装"), (211, "3D装飾品"), (215, "3Dツール・システム"), (214, "3Dテクスチャ")];
    (string Name, string Subdomain)[] shops = [("作り物のショップ 甲", "chmonos-sample-kou"), ("作り物のショップ 乙", "chmonos-sample-otsu"), ("作り物のショップ 丙", "chmonos-sample-hei")];
    string[] kinds = ["アバター", "衣装", "衣装", "アクセサリ", "ギミック", "テクスチャ"];

    var existing = store.Items.EnumerateItemIds().Where(id => !LocalItemId.IsLocal(id)).Count();
    var now = DateTimeOffset.Now;
    var rng = new Random(20260930 + existing);
    for (var i = existing; i < existing + count; i++)
    {
        var id = (90000001 + i).ToString();
        var (categoryId, categoryName) = categories[i % categories.Length];
        var shop = shops[i % shops.Length];
        var name = $"作り物の商品 {i + 1:D3}（{kinds[i % kinds.Length]}）";
        var imageUrls = Enumerable.Range(0, 1 + i % 3).Select(n => $"https://example.invalid/sample/{id}/{n}.jpg").ToList();
        var price = 500 * (1 + rng.Next(8));

        var item = new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock
            {
                FetchedAt = now,
                Name = name,
                Description = $"確かめ用の作り物の商品です。\n\n■ 内容\n作り物のファイル一式\n\n■ 利用規約\n確かめのためだけの文です（{i + 1} 件目）。",
                PublishedAt = now.AddDays(-30 - i),
                PriceText = $"¥ {price:N0}",
                WishListsCount = rng.Next(0, 500),
                Url = $"https://example.invalid/items/{id}",
                Tags = ["作り物", kinds[i % kinds.Length], "VRChat"],
                Category = new BoothCategory { Id = categoryId, Name = categoryName, ParentName = "3Dモデル" },
                Shop = new BoothShop { Name = shop.Name, Subdomain = shop.Subdomain, Url = $"https://example.invalid/shop/{shop.Subdomain}" },
                Images = imageUrls.Select(url => new BoothImage { OriginalUrl = url }).ToList(),
                Variations = [new BoothVariation { Id = 900000000L + i, Name = "通常版", Price = price, Status = "addable_to_cart", Type = "digital" }],
                H2Sections = [new H2Section { Heading = "内容", Text = "作り物のファイル一式" }],
            },
            Local = new LocalBlock
            {
                Purchases = [new Purchase { VariationId = 900000000L + i, NameSnapshot = "通常版", Price = price }],
                AcquiredAt = DateOnly.FromDateTime(now.AddDays(-i).DateTime),
                LastFetchedAt = now,
                // 作り物の番号は BOOTH に無い。期限が来て取り直すと「見つからない」が積もるので、来ない先へ置く
                NextFetchDueAt = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AvatarsDetectedAt = now,
            },
        };
        // 錠の中で新しい商品を作る道（CLAUDE.md「Items.SaveAsync を直接呼ぶのは、新しい商品を作るときだけ」）
        await store.Items.SaveAsync(item);

        // 絵は、アプリが取得して置く場所と名前に合わせる（無いと、裏の取得が「残りの画像」として取りに行く）
        var dir = paths.ItemImagesDir(id);
        Directory.CreateDirectory(dir);
        for (var n = 0; n < imageUrls.Count; n++)
        {
            SaveSampleImage(Path.Combine(dir, ImagePipeline.FileNameFor(imageUrls[n])), i, n);
        }
    }

    Console.WriteLine($"作り物の商品を足した: {count} 件（合わせて {existing + count} 件）");
    return 0;
}

// 商品ごとに色の違う、縞のある絵。一覧で見分けが付けば足りる（384 画素＝アプリが保存する長辺の既定）。
// 色は 30 通り・縞は 3 通りの 90 種類を使い回す（2000 件で 4000 枚を1枚ずつ圧縮すると2分掛かる。300 件で 18 秒だった）
void SaveSampleImage(string path, int item, int index)
{
    const int Size = 384;
    var hue = item * 47 % 360 / 12 * 12 + index * 4;
    if (imageCache.TryGetValue((hue, index), out var cached))
    {
        File.WriteAllBytes(path, cached);
        return;
    }

    using var image = new Image<Rgba32>(Size, Size);
    image.ProcessPixelRows(rows =>
    {
        for (var y = 0; y < rows.Height; y++)
        {
            var row = rows.GetRowSpan(y);
            for (var x = 0; x < row.Length; x++)
            {
                var band = ((x + y * (1 + index)) / 48) % 2 == 0 ? 0.62 : 0.78;
                row[x] = FromHue(hue, band);
            }
        }
    });
    using var buffer = new MemoryStream();
    image.Save(buffer, new WebpEncoder { Quality = 80 });
    imageCache[(hue, index)] = buffer.ToArray();
    File.WriteAllBytes(path, imageCache[(hue, index)]);
}

static Rgba32 FromHue(int hue, double value)
{
    var c = value * 0.45;
    var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
    var m = value - c;
    var (r, g, b) = (hue / 60) switch
    {
        0 => (c, x, 0.0),
        1 => (x, c, 0.0),
        2 => (0.0, c, x),
        3 => (0.0, x, c),
        4 => (x, 0.0, c),
        _ => (c, 0.0, x),
    };
    return new Rgba32((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
}

async Task<int> ScanAsync(string[] folders)
{
    var missing = folders.Where(folder => !Directory.Exists(folder) && !File.Exists(folder)).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"無い: {string.Join(", ", missing)}");
        return 2;
    }

    var watch = System.Diagnostics.Stopwatch.StartNew();
    var result = await RunAsync(new UiCommand.ScanFolders(folders));
    if (result is not CommandResult.Imported imported)
    {
        Console.Error.WriteLine($"取り込みの結果が読めない: {result.GetType().Name}");
        return 1;
    }

    var s = imported.Summary;
    Console.WriteLine($"取り込んだ: {watch.Elapsed.TotalSeconds:F1} 秒・走査 {s.FilesScanned}・未確定 {s.UnresolvedFiles}・商品を足した {s.ItemsAdded}"
        + $"・読めないファイル {s.FilesUnreadable}・読めないフォルダ {s.FoldersUnreadable}・壊れた zip {s.FilesBrokenArchive}");
    return 0;
}

UnresolvedFile FindUnresolved(string part)
{
    var hits = store.Unresolved.Load()
        .Where(file => file.Paths.Any(path => path.Contains(part, StringComparison.OrdinalIgnoreCase)))
        .ToList();
    return hits.Count switch
    {
        1 => hits[0],
        0 => throw new InvalidOperationException($"未確定に無い: {part}（先に scan）"),
        _ => throw new InvalidOperationException($"未確定に {hits.Count} 件ある: {part}（1件に絞れる言葉で）"),
    };
}

async Task<int> RegisterAsync(string[] values)
{
    var file = FindUnresolved(values[0]);
    var name = values[1];
    var tag = values.Length > 2 ? values[2] : null;
    var memo = values.Length > 3 ? values[3] : null;

    await RunAsync(new UiCommand.RegisterLocalItem(file.Hash, name));
    var id = LocalItemId.For(file.Hash);

    if (!string.IsNullOrEmpty(tag) || !string.IsNullOrEmpty(memo))
    {
        var owns = new List<LocalField>();
        var local = new LocalBlock();
        if (!string.IsNullOrEmpty(tag))
        {
            var parts = tag.Split('/', 2);
            var sub = parts.Length > 1 ? parts[1] : null;
            // マスタに無い名前を商品に付けると「一覧に無いタグ」になる。先にマスタへ足す
            await RunAsync(new UiCommand.AddUserTag(parts[0], sub));
            local = local with { UserTags = [new UserTagAssignment { Top = parts[0], Subs = sub is null ? [] : [sub] }] };
            owns.Add(LocalField.UserTags);
        }

        if (!string.IsNullOrEmpty(memo))
        {
            local = local with { Memo = memo };
            owns.Add(LocalField.Memo);
        }

        await RunAsync(new UiCommand.SaveItemLocal(id, local, owns));
    }

    Console.WriteLine($"登録した: {id}「{name}」{(tag is null ? string.Empty : $" タグ {tag}")}{(memo is null ? string.Empty : " メモつき")}");
    return 0;
}

async Task<int> AttachAsync(string part, int number)
{
    var file = FindUnresolved(part);
    var id = (90000000 + number).ToString();
    if (!store.Items.Exists(id))
    {
        Console.Error.WriteLine($"作り物の商品が無い: {number} 番（{id}。先に items）");
        return 2;
    }

    // 商品が手元にあるので、BOOTH へは問い合わせずに紐付く
    await RunAsync(new UiCommand.AssignItemId(file.Hash, id));
    Console.WriteLine($"紐付けた: {Path.GetFileName(file.Paths[0])} → {id}");
    return 0;
}

async Task<int> ChangeSettingsAsync(string[] pairs)
{
    foreach (var pair in pairs)
    {
        var at = pair.IndexOf('=');
        if (at <= 0)
        {
            Console.Error.WriteLine($"鍵=値 の形で: {pair}");
            return 2;
        }

        var key = pair[..at];
        var value = pair[(at + 1)..];
        IReadOnlyList<string> List() => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Func<AppSettings, AppSettings>? change = key switch
        {
            "importFolders" => current => current with { ImportFolders = List() },
            "watchedFolders" => current => current with { WatchedFolders = List() },
            "resumeFetchInBackground" => current => current with { ResumeFetchInBackground = bool.Parse(value) },
            "startImportOnLaunch" => current => current with { StartImportOnLaunch = bool.Parse(value) },
            "colorTheme" => current => current with { ColorTheme = Enum.Parse<ColorThemeMode>(value, ignoreCase: true) },
            "notificationRetentionCount" => current => current with { NotificationRetentionCount = int.Parse(value) },
            _ => null,
        };
        if (change is null)
        {
            Console.Error.WriteLine($"変えられない鍵: {key}（importFolders・watchedFolders・resumeFetchInBackground・startImportOnLaunch・colorTheme・notificationRetentionCount）");
            return 2;
        }

        await RunAsync(new UiCommand.ChangeSettings(change));
        Console.WriteLine($"設定: {key} = {value}");
    }

    return 0;
}

// 管理の画面（タグ・属性・要確認・改変の左の一覧）の速さとメモリを測る台にする。
// 数は 2026-09-24 に台（stress-manage）を作ったときの物：大分類20×小分類10・属性20・知らせ1000・改変300
async Task<int> FillManageAsync()
{
    var rng = new Random(20260924);
    string[] topNames = ["衣装", "髪", "アクセサリ", "靴", "小物", "ギミック", "シェーダー", "モーション", "ツール", "テクスチャ",
        "ワールド", "アバター", "表情", "武器", "乗り物", "家具", "エフェクト", "音", "UI", "その他"];
    string[] subSuffix = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
    string[] attributeNames = ["かわいい", "かっこいい", "よく使う", "軽さ", "質感", "色味", "揺れもの", "改変のしやすさ", "説明の丁寧さ", "値段の満足",
        "季節感", "露出", "ポリゴン数", "テクスチャの細かさ", "Quest対応", "表情の豊かさ", "汎用性", "お気に入り度", "組み合わせやすさ", "思い出"];

    await store.UserTags.SaveAsync(new UserTagMaster
    {
        Tops = topNames.Select(top => new UserTagTop { Name = top, Subs = subSuffix.Select(sub => new UserTagSub { Name = top + sub }).ToList() }).ToList(),
    });
    await store.Attributes.SaveAsync(new AttributeMaster { Attributes = attributeNames.Select(name => new AttributeDefinition { Name = name }).ToList() });

    var items = (await store.Items.LoadAllAsync()).Items.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
    if (items.Count == 0)
    {
        Console.Error.WriteLine("商品が無い（先に items）");
        return 2;
    }

    foreach (var item in items)
    {
        // 1件に大分類を4つ、それぞれ小分類を1〜2つ。2000件なら、大分類1つに約400件・小分類1つに約60件になる
        var assigned = topNames.OrderBy(_ => rng.Next()).Take(4)
            .Select(top => new UserTagAssignment { Top = top, Subs = subSuffix.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).Select(sub => top + sub).ToList() })
            .ToList();
        var attributes = attributeNames.ToDictionary(name => name, _ => rng.Next(0, 101));
        await store.Items.SaveLocalAsync(
            item.Id, item.Local with { UserTags = assigned, Attributes = attributes }, [LocalField.UserTags, LocalField.Attributes]);
    }

    // 知らせ1000件。種類の比は、実際に溜まった知らせに寄せる（更新が多く、バリエーションの消失・復活が続く）
    var notes = new List<NotificationRecord>();
    var baseTime = DateTimeOffset.Now;
    string[] fields = ["価格", "商品名", "説明文", "画像", "販売状態", "バリエーション"];
    for (var i = 0; i < 1000; i++)
    {
        var item = items[rng.Next(items.Count)];
        var created = baseTime.AddMinutes(-i * 37);
        var read = rng.Next(100) < 45;
        var roll = rng.Next(100);
        if (roll < 62)
        {
            var diffs = fields.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4))
                .Select(field => field == "価格"
                    ? new NotificationDiff { Field = field, Before = $"¥ {rng.Next(1, 60) * 100}", After = $"¥ {rng.Next(1, 60) * 100}" }
                    : field is "説明文" or "画像" ? new NotificationDiff { Field = field } : new NotificationDiff { Field = field, Before = "前の" + field, After = "新しい" + field })
                .ToList();
            notes.Add(new NotificationRecord
            {
                Id = $"itemUpdated:{item.Id}:{i}", Kind = NotificationKind.ItemUpdated, ItemId = item.Id, Title = item.DisplayName,
                Detail = string.Join(" / ", diffs.Select(diff => diff.Before is null ? diff.Field : $"{diff.Field} {diff.Before} → {diff.After}")),
                Diffs = diffs, CreatedAt = created, IsRead = read, IsStrong = diffs.Any(diff => diff.Field is "販売状態" or "バリエーション"),
            });
        }
        else
        {
            var (kind, prefix, detail, resolved) = roll switch
            {
                < 80 => (NotificationKind.OrphanVariationLink, "variation-gone", "消えたバリエーション：通常版", rng.Next(100) < 30),
                < 92 => (NotificationKind.VariationBackOnBooth, "variation-back", "戻ったバリエーション：通常版", false),
                _ => (NotificationKind.ItemBackOnBooth, "item-back", "BOOTHで再び公開されました。", false),
            };
            notes.Add(new NotificationRecord
            {
                Id = $"{prefix}:{item.Id}:{i}", Kind = kind, ItemId = item.Id, Title = item.DisplayName,
                Detail = detail, CreatedAt = created, IsRead = read, IsResolved = resolved,
            });
        }
    }

    // 知らせは既定で200件までしか持たない。1000件を見るので上限を上げる
    await RunAsync(new UiCommand.ChangeSettings(current => current with { NotificationRetentionCount = 2000 }));
    await store.Notifications.SaveAsync(notes);

    // 改変300件。アバター40体・プロジェクト30個（2割は紐付け無し）・使ったもの3〜8件。プロジェクトは在り得ないパス
    foreach (var old in store.Modifications.EnumerateIds())
    {
        await store.Modifications.DeleteAsync(old);
    }

    var avatars = items.Where(item => item.Booth.Category?.Name == "3Dキャラクター").Take(40).ToList();
    if (avatars.Count == 0)
    {
        avatars = items.Take(1).ToList();
    }

    var projects = Enumerable.Range(1, 30).Select(n => $@"Q:\chmonos-sample\VRChatProjects\project{n:00}").ToList();
    for (var i = 0; i < 300; i++)
    {
        var avatar = avatars[rng.Next(avatars.Count)];
        var created = baseTime.AddHours(-i * 5 - 3);
        var name = $"改変{i + 1:000}";
        await store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = ModificationId.For(avatar.Id, name, created), AvatarItemId = avatar.Id, Name = name,
            CreatedAt = created, UpdatedAt = created.AddMinutes(30),
            UnityProject = rng.Next(100) < 80 ? projects[rng.Next(projects.Count)] : null,
            Members = Enumerable.Range(0, rng.Next(3, 9))
                .Select(k => new ModificationMember { ItemId = items[rng.Next(items.Count)].Id, AddedAt = created.AddMinutes(k + 1) })
                .ToList(),
        });
    }

    Console.WriteLine($"管理の台にした: 商品 {items.Count} 件にタグと属性・大分類 {topNames.Length}×小分類 {subSuffix.Length}・属性 {attributeNames.Length}"
        + $"・知らせ {notes.Count} 件（未読 {notes.Count(note => !note.IsRead)}）・改変 300 件（アバター {avatars.Count} 体）");
    return 0;
}

// 商品ページの変化の知らせを作る。知らせの中身（変わった行・名前・価格・販売終了）を実機で見るための台。
//
// **知らせは ItemService.RefreshAsync が作る**（NoteChangesAsync は private で、BoothChanges.Describe の結果から組む）。
// 知らせを手で組むと、行の差の作り方（LineDiff）が変わったときに写しだけ古い形で残るので、
// つながらない相手に「前の版」「今の版」の商品JSONとHTMLを返させ、アプリの取り直しをそのまま通す。
// まず全商品を前の版へ取り直し、知らせを空にしてから、今の版へ取り直す（前の版へ寄せるときの知らせを残さない）
async Task<int> ChangesAsync()
{
    var scripts = ChangeScriptData.Scripts;
    var ids = store.Items.EnumerateItemIds().Where(id => !LocalItemId.IsLocal(id)).OrderBy(id => id, StringComparer.Ordinal).Take(scripts.Length).ToList();
    if (ids.Count < scripts.Length)
    {
        Console.Error.WriteLine($"商品が足りない: {ids.Count} 件（{scripts.Length} 件要る。先に items）");
        return 2;
    }

    var service = new ItemService(store, client, images, Settings);
    // 色・価格・分類などの土台は、商品を足した直後の姿。版ごとに足す物だけ重ねる
    var bases = new Dictionary<string, ItemRecord>();
    for (var n = 0; n < ids.Count; n++)
    {
        bases[ids[n]] = (await store.Items.LoadAsync(ids[n]))!;
        await RefreshAsAsync(service, bases[ids[n]], scripts[n][0]);
    }

    await store.Notifications.SaveAsync(new List<NotificationRecord>());
    for (var n = 0; n < ids.Count; n++)
    {
        // 2版目以降は続けて取り直す（同じ商品に2回続けて変わった場合。未読の知らせの重ね方を見る）
        for (var version = 1; version < scripts[n].Length; version++)
        {
            await RefreshAsAsync(service, bases[ids[n]], scripts[n][version]);
        }
    }

    // 取り直しは次の予定日を普段の間隔で置く。作り物の番号は BOOTH に無いので、来ない先へ戻す
    foreach (var id in ids)
    {
        var item = (await store.Items.LoadAsync(id))!;
        await store.Items.SaveLocalAsync(
            id, item.Local with { NextFetchDueAt = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero) }, LocalOwners.Fetch);
    }

    var notes = store.Notifications.Load();
    var updated = notes.Where(note => note.Kind == NotificationKind.ItemUpdated).ToList();
    var withLines = updated.Where(note => note.Diffs.Any(diff => diff.Lines is { Count: > 0 })).ToList();
    Console.WriteLine($"変化の知らせを作った: 知らせ {notes.Count} 件（商品の更新 {updated.Count}）・変わった行を持つ {withLines.Count} 件"
        + $"・足した行 {withLines.Sum(note => note.Diffs.Sum(diff => diff.Lines?.Count(line => line.Kind == NotificationLineKind.Added) ?? 0))}"
        + $"・消した行 {withLines.Sum(note => note.Diffs.Sum(diff => diff.Lines?.Count(line => line.Kind == NotificationLineKind.Removed) ?? 0))}");
    return 0;
}

async Task RefreshAsAsync(ItemService service, ItemRecord baseItem, ChangeVersion version)
{
    client.Script[baseItem.Id] = (version.ToJson(baseItem), version.ToHtml());
    var outcome = await service.RefreshAsync(baseItem.Id);
    if (outcome != RefreshOutcome.Updated)
    {
        throw new InvalidOperationException($"取り直せなかった: {baseItem.Id} → {outcome}");
    }
}

async Task<int> ShowAsync()
{
    // アプリと同じ読み方で全部読む（読めない物があればここで分かる）
    var loaded = await store.Items.LoadAllAsync();
    var ids = store.Items.EnumerateItemIds();
    var unresolved = store.Unresolved.Load();
    var settings = Settings();
    var tags = store.UserTags.Load();
    Console.WriteLine($"商品 {ids.Count} 件（BOOTHに無い商品 {ids.Count(LocalItemId.IsLocal)} 件）・読めた {loaded.Items.Count} 件");
    Console.WriteLine($"未確定 {unresolved.Count} 件（壊れた zip {unresolved.Count(file => file.ArchiveBroken)} 件）");
    Console.WriteLine($"ユーザータグの大分類 {tags.Tops.Count} 個");
    Console.WriteLine($"取り込み元 {settings.ImportFolders.Count} 個・監視 {settings.WatchedFolders.Count} 個"
        + $"・使っていない間の取得 {(settings.ResumeFetchInBackground ? "入" : "切")}・起動時の取り込み {(settings.StartImportOnLaunch ? "入" : "切")}");
    return 0;
}

/// <summary>
/// つながらない相手。何を頼まれても「BOOTH から応答が無い」と答え、呼ばれた回数を数える。
/// 作り物の写しを組むのに BOOTH へ問い合わせないための物（問い合わせは1本ずつ・1.5秒以上、の決め事の外に出ない）。
/// </summary>
internal sealed class OfflineClient : IBoothClient
{
    private int _calls;

    public int Calls => _calls;

    /// <summary>商品の番号ごとに、返す商品JSONとHTML（changes の手順だけが置く。台本に無い商品は断る）。</summary>
    public Dictionary<string, (string Json, string Html)> Script { get; } = [];

#pragma warning disable CS0067 // 取得をしないので、知らせる物が無い
    public event Action<BoothActivity>? ActivityChanged;
#pragma warning restore CS0067

    public int CurrentIntervalMs => 1500;

    public bool IsThrottled => false;

    private Task<BoothFetchResult<T>> Refuse<T>()
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(BoothFetchResult<T>.Unreachable("写しを組む道具は BOOTH へ問い合わせない"));
    }

    public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        => Script.TryGetValue(itemId, out var scripted) ? Task.FromResult(BoothFetchResult<string>.Success(scripted.Json)) : Refuse<string>();

    public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
        => Script.TryGetValue(itemId, out var scripted) ? Task.FromResult(BoothFetchResult<string>.Success(scripted.Html)) : Refuse<string>();

    public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default) => Refuse<byte[]>();

    public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default) => Refuse<string>();

    public Task<BoothFetchResult<string>> GetTextUntilAsync(
        string url, Func<string, bool> found, int maxBytes = 262144, CancellationToken cancellationToken = default) => Refuse<string>();
}
