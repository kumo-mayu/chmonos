using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 共通素体の中のアバターの並び（ユーザ指示 2026-11-04）。商品ページ・素体の詳細・検索の候補が同じ関数で並べる。
/// </summary>
public class AvatarBaseOrderTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarBaseOrderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-baseorder-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new AvatarService(_store);
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

    private sealed record Member(string Id, string Name, bool Owned);

    private static readonly Member[] Members =
    [
        new("1", "ユウ", false),
        new("2", "ミナト", true),
        new("3", "カイ", false),
        new("4", "アオイ", true),
    ];

    private static string[] Arrange(IReadOnlyList<string>? order)
        => [.. AvatarBaseOrder.Arrange(order, Members, member => member.Id, member => member.Owned, member => member.Name).Select(member => member.Name)];

    [Fact]
    public void 並べていなければ_持っている物が先で名前の順()
        => Assert.Equal(["アオイ", "ミナト", "カイ", "ユウ"], Arrange(null));

    /// <summary>並べた後に入った仲間は後ろへ、既定の順で。今は一員でない ID は無視する。</summary>
    [Fact]
    public void 並べた順が先で_並べていない仲間は後ろ_一員でないIDは無視する()
        => Assert.Equal(["ユウ", "カイ", "アオイ", "ミナト"], Arrange(["1", "9", "3"]));

    [Fact]
    public async Task 並びを保存し_nullで並べていない状態に戻すと書き出さない()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }] });

        await _service.SetBaseMemberOrderAsync("作り物素体", ["3", "1", "3"]);
        Assert.Equal(["3", "1"], (await _store.Avatars.LoadAsync()).BaseGroups.Single().MemberOrder);

        await _service.SetBaseMemberOrderAsync("作り物素体", null);
        Assert.Null((await _store.Avatars.LoadAsync()).BaseGroups.Single().MemberOrder);
        Assert.DoesNotContain("memberOrder", await File.ReadAllTextAsync(Path.Combine(_root, "avatar-registry.json")));
    }

    /// <summary>並びは人が決めた値。名前を変えても残る（グループは with で名前だけ替える）。</summary>
    [Fact]
    public async Task 名前を変えても並びは残る()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "作り物素体", MemberOrder = ["3", "1"] }] });

        await _service.RenameBaseAsync("作り物素体", "新しい素体");

        Assert.Equal(["3", "1"], (await _store.Avatars.LoadAsync()).BaseGroups.Single().MemberOrder);
    }
}
