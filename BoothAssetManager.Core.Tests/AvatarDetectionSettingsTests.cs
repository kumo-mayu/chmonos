using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 見出し語を既定に足したとき、保存済みの設定を使う人にも届くこと。
///
/// 見出し語の一覧は settings.json に丸ごと保存されるので、既定に語を足しただけでは
/// 今の利用者の検出は古い一覧のまま動く。使うときに既定と合わせる。
/// </summary>
public class AvatarDetectionSettingsTests : IDisposable
{
    private const string AvatarId = "1111";
    private const string ItemId = "999";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public AvatarDetectionSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-headings-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

    /// <summary>
    /// 「🔍検索用🔍」の見出しは 2026-09-11 に既定へ足した語。
    /// それ以前の一覧が保存された設定でも、この見出しの下のアバターを対応として拾う。
    /// </summary>
    [Fact]
    public async Task ReadsNewDefaultHeadingsEvenWithOldSavedSettings()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    BoothName = "オリジナル3Dモデル「マヌカ」",
                    DisplayName = "マヌカ",
                    Category = "3Dキャラクター",
                    CheckedAt = DateTimeOffset.Now,
                },
            ],
        });

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "ネイルチップ" },
            Local = new LocalBlock(),
        });

        File.WriteAllText(
            _paths.ItemHtmlFile(ItemId),
            $"<h2>🔍 検索用 🔍</h2><p>https://booth.pm/ja/items/{AvatarId}</p>");

        var oldSettings = new AppSettings
        {
            AvatarSupportHeadings = ["対応アバター", "対応モデル", "対応リスト", "対応表", "対応一覧", "Supported", "Compatible"],
        };

        await new AvatarService(_store, oldSettings).DetectAsync();

        var item = await _store.Items.LoadAsync(ItemId);
        var link = Assert.Single(item!.Local.Avatars);
        Assert.Equal(AvatarId, link.AvatarItemId);
        Assert.Equal(AvatarLinkSource.SupportSection, link.Source);
        Assert.True(link.Confirmed);
    }
}
