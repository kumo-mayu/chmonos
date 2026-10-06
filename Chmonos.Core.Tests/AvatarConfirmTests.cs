using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 確認待ちの対応アバターを確認済みにする（ユーザ判断 2026-10-06・メモ83）。
/// 前は手で足す・消した物を戻すときしか確認済みにならず、検出の推定を「合っている」と決める道が無かった。
/// </summary>
public class AvatarConfirmTests : IDisposable
{
    private const string ItemId = "9900001";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly EditService _service;

    public AvatarConfirmTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-confirm-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new EditService(_store);
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

    private static AvatarLink Guess(string id) => new()
    {
        AvatarItemId = id,
        Name = "アバター" + id,
        Source = AvatarLinkSource.H2Link,
        Confirmed = false,
    };

    private async Task SaveAsync(LocalBlock local)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = local,
        });

    private async Task<IReadOnlyList<AvatarLink>> LinksAsync()
        => (await _store.Items.LoadAsync(ItemId))!.Local.Avatars;

    [Fact]
    public async Task 名指しした確認待ちだけを確認済みにする()
    {
        await SaveAsync(new LocalBlock { Avatars = [Guess("9900011"), Guess("9900012")] });

        Assert.True(await _service.ConfirmAvatarsAsync(ItemId, ["9900011"]));

        var links = await LinksAsync();
        Assert.True(links.Single(link => link.AvatarItemId == "9900011").Confirmed);
        Assert.False(links.Single(link => link.AvatarItemId == "9900012").Confirmed);
    }

    /// <summary>出どころを手入力にしないと、検出し直し（Manual 以外を作り直す）で確認待ちに戻る。</summary>
    [Fact]
    public async Task 確認済みにした行は手入力になり次の検出で戻らない()
    {
        await SaveAsync(new LocalBlock { Avatars = [Guess("9900011")] });

        await _service.ConfirmAvatarsAsync(ItemId, ["9900011"]);

        Assert.Equal(AvatarLinkSource.Manual, (await LinksAsync())[0].Source);
    }

    /// <summary>「すべて確認済みにする」は確認待ちの全部を渡す。消した行は消したまま（戻すのは「消したもの」の［戻す］）。</summary>
    [Fact]
    public async Task すべて確認済みにしても消した行は戻らない()
    {
        var rejected = Guess("9900013") with { Source = AvatarLinkSource.Manual, Rejected = true };
        await SaveAsync(new LocalBlock { Avatars = [Guess("9900011"), Guess("9900012"), rejected] });

        await _service.ConfirmAvatarsAsync(ItemId, ["9900011", "9900012", "9900013"]);

        var links = await LinksAsync();
        Assert.All(links.Where(link => !link.Rejected), link => Assert.True(link.Confirmed));
        var kept = links.Single(link => link.AvatarItemId == "9900013");
        Assert.True(kept.Rejected);
        Assert.False(kept.Confirmed);
    }

    /// <summary>
    /// 画面が開いた時点の写しを書き戻さない：画面が読んだ後に、裏の検出が対応アバターを足し、
    /// 別の所でメモが書かれても、確認済みにするのはその1行だけで、ほかは今の記録のまま残る。
    /// </summary>
    [Fact]
    public async Task 画面が読んだ後に書かれた欄と行を巻き戻さない()
    {
        await SaveAsync(new LocalBlock { Avatars = [Guess("9900011")] });

        // 画面が読んだ後に、ほかの書き手が行を足し、メモを書いた
        await SaveAsync(new LocalBlock { Memo = "あとから書いたメモ", Avatars = [Guess("9900011"), Guess("9900012")] });

        await _service.ConfirmAvatarsAsync(ItemId, ["9900011"]);

        var saved = (await _store.Items.LoadAsync(ItemId))!;
        Assert.Equal("あとから書いたメモ", saved.Local.Memo);
        Assert.Equal(["9900011", "9900012"], saved.Local.Avatars.Select(link => link.AvatarItemId));
        Assert.False(saved.Local.Avatars[1].Confirmed);
    }

    /// <summary>錠の取り合い：確認済みにするのと、別の書き手が同じ一覧を変えるのが重なっても、どちらの変更も残る。</summary>
    [Fact]
    public async Task 同じ一覧を同時に変えても両方の変更が残る()
    {
        await SaveAsync(new LocalBlock { Avatars = [Guess("9900011")] });

        var confirm = _service.ConfirmAvatarsAsync(ItemId, ["9900011"]);
        var add = _store.Items.ChangeLocalAsync(
            ItemId,
            local => local with { Avatars = [.. local.Avatars, Guess("9900012")] },
            LocalOwners.SupportedAvatars);
        await Task.WhenAll(confirm, add);

        var links = await LinksAsync();
        Assert.Equal(2, links.Count);
        Assert.True(links.Single(link => link.AvatarItemId == "9900011").Confirmed);
    }

    [Fact]
    public async Task 変える行が無ければ書かない()
    {
        await SaveAsync(new LocalBlock { Avatars = [Guess("9900011") with { Confirmed = true }] });
        var path = _store.Paths.ItemFile(ItemId);
        var before = File.GetLastWriteTimeUtc(path);

        Assert.True(await _service.ConfirmAvatarsAsync(ItemId, ["9900011"]));

        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task 商品が無ければ失敗を返す()
    {
        var handler = new CommandHandler(null!, null!, _service);

        var result = await handler.ExecuteAsync(new UiCommand.ConfirmAvatars("9900099", ["9900011"]));

        Assert.IsType<CommandResult.Failed>(result);
    }
}
