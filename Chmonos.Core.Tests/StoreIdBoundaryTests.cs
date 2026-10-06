using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 手で直せる JSON に書かれた ID から、保存先の外の場所を組まない・消さない（2026-10-06 外部の点検・L106）。
/// 消す試験は、試験の保存先の**隣**に番兵のフォルダとファイルを置き、残ることを確かめる。
/// </summary>
public class StoreIdBoundaryTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    /// <summary>保存先（library）の隣に置く番兵。中のファイルが消えたら、保存先の外を消したということ</summary>
    private readonly string _sentinel;
    private readonly string _sentinelFile;

    public StoreIdBoundaryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-store-id-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        _sentinel = Path.Combine(_root, "sentinel");
        Directory.CreateDirectory(_sentinel);
        _sentinelFile = Path.Combine(_sentinel, "keep.txt");
        File.WriteAllText(_sentinelFile, "残っていること");
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

    private void AssertSentinelSurvives()
    {
        Assert.True(Directory.Exists(_sentinel), "保存先の隣の番兵のフォルダが消えた");
        Assert.True(File.Exists(_sentinelFile), "保存先の隣の番兵のファイルが消えた");
    }

    /// <summary>悪い値。番兵の絶対パス・上へ抜ける相対・ドライブ名・UNC・区切り・空</summary>
    public static TheoryData<string> BadIds()
    {
        var data = new TheoryData<string>
        {
            @"C:\Windows",
            "C:",
            "C:foo",
            @"\\server\share",
            "..",
            ".",
            @"..\sentinel",
            @"..\..\sentinel",
            "../sentinel",
            "9900001/../..",
            @"9900001\x",
            "",
            " 9900001",
            "9900001 ",
            "local-ZZZZ",
            "local-",
            "local-0a1b2c3d.exe",
            "mod-0a1b2c3d",
            "CON",
            "file:///C:/x",
        };
        return data;
    }

    [Theory]
    [InlineData("9900001")]
    [InlineData("local-0a1b2c3d")]
    public void 商品IDの形を受ける(string id) => Assert.True(StoreIds.IsItemId(id));

    [Theory]
    [MemberData(nameof(BadIds))]
    public void 商品IDの形でない値を受けない(string id) => Assert.False(StoreIds.IsItemId(id));

    [Fact]
    public void 改変IDの形を見る()
    {
        Assert.True(StoreIds.IsModificationId("mod-0a1b2c3d"));
        Assert.False(StoreIds.IsModificationId("9900001"));
        Assert.False(StoreIds.IsModificationId(@"mod-0a1b2c3d\..\.."));
        Assert.False(StoreIds.IsModificationId(Path.Combine(_root, "sentinel")));
        Assert.False(StoreIds.IsPackageHash(@"..\x"));
        Assert.True(StoreIds.IsPackageHash("ABCDEF0123"));
    }

    /// <summary>ID から場所を組む関数は全部、形の外れた値で投げる（AppPaths の一覧と1対1）</summary>
    [Theory]
    [MemberData(nameof(BadIds))]
    public void 形の外れたIDから場所を組まない(string id)
    {
        Assert.Throws<InvalidStoreIdException>(() => _paths.ItemFile(id));
        Assert.Throws<InvalidStoreIdException>(() => _paths.ItemCopyFile(id));
        Assert.Throws<InvalidStoreIdException>(() => _paths.ItemHtmlFile(id));
        Assert.Throws<InvalidStoreIdException>(() => _paths.ItemImagesDir(id));
        Assert.Throws<InvalidStoreIdException>(() => _paths.AvatarImagesDir(id));
        if (!StoreIds.IsModificationId(id))
        {
            Assert.Throws<InvalidStoreIdException>(() => _paths.ModificationFile(id));
            Assert.Throws<InvalidStoreIdException>(() => _paths.ModificationImagesDir(id));
        }

        if (!StoreIds.IsPackageHash(id))
        {
            Assert.Throws<InvalidStoreIdException>(() => _paths.UnityPackageFile(id));
        }
    }

    [Fact]
    public void 中かどうかは区切りを付けて比べる()
    {
        var images = _paths.ImagesDir;
        Assert.True(StoreIds.IsInside(Path.Combine(images, "9900001"), images));
        Assert.False(StoreIds.IsInside(images, images));
        Assert.False(StoreIds.IsInside(images + "-old", images));
        Assert.False(StoreIds.IsInside(Path.Combine(images, "..", "items"), images));
        Assert.False(StoreIds.IsInside(_sentinel, images));
    }

    /// <summary>
    /// 登録簿の ID に番兵の場所を書き、「持っている」に見えるよう番兵の隣に同じ名前の .json も置く。
    /// 前は持っていないアバターの画像の片付けが、番兵のフォルダを丸ごと消していた
    /// </summary>
    [Fact]
    public async Task 登録簿の絶対パスのIDで保存先の外を消さない()
    {
        File.WriteAllText(_sentinel + ".json", "{}");
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = _sentinel, BoothName = "アバター", AvatarOverride = true, ImageUrl = "" },
                new AvatarRegistryEntry { ItemId = @"..\..\sentinel", BoothName = "アバター", AvatarOverride = true, ImageUrl = "" },
            ],
        });

        var requests = new List<string>();
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = true };
        var client = new BoothClient(new HttpClient(new RecordingHandler(requests)), settings, TestWait.None);
        var sync = new AvatarImageSync(_store, client, new ImagePipeline(client, _paths, settings));

        await sync.SyncAsync();

        AssertSentinelSurvives();
        Assert.Empty(requests);
        Assert.Null(AvatarImageSync.IconPath(_paths, _sentinel, item: null));
    }

    /// <summary>改変の記録の中の id に番兵の場所を書いても、一覧に出ず、消すで番兵が消えない</summary>
    [Fact]
    public async Task 改変の記録の悪いIDで保存先の外を消さない()
    {
        Directory.CreateDirectory(_paths.ModificationsDir);
        var escaped = JsonEscape(_sentinel);
        File.WriteAllText(
            Path.Combine(_paths.ModificationsDir, "mod-0000b001.json"),
            $$"""{ "id": "{{escaped}}", "avatarItemId": "9900001", "name": "改変" }""");

        var loaded = await _store.Modifications.LoadAllAsync();
        Assert.Empty(loaded.Modifications);
        Assert.Single(loaded.FailedIds);

        Assert.Throws<InvalidStoreIdException>(() => _store.Modifications.Delete(_sentinel));
        Assert.Throws<InvalidStoreIdException>(() => _store.Modifications.Delete(@"..\..\..\sentinel"));
        Assert.False(_store.Modifications.Exists(_sentinel));
        Assert.Null(await _store.Modifications.LoadAsync(_sentinel));
        AssertSentinelSurvives();

        var issues = await HandEditCheck.FindAsync(_store);
        Assert.Contains(issues, issue => issue.Where == "modifications/" && issue.What.Contains("mod-0000b001.json", StringComparison.Ordinal));
    }

    /// <summary>商品の記録の中の id に番兵の場所を書いても、一覧に出ず、外すで番兵が消えない</summary>
    [Fact]
    public async Task 商品の記録の悪いIDで保存先の外を消さない()
    {
        File.WriteAllText(
            Path.Combine(_paths.ItemsDir, "9900001.json"),
            $$"""{ "id": "{{JsonEscape(_sentinel)}}", "booth": { "name": "x" }, "local": {} }""");

        var loaded = await _store.Items.LoadAllAsync();
        Assert.DoesNotContain(loaded.Items, item => item.Id == _sentinel);

        Assert.False(await _store.Items.DeleteIfAsync(_sentinel, _ => true));
        Assert.Null(await _store.Items.LoadAsync(_sentinel));
        Assert.False(_store.Items.Exists(_sentinel));
        AssertSentinelSurvives();

        // 手で直したファイルの点検には「ファイル名と中の商品IDが違う」として出る
        var issues = await HandEditCheck.FindAsync(_store);
        Assert.Contains(issues, issue => issue.Where == "items/" && issue.What.Contains("9900001.json", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>...json</c> の名前は商品ID「..」に読める。前は外すと <c>images/..</c>＝保存先の丸ごとを消せた
    /// </summary>
    [Fact]
    public async Task ドットだけの名前のファイルを商品として扱わない()
    {
        File.WriteAllText(Path.Combine(_paths.ItemsDir, "...json"), """{ "id": "..", "booth": { "name": "x" }, "local": {} }""");
        File.WriteAllText(Path.Combine(_paths.ItemsDir, "..json"), """{ "id": ".", "booth": { "name": "x" }, "local": {} }""");
        var keep = Path.Combine(_paths.ItemsDir, "9900002.json");
        File.WriteAllText(keep, """{ "id": "9900002", "booth": { "name": "y" }, "local": {} }""");

        var loaded = await _store.Items.LoadAllAsync();
        Assert.Equal(["9900002"], loaded.Items.Select(item => item.Id));
        Assert.DoesNotContain("..", _store.Items.EnumerateItemIds());

        Assert.False(await _store.Items.DeleteIfAsync("..", _ => true));
        Assert.False(await _store.Items.DeleteIfAsync(".", _ => true));
        Assert.True(File.Exists(keep));
        Assert.True(Directory.Exists(_paths.ImagesDir));
        AssertSentinelSurvives();

        var issues = await HandEditCheck.FindAsync(_store);
        Assert.Contains(issues, issue => issue.Where == "items/" && issue.What.Contains("...json", StringComparison.Ordinal));
    }

    /// <summary>やりかけの記録（手で直せる）に書かれた形の外れた ID では、IDの変更を進めない</summary>
    [Fact]
    public async Task IDの変更は形の外れたIDで何もしない()
    {
        var keep = Path.Combine(_paths.ItemsDir, "9900003.json");
        File.WriteAllText(keep, """{ "id": "9900003", "booth": { "name": "y" }, "local": {} }""");
        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new RecordingHandler([])), settings, TestWait.None);
        var service = new ItemService(_store, client, new ImagePipeline(client, _paths, settings), settings);

        Assert.Equal(ItemIdChangeOutcome.TargetUnavailable, await service.ChangeItemIdAsync("9900003", _sentinel));
        Assert.Equal(ItemIdChangeOutcome.TargetUnavailable, await service.ResumeItemIdChangeAsync(@"..\..\sentinel", "9900003"));
        Assert.True(File.Exists(keep));
        AssertSentinelSurvives();
    }

    private static string JsonEscape(string value) => value.Replace(@"\", @"\\", StringComparison.Ordinal);

    private sealed class RecordingHandler(List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (requests)
            {
                requests.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
