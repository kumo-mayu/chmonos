using System.IO.Compression;
using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 読めない商品の記録を知らせ、控えに戻す・BOOTH から作り直す（ユーザ判断 2026-10-05「前提と 1 と 3 の案」）。
///
/// 読めない記録は全件の読み込みが飛ばすので、前はその商品が黙って検索から消えていた。
/// どの道でも壊れた記録は消さずに items/_broken へよける。BOOTH は作り物（<see cref="Handler"/>）。
/// </summary>
public class BrokenItemRecordTests : IDisposable
{
    private const string ItemId = "9900001";
    private const string OtherId = "9900002";

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly Handler _booth = new();
    private readonly ImportPipeline _pipeline;
    private readonly NotificationService _notifications;
    private readonly CommandHandler _commands;

    public BrokenItemRecordTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chmonos-broken-item-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(_booth), settings, TestWait.None);
        var images = new ImagePipeline(client, _paths, settings);
        var items = new ItemService(_store, client, images);
        _pipeline = new ImportPipeline(_store, client, images, settings);
        _notifications = new NotificationService(_store, settings);
        _commands = new CommandHandler(
            _pipeline, items, notifications: _notifications, brokenItems: new BrokenItemRepair(_store, items, _notifications));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>教えた商品IDだけに答える作り物の BOOTH（ほかは 404）。商品ページは空。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        public HashSet<string> Known { get; } = [];

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var url = request.RequestUri!.ToString();
            var id = Known.FirstOrDefault(known => url.Contains(known, StringComparison.Ordinal));
            if (id is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (!url.EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {
                      "id": {{id}},
                      "name": "作り直した商品",
                      "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                      "images": [],
                      "variations": [ { "id": 1, "name": null, "price": 100 } ]
                    }
                    """),
            });
        }
    }

    private static ItemRecord Item(string id, string memo) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "作り物の商品", FetchedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) },
        Local = new LocalBlock { Memo = memo },
    };

    /// <summary>手で直して壊した形：3行目で括弧が閉じていない。</summary>
    private void Break(string id)
        => File.WriteAllText(_paths.ItemFile(id), "{\n  \"id\": \"" + id + "\",\n  \"booth\": {\n");

    private string[] BrokenFiles()
        => Directory.Exists(_paths.BrokenItemsDir) ? Directory.GetFiles(_paths.BrokenItemsDir) : [];

    private NotificationRecord? NoticeOf(string id)
        => _store.Notifications.Load().SingleOrDefault(record => record.Id == NotificationService.UnreadableItemPrefix + id);

    // ---- 控え ----

    [Fact]
    public async Task 商品の記録を書くたびに_書いた版が控えに残る()
    {
        await _store.Items.SaveAsync(Item(ItemId, "1回目"));
        Assert.Equal(File.ReadAllText(_paths.ItemFile(ItemId)), File.ReadAllText(_paths.ItemCopyFile(ItemId)));

        await _store.Items.SaveLocalAsync(ItemId, Item(ItemId, "2回目").Local, [LocalField.Memo]);
        Assert.Contains("2回目", File.ReadAllText(_paths.ItemCopyFile(ItemId)));
        Assert.True(_store.Items.HasUsableCopy(ItemId));
    }

    [Fact]
    public async Task 控えとよけた記録は_全件の読み込みに入らない()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));
        await _store.Items.SaveAsync(Item(OtherId, "メモ"));
        Break(OtherId);
        Assert.Equal(BrokenItemOutcome.Done, (await _store.Items.SetAsideBrokenAsync(OtherId)).Outcome);

        var loaded = await _store.Items.LoadAllAsync();

        Assert.Equal([ItemId], loaded.Items.Select(item => item.Id));
        Assert.Empty(loaded.FailedItemIds);
        Assert.Equal([ItemId], _store.Items.EnumerateItemIds());
    }

    [Fact]
    public async Task 商品を外すと_控えも消える()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));

        await _store.Items.DeleteAsync(ItemId);

        Assert.False(File.Exists(_paths.ItemCopyFile(ItemId)));
    }

    // ---- 知らせる ----

    [Fact]
    public async Task 控えが無いか読めないときだけ_題は商品IDのファイル名になる()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));
        await _store.Items.SaveAsync(Item(OtherId, "メモ"));
        Break(ItemId);
        Break(OtherId);
        File.Delete(_paths.ItemCopyFile(ItemId));
        await File.WriteAllTextAsync(_paths.ItemCopyFile(OtherId), "{ 壊れた控え");

        await _notifications.DetectUnreadableItemsAsync();

        Assert.Equal($"{ItemId}.json", NoticeOf(ItemId)!.Title);
        Assert.Equal($"{OtherId}.json", NoticeOf(OtherId)!.Title);
        Assert.Empty(NoticeOf(ItemId)!.Diffs);
    }

    [Fact]
    public async Task 表示名があれば_題は表示名になる()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ") with { Local = new LocalBlock { Memo = "メモ", DisplayName = "作り物の呼び名" } });
        Break(ItemId);

        await _notifications.DetectUnreadableItemsAsync();

        Assert.Equal("作り物の呼び名", NoticeOf(ItemId)!.Title);
    }

    [Fact]
    public async Task 壊れた記録は_何行目かを添えて1商品1件の知らせになり_直ると解消済みになる()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));
        await _store.Items.SaveAsync(Item(OtherId, "メモ"));
        Break(ItemId);

        Assert.Equal(1, await _notifications.DetectUnreadableItemsAsync());

        var notice = NoticeOf(ItemId);
        Assert.NotNull(notice);
        Assert.Equal(NotificationKind.UnreadableItem, notice!.Kind);
        Assert.Equal("作り物の商品", notice.Title);
        Assert.Equal(["壊れている場所", "ファイル"], notice.Diffs.Select(diff => diff.Field));
        Assert.Equal($"{ItemId}.json", notice.Diffs[1].After);
        Assert.DoesNotContain(ItemId, notice.Title);
        Assert.Equal("壊れている場所：4 行目", notice.Detail);
        Assert.Null(NoticeOf(OtherId));

        // 同じ所で読めないままなら出し直さない（既読にした物を未読に戻さない）
        await _notifications.SetReadAsync(notice.Id, isRead: true);
        Assert.Equal(0, await _notifications.DetectUnreadableItemsAsync());
        Assert.True(NoticeOf(ItemId)!.IsRead);

        // 手で直したら解消済み
        await File.WriteAllTextAsync(_paths.ItemFile(ItemId), File.ReadAllText(_paths.ItemCopyFile(ItemId)));
        await _notifications.DetectUnreadableItemsAsync();
        Assert.True(NoticeOf(ItemId)!.IsResolved);
    }

    [Fact]
    public async Task 中身がnullだけの記録も_読めない記録として数える()
    {
        await File.WriteAllTextAsync(_paths.ItemFile(ItemId), "null");

        await _notifications.DetectUnreadableItemsAsync();

        Assert.Equal("壊れている内容：中身が空です", NoticeOf(ItemId)!.Detail);
        Assert.Equal([ItemId], (await _store.Items.LoadAllAsync()).FailedItemIds);
    }

    // ---- ① 1つ前の版に戻す ----

    [Fact]
    public async Task 一つ前の版に戻すと_控えが本体になり_壊れた記録はbrokenに残る()
    {
        await _store.Items.SaveAsync(Item(ItemId, "残したいメモ"));
        Break(ItemId);
        var broken = File.ReadAllText(_paths.ItemFile(ItemId));
        await _notifications.DetectUnreadableItemsAsync();

        var result = await _commands.ExecuteAsync(new UiCommand.RestoreItemCopy(ItemId));

        Assert.Equal(new CommandResult.ItemSaved(ItemId), result);
        Assert.Equal("残したいメモ", (await _store.Items.LoadAsync(ItemId))!.Local.Memo);
        var kept = Assert.Single(BrokenFiles());
        Assert.StartsWith(ItemId + "-", Path.GetFileName(kept));
        Assert.Equal(broken, File.ReadAllText(kept));
        Assert.True(NoticeOf(ItemId)!.IsResolved);
        Assert.Equal(0, _booth.Requests);
    }

    [Fact]
    public async Task 控えが無いと_一つ前の版には戻さず_本体もそのまま()
    {
        Break(ItemId);

        var result = await _commands.ExecuteAsync(new UiCommand.RestoreItemCopy(ItemId));

        Assert.Equal(new CommandResult.Failed("1つ前の版がありません。BOOTHから作り直してください。"), result);
        Assert.False(_store.Items.HasUsableCopy(ItemId));
        Assert.True(File.Exists(_paths.ItemFile(ItemId)));
        Assert.Empty(BrokenFiles());
    }

    [Fact]
    public async Task 控えも読めないと_戻さずに理由を言う()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));
        Break(ItemId);
        await File.WriteAllTextAsync(_paths.ItemCopyFile(ItemId), "{ 壊れた控え");

        var result = await _commands.ExecuteAsync(new UiCommand.RestoreItemCopy(ItemId));

        Assert.Equal(new CommandResult.Failed("1つ前の版も壊れていて戻せません。BOOTHから作り直してください。"), result);
        Assert.False(_store.Items.HasUsableCopy(ItemId));
        Assert.Empty(BrokenFiles());
    }

    [Fact]
    public async Task 押すまでに手で直していれば_何も動かさない()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));

        var result = await _commands.ExecuteAsync(new UiCommand.RestoreItemCopy(ItemId));

        Assert.Equal(new CommandResult.Failed("この記録はもう読めるようになっています。"), result);
        Assert.Empty(BrokenFiles());
    }

    // ---- ③ BOOTH から作り直す ----

    [Fact]
    public async Task BOOTHから作り直すと_取り直した情報で新しく作り_壊れた記録はbrokenに残る()
    {
        _booth.Known.Add(ItemId);
        Break(ItemId);
        await _notifications.DetectUnreadableItemsAsync();

        var result = await _commands.ExecuteAsync(new UiCommand.RebuildItemFromBooth(ItemId));

        Assert.Equal(new CommandResult.ItemSaved(ItemId), result);
        var rebuilt = await _store.Items.LoadAsync(ItemId);
        Assert.Equal("作り直した商品", rebuilt!.Booth.Name);
        Assert.Empty(rebuilt.Local.LocalFiles);
        Assert.Single(BrokenFiles());
        Assert.True(NoticeOf(ItemId)!.IsResolved);
        Assert.True(_store.Items.HasUsableCopy(ItemId));
    }

    [Fact]
    public async Task BOOTHに見つからなければ_壊れた記録を元の場所に戻す()
    {
        Break(ItemId);
        var broken = File.ReadAllText(_paths.ItemFile(ItemId));

        var result = await _commands.ExecuteAsync(new UiCommand.RebuildItemFromBooth(ItemId));

        Assert.IsType<CommandResult.Failed>(result);
        Assert.Equal(broken, File.ReadAllText(_paths.ItemFile(ItemId)));
        Assert.Empty(BrokenFiles());
    }

    [Fact]
    public async Task BOOTHに無い商品は_問い合わせずに作り直せないと言う()
    {
        const string localId = "local-9900003";
        Break(localId);

        var result = await _commands.ExecuteAsync(new UiCommand.RebuildItemFromBooth(localId));

        Assert.Equal(
            new CommandResult.Failed("BOOTHに無い商品として登録したものなので、作り直せません。JSONを開いて直してください。"),
            result);
        Assert.Equal(0, _booth.Requests);
        Assert.True(File.Exists(_paths.ItemFile(localId)));
    }

    /// <summary>
    /// 作り直した記録には手元のファイルが無い。ダウンロード元の記録で商品が決まるファイルは、取り込み直すと付き直し、
    /// 決まらないファイルは未確定に出る（今の取り込みの作りのまま。確認の窓の文はこれを言っている）。
    /// </summary>
    [Fact]
    public async Task 作り直した後に取り込み直すと_手掛かりのあるファイルは付き直し_無いファイルは未確定に出る()
    {
        _booth.Known.Add(ItemId);
        var marked = Zip("marked.zip", ItemId);
        var plain = Zip("plain.zip", itemId: null);
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));
        await _store.Items.ChangeLocalAsync(
            ItemId,
            local => local with { LocalFiles = [FileRecord(marked), FileRecord(plain)] },
            [LocalField.LocalFiles]);
        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        Break(ItemId);

        Assert.Equal(new CommandResult.ItemSaved(ItemId), await _commands.ExecuteAsync(new UiCommand.RebuildItemFromBooth(ItemId)));
        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        var files = (await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles;
        Assert.Equal([marked], files.SelectMany(file => file.Paths));
        Assert.Equal([plain], _store.Unresolved.Load().SelectMany(file => file.Paths));
    }

    private string Zip(string name, string? itemId)
    {
        var path = Path.Combine(_source, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry(name + ".txt").Open();
            stream.Write(System.Text.Encoding.UTF8.GetBytes(name));
        }

        if (itemId is not null)
        {
            File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }

        return path;
    }

    private static LocalFileRecord FileRecord(string path) => new()
    {
        Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
        Paths = [path],
        SizeBytes = new FileInfo(path).Length,
    };

    // ---- バックアップ ----

    [Fact]
    public async Task バックアップには_よけた記録を入れ_控えは入れない()
    {
        await _store.Items.SaveAsync(Item(ItemId, "メモ"));
        await _store.Items.SaveAsync(Item(OtherId, "メモ"));
        Break(OtherId);
        await _store.Items.SetAsideBrokenAsync(OtherId);
        var zip = Path.Combine(_root, "backup.zip");

        BackupArchive.Export(_paths.Root, zip, includeImages: false);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        Assert.Contains($"items/{ItemId}.json", names);
        Assert.Contains(names, name => name.StartsWith("items/_broken/" + OtherId + "-", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("items/.prev/", StringComparison.Ordinal));
    }
}
