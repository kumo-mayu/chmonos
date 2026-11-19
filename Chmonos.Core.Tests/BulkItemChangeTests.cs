using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 選択の帯からまとめて当てる変更（ユーザ判断 2026-11-20）。商品ごとに錠の中で今の値に当て、
/// 既にそうなっている商品は書かずに数える。
/// </summary>
public class BulkItemChangeTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly CommandHandler _commands;

    public BulkItemChangeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-bulk-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _commands = new CommandHandler(null!, null!, new EditService(_store));
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

    private Task SaveAsync(string id, LocalBlock local) => _store.Items.SaveAsync(new ItemRecord
    {
        Id = id,
        Booth = new BoothBlock { FetchedAt = DateTimeOffset.UnixEpoch, Name = "作り物の商品" },
        Local = local,
    });

    private async Task<LocalBlock> LocalOfAsync(string id) => (await _store.Items.LoadAsync(id))!.Local;

    private async Task<BulkItemChangeOutcome> RunAsync(BulkItemChange change, params string[] ids)
        => Assert.IsType<CommandResult.ItemsChanged>(await _commands.ExecuteAsync(new UiCommand.ChangeItems(ids, change))).Outcome;

    [Fact]
    public async Task タグを足すと今のタグに足し_既にある商品は数えるだけ()
    {
        await SaveAsync("9900901", new LocalBlock { UserTags = [new UserTagAssignment { Top = "衣装" }], Memo = "残す" });
        await SaveAsync("9900902", new LocalBlock { UserTags = [new UserTagAssignment { Top = "髪型" }] });

        var outcome = await RunAsync(new BulkItemChange.AddUserTag("髪型"), "9900901", "9900902", "9900999");

        Assert.Equal(new BulkItemChangeOutcome(Changed: 1, AlreadyDone: 1, NotFound: 1), outcome);
        var first = await LocalOfAsync("9900901");
        Assert.Equal(["衣装", "髪型"], first.UserTags.Select(tag => tag.Top));
        Assert.Equal("残す", first.Memo);
    }

    [Fact]
    public async Task 小分類を足すと大分類も付き_外すと小分類だけを外す()
    {
        await SaveAsync("9900901", new LocalBlock());
        await SaveAsync("9900902", new LocalBlock { UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["ワンピース"] }] });

        Assert.Equal(new BulkItemChangeOutcome(1, 1, 0), await RunAsync(new BulkItemChange.AddUserTag("衣装", "ワンピース"), "9900901", "9900902"));
        Assert.Equal(["ワンピース"], Assert.Single((await LocalOfAsync("9900901")).UserTags).Subs);

        Assert.Equal(new BulkItemChangeOutcome(2, 0, 0), await RunAsync(new BulkItemChange.RemoveUserTag("衣装", "ワンピース"), "9900901", "9900902"));
        var kept = Assert.Single((await LocalOfAsync("9900902")).UserTags);
        Assert.Equal(("衣装", 0), (kept.Top, kept.Subs.Count));
    }

    [Fact]
    public async Task 大分類を外すと小分類ごと外し_付いていない商品は数えるだけ()
    {
        await SaveAsync("9900901", new LocalBlock { UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["ワンピース"] }, new UserTagAssignment { Top = "髪型" }] });
        await SaveAsync("9900902", new LocalBlock());

        Assert.Equal(new BulkItemChangeOutcome(1, 1, 0), await RunAsync(new BulkItemChange.RemoveUserTag("衣装"), "9900901", "9900902"));
        Assert.Equal(["髪型"], (await LocalOfAsync("9900901")).UserTags.Select(tag => tag.Top));
    }

    /// <summary>属性を外すとキーを消して未評価に戻す（0 にはしない。未評価と 0 は別）。</summary>
    [Fact]
    public async Task 属性を外すと未評価に戻し_ほかの属性は残す()
    {
        await SaveAsync("9900901", new LocalBlock { Attributes = new Dictionary<string, int> { ["かわいさ"] = 0, ["露出"] = 40 } });
        await SaveAsync("9900902", new LocalBlock());

        Assert.Equal(new BulkItemChangeOutcome(1, 1, 0), await RunAsync(new BulkItemChange.RemoveAttribute("かわいさ"), "9900901", "9900902"));
        var attributes = (await LocalOfAsync("9900901")).Attributes;
        Assert.False(attributes.ContainsKey("かわいさ"));
        Assert.Equal(40, attributes["露出"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task お気に入り_非表示_更新の通知は名指しした欄だけを書く(bool value)
    {
        await SaveAsync("9900901", new LocalBlock { IsFavorite = !value, IsHidden = !value, NotifyOnUpdate = !value, Memo = "残す" });

        Assert.Equal(new BulkItemChangeOutcome(1, 0, 0), await RunAsync(new BulkItemChange.SetFavorite(value), "9900901"));
        Assert.Equal(new BulkItemChangeOutcome(1, 0, 0), await RunAsync(new BulkItemChange.SetHidden(value), "9900901"));
        Assert.Equal(new BulkItemChangeOutcome(1, 0, 0), await RunAsync(new BulkItemChange.SetNotifyOnUpdate(value), "9900901"));
        Assert.Equal(new BulkItemChangeOutcome(0, 1, 0), await RunAsync(new BulkItemChange.SetHidden(value), "9900901"));

        var local = await LocalOfAsync("9900901");
        Assert.Equal((value, value, value, "残す"), (local.IsFavorite, local.IsHidden, local.NotifyOnUpdate, local.Memo));
    }

    /// <summary>確認待ちだけを確認済みにし、外した行は外したままにする（外した／確認待ちの区別を崩さない）。</summary>
    [Fact]
    public async Task 対応アバターは確認待ちだけを確認済みにし_外した行は触らない()
    {
        await SaveAsync("9900901", new LocalBlock
        {
            Avatars =
            [
                new AvatarLink { AvatarItemId = "9900811", Source = AvatarLinkSource.Tag },
                new AvatarLink { AvatarItemId = "9900812", Source = AvatarLinkSource.Tag, Rejected = true },
            ],
        });
        await SaveAsync("9900902", new LocalBlock());

        Assert.Equal(new BulkItemChangeOutcome(1, 1, 0), await RunAsync(new BulkItemChange.ConfirmAvatars(), "9900901", "9900902"));
        var avatars = (await LocalOfAsync("9900901")).Avatars;
        Assert.True(avatars.Single(link => link.AvatarItemId == "9900811").Confirmed);
        Assert.Equal((false, true), (avatars.Single(link => link.AvatarItemId == "9900812").Confirmed, avatars.Single(link => link.AvatarItemId == "9900812").Rejected));
    }

    /// <summary>既にそうなっている商品は書かない（ファイルの日時が変わらない）。</summary>
    [Fact]
    public async Task 既にある物を足しても書かない()
    {
        await SaveAsync("9900901", new LocalBlock { UserTags = [new UserTagAssignment { Top = "衣装" }] });
        var path = Directory.EnumerateFiles(_root, "*9900901*.json", SearchOption.AllDirectories).First();
        var before = File.GetLastWriteTimeUtc(path);
        await Task.Delay(30);

        await RunAsync(new BulkItemChange.AddUserTag("衣装"), "9900901");

        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }
}
