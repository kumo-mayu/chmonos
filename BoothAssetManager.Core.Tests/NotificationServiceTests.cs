using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class NotificationServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;

    public NotificationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-notify-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
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
    }

    private NotificationService Create(int retention = 200)
        => new(_store, new AppSettings { NotificationRetentionCount = retention });

    private static NotificationRecord Record(string id, bool isRead = false, int ageDays = 0) => new()
    {
        Id = id,
        Kind = NotificationKind.ItemUpdated,
        Title = id,
        Detail = "詳細",
        CreatedAt = DateTimeOffset.Now.AddDays(-ageDays),
        IsRead = isRead,
    };

    [Fact]
    public async Task MarksOneAsRead()
    {
        await _store.Notifications.SaveAsync([Record("a"), Record("b")]);

        Assert.True(await Create().SetReadAsync("a", true));

        var saved = _store.Notifications.Load();
        Assert.True(saved.Single(record => record.Id == "a").IsRead);
        Assert.False(saved.Single(record => record.Id == "b").IsRead);
    }

    /// <summary>既読は消さずに残す。見たことと無かったことは別なので。</summary>
    [Fact]
    public async Task KeepsReadRecordsInsteadOfDeleting()
    {
        await _store.Notifications.SaveAsync([Record("a")]);

        await Create().SetReadAsync("a", true);

        Assert.Single(_store.Notifications.Load());
    }

    [Fact]
    public async Task CanPutOneBackToUnread()
    {
        await _store.Notifications.SaveAsync([Record("a", isRead: true)]);

        Assert.True(await Create().SetReadAsync("a", false));
        Assert.False(_store.Notifications.Load()[0].IsRead);
    }

    [Fact]
    public async Task ReportsNoChangeWhenAlreadyInThatState()
    {
        await _store.Notifications.SaveAsync([Record("a", isRead: true)]);

        Assert.False(await Create().SetReadAsync("a", true));
        Assert.False(await Create().SetReadAsync("missing", true));
    }

    [Fact]
    public async Task MarksEveryUnreadAsRead()
    {
        await _store.Notifications.SaveAsync([Record("a"), Record("b", isRead: true), Record("c")]);

        Assert.Equal(2, await Create().MarkAllReadAsync());
        Assert.All(_store.Notifications.Load(), record => Assert.True(record.IsRead));
    }

    /// <summary>
    /// 上限を超えたら古い既読から捨てる。未読は捨てない
    /// （未読を落とすと、気付かないまま消えたことにも気付けない）。
    /// </summary>
    [Fact]
    public async Task DropsTheOldestReadRecordsWhenOverTheLimit()
    {
        await _store.Notifications.SaveAsync(
        [
            Record("old-read", isRead: true, ageDays: 30),
            Record("new-read", isRead: true, ageDays: 1),
            Record("unread", ageDays: 60),
            Record("target"),
        ]);

        await Create(retention: 3).SetReadAsync("target", true);

        var saved = _store.Notifications.Load();
        Assert.Equal(3, saved.Count);
        Assert.DoesNotContain(saved, record => record.Id == "old-read");
        Assert.Contains(saved, record => record.Id == "unread");
    }

    [Fact]
    public async Task KeepsUnreadEvenWhenOverTheLimit()
    {
        await _store.Notifications.SaveAsync(
        [
            Record("u1", ageDays: 90),
            Record("u2", ageDays: 80),
            Record("u3", ageDays: 70),
            Record("target"),
        ]);

        await Create(retention: 1).SetReadAsync("target", true);

        // 捨てられる既読が無いので、上限を割り込んでも未読は残る
        Assert.Equal(3, _store.Notifications.Load().Count(record => !record.IsRead));
    }

    /// <summary>マスタから消えた分類をitemが参照したままなら知らせる。</summary>
    [Fact]
    public async Task DetectsItemsReferencingMissingTagsAndAttributes()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [new UserTagTop { Name = "衣装" }] });
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "かわいい" }] });

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "参照が壊れたitem", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                UserTags = [new UserTagAssignment { Top = "ギミック" }],
                Attributes = new Dictionary<string, int> { ["メカ"] = 50 },
            },
        });

        Assert.Equal(1, await Create().DetectOrphanReferencesAsync());

        var record = Assert.Single(_store.Notifications.Load());
        Assert.Equal(NotificationKind.OrphanTag, record.Kind);
        Assert.Contains("ギミック", record.Detail);
        Assert.Contains("メカ", record.Detail);
    }

    /// <summary>サブ名のずれも知らせる。トップだけ見ていると、この食い違いに気付けない。</summary>
    [Fact]
    public async Task DetectsItemsReferencingMissingSubLevels()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] }],
        });

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["制服", "消えたサブ"] }],
            },
        });

        Assert.Equal(1, await Create().DetectOrphanReferencesAsync());
        Assert.Contains("衣装／消えたサブ", Assert.Single(_store.Notifications.Load()).Detail);
    }

    [Fact]
    public async Task DetectsNothingWhenEveryReferenceResolves()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [new UserTagTop { Name = "衣装" }] });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "健全なitem", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { UserTags = [new UserTagAssignment { Top = "衣装" }] },
        });

        Assert.Equal(0, await Create().DetectOrphanReferencesAsync());
    }

    /// <summary>同じitemで既に出していれば重ねて出さない。開くたびに増えては読めない。</summary>
    [Fact]
    public async Task DoesNotPileUpTheSameOrphanNotice()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { UserTags = [new UserTagAssignment { Top = "無いタグ" }] },
        });

        var service = Create();
        await service.DetectOrphanReferencesAsync();
        await service.DetectOrphanReferencesAsync();

        Assert.Single(_store.Notifications.Load());
    }

    /// <summary>
    /// 既読にしても作り直さない。直さないまま画面を開くたびに
    /// 同じ話が積み上がると、既読が何の意味も持たなくなる。
    /// </summary>
    [Fact]
    public async Task DoesNotReRaiseAnOrphanNoticeThatWasAlreadyRead()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { UserTags = [new UserTagAssignment { Top = "無いタグ" }] },
        });

        var service = Create();
        await service.DetectOrphanReferencesAsync();
        await service.SetReadAsync("orphan-tag:1", true);

        Assert.Equal(0, await service.DetectOrphanReferencesAsync());

        var record = Assert.Single(_store.Notifications.Load());
        Assert.True(record.IsRead);
    }

    /// <summary>参照している名前が変われば別の話なので、未読にして出し直す。</summary>
    [Fact]
    public async Task RaisesAgainWhenTheMissingNamesChanged()
    {
        var item = new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { UserTags = [new UserTagAssignment { Top = "無いタグ" }] },
        };
        await _store.Items.SaveAsync(item);

        var service = Create();
        await service.DetectOrphanReferencesAsync();
        await service.SetReadAsync("orphan-tag:1", true);

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = item.Id,
            Booth = item.Booth,
            Local = new LocalBlock { UserTags = [new UserTagAssignment { Top = "別の無いタグ" }] },
        });

        Assert.Equal(1, await service.DetectOrphanReferencesAsync());

        var record = Assert.Single(_store.Notifications.Load());
        Assert.False(record.IsRead);
        Assert.Contains("別の無いタグ", record.Detail);
    }

    /// <summary>
    /// 説明文はあるのに見出しが0件、という判定は**②（商品ページ）を取った商品だけ**で数える。
    /// 説明文は商品JSON（①）から入り、見出しはHTMLからしか入らないので、
    /// ①だけ済んだ商品はいつでも「読み取れなかった」ように見える。
    /// 取り込みを②の前で止めただけで「形式が変わったかもしれません」が出ていた（2026-09-22）。
    /// </summary>
    [Fact]
    public async Task DoesNotBlameTheFormatForItemsWhosePageIsNotFetchedYet()
    {
        foreach (var index in Enumerable.Range(0, 8))
        {
            await _store.Items.SaveAsync(new ItemRecord
            {
                Id = $"{index}",
                Booth = new BoothBlock
                {
                    Name = $"商品{index}",
                    FetchedAt = DateTimeOffset.Now,
                    Description = new string('あ', 300),
                },
            });
        }

        Assert.False(await Create().DetectPageStructureAsync());
        Assert.Empty(Create().Load());
    }

    /// <summary>②を取ってあるのに見出しが揃って0件なら、そのときは知らせる。</summary>
    [Fact]
    public async Task BlamesTheFormatOnceThePagesAreFetched()
    {
        foreach (var index in Enumerable.Range(0, 8))
        {
            await _store.Items.SaveAsync(new ItemRecord
            {
                Id = $"{index}",
                Booth = new BoothBlock
                {
                    Name = $"商品{index}",
                    FetchedAt = DateTimeOffset.Now,
                    Description = new string('あ', 300),
                },
            });

            // ②を通った印。説明の無い商品でも空のファイルを置くので、在ること自体が印になる
            await File.WriteAllTextAsync(_store.Paths.ItemHtmlFile($"{index}"), "<p>本文</p>");
        }

        Assert.True(await Create().DetectPageStructureAsync());
        Assert.Contains(Create().Load(), record => record.Kind == NotificationKind.PageStructureChanged);
    }
}
