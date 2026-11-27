using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Query.Tests;

/// <summary>
/// 商品の答えに 2026-11-27 に足した欄（バリエーションの向き先・変換扱い・中身の更新の控え・更新を知らせるか。
/// <c>docs/spec/local-api-shapes.md</c>「出す物を決めた」）。形そのものは見本（<c>Golden/item-*.json</c>）で見るので、ここは決まりの分かれ目を見る。
/// 名前・ID は作り物。
/// </summary>
public sealed class ItemAnswerConversionTests : IDisposable
{
    private const string ProfileHash = "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-query-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task<DataStore> StoreAsync()
    {
        var store = new DataStore(new AppPaths(_root));

        // プロファイルのファイルを含む商品（変換扱いは何も決めていない）
        new UnityPackagePathStore(store.Paths).Add(ProfileHash, "Profile.unitypackage",
        [
            new UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2sample.json"),
        ]);
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9900001",
            Booth = new BoothBlock { Name = "作り物のプロファイル" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = ProfileHash,
                        Paths = [@"D:\fake\profile.zip"],
                        SizeBytes = 1,
                        Contents = ["Profile.unitypackage"],
                        UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                    },
                ],
            },
        });

        // 何も持たない商品（変換扱い・中身の更新の控え・向き先のどれも無い）
        await store.Items.SaveAsync(new ItemRecord { Id = "9900002", Booth = new BoothBlock { Name = "作り物の衣装" } });

        // 向き先：11 は検出だけ、12 は人が全部外しただけの行（検出の行を消す）、13 は人だけ。
        // 「バリエーションを選ばない購入」の行（null）は保存すると読めなくなる（2026-11-27 に見つけた。直すかは別に決める）ので、Core の試験で見る
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9900003",
            Booth = new BoothBlock { Name = "作り物の衣装2" },
            Local = new LocalBlock
            {
                VariationTargets =
                [
                    new VariationTarget { VariationId = 12, RejectedAvatars = ["7000009"] },
                    new VariationTarget { VariationId = 13, Avatars = ["7000008"] },
                ],
                DetectedVariationTargets =
                [
                    new VariationTarget { VariationId = 11, Avatars = ["7000007"] },
                    new VariationTarget { VariationId = 12, Avatars = ["7000009"] },
                ],
            },
        });

        return store;
    }

    private static JsonElement Json(ItemDetail detail) => JsonDocument.Parse(QueryJson.Serialize(detail)).RootElement;

    [Fact]
    public async Task 自動の判定は_中のプロファイルのファイルを見る()
    {
        var snapshot = await QuerySnapshot.LoadAsync(await StoreAsync());

        var profile = ItemMapper.Detail(snapshot.ItemsById["9900001"], snapshot).Conversion;
        Assert.Equal("auto", profile.Mode);
        Assert.True(profile.HasProfileFiles);
        Assert.Empty(profile.Targets);

        var plain = ItemMapper.Detail(snapshot.ItemsById["9900002"], snapshot).Conversion;
        Assert.Equal("auto", plain.Mode);
        Assert.False(plain.HasProfileFiles);
    }

    [Fact]
    public async Task 合わせた向き先は_人が外しただけの行で検出の行を消す()
    {
        var snapshot = await QuerySnapshot.LoadAsync(await StoreAsync());
        var detail = ItemMapper.Detail(snapshot.ItemsById["9900003"], snapshot);

        Assert.Equal([((long?)11, "7000007"), (13, "7000008")], detail.VariationTargets.Select(target => (target.VariationId, target.Avatars.Single())));
        Assert.All(detail.VariationTargets, target => Assert.Null(target.RejectedAvatars));
        Assert.Null(detail.VariationTargetSources);

        // 出どころ別は保存のまま（外しただけの行も、外した物つきで）
        var sources = ItemMapper.Detail(snapshot.ItemsById["9900003"], snapshot, Includes.ParseItem("variationTargets")).VariationTargetSources!;
        Assert.Equal([(long?)12, 13], sources.Decided.Select(target => target.VariationId));
        Assert.Equal(["7000009"], sources.Decided[0].RejectedAvatars!);
        Assert.Equal([(long?)11, 12], sources.Detected.Select(target => target.VariationId));
        Assert.Empty(sources.Detected[0].RejectedAvatars!);
    }

    [Fact]
    public async Task 名前から見つけた向き先を使わない設定では_人が決めた行だけを合わせる()
    {
        var store = await StoreAsync();
        await store.Settings.UpdateAsync(settings => settings with { UseDetectedVariationTargets = false });
        var snapshot = await QuerySnapshot.LoadAsync(store);

        var detail = ItemMapper.Detail(snapshot.ItemsById["9900003"], snapshot, Includes.ParseItem("variationTargets"));
        Assert.Equal([(long?)13], detail.VariationTargets.Select(target => target.VariationId));

        // 出どころ別は設定に関わらず検出の行も返す
        Assert.Equal(2, detail.VariationTargetSources!.Detected.Count);
    }

    [Fact]
    public async Task 中身の更新の控えは_頼んだときだけ欄を出し_見つけていなければ日時をnullにする()
    {
        var snapshot = await QuerySnapshot.LoadAsync(await StoreAsync());
        var item = snapshot.ItemsById["9900002"];

        var plain = Json(ItemMapper.Detail(item, snapshot));
        Assert.False(plain.TryGetProperty("contentUpdate", out _));
        Assert.False(plain.TryGetProperty("variationTargetSources", out _));
        Assert.True(plain.GetProperty("notifyOnUpdate").GetBoolean());
        Assert.Equal(JsonValueKind.Array, plain.GetProperty("variationTargets").ValueKind);

        var asked = Json(ItemMapper.Detail(item, snapshot, Includes.ParseItem("contentUpdate,variationTargets")));
        var update = asked.GetProperty("contentUpdate");
        Assert.Equal(JsonValueKind.Null, update.GetProperty("foundAt").ValueKind);
        Assert.False(update.GetProperty("hasNewerFiles").GetBoolean());
        Assert.Equal(0, asked.GetProperty("variationTargetSources").GetProperty("decided").GetArrayLength());
        Assert.Equal(0, asked.GetProperty("variationTargetSources").GetProperty("detected").GetArrayLength());
    }
}
