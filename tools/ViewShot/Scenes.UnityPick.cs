using Chmonos.App.ViewModels;
using Chmonos.Core.Services;

namespace ViewShot;

internal static partial class Scenes
{
    // 「Unityで選択」の木の窓（ユーザ判断 2026-11-13：候補が平らに大量に並んだのをフォルダの木にする）。
    // 平らになる場合（8件以下）は今までの ListChoice の窓のままなので、ここでは描かない（窓を出す作りで台に載らない）。
    // フォルダ名・画像名・zip 名は作り物
    private static IEnumerable<Scene> UnityPick =>
    [
        // 報告の形に近い少数：アバター本体の包みを1つ入れて、画像8（うち1つは名前だけ同じ）と unitypackage 1
        UnityPickScene(
            "unity-pick-tree-few",
            "Unityで選択の木：画像8（名前が同じ1つ）・unitypackage 1（節に分かれ、全部開く）",
            [
                PickSame("Assets/SampleShop/SampleAvatar/Textures/Body/body.png"),
                PickSame("Assets/SampleShop/SampleAvatar/Textures/Body/body_normal.png"),
                PickSame("Assets/SampleShop/SampleAvatar/Textures/Body/body_mask.png"),
                PickSame("Assets/SampleShop/SampleAvatar/Textures/Face/face.png"),
                PickSame("Assets/SampleShop/SampleAvatar/Textures/Face/eye_iris.png"),
                PickNamed("Assets/SampleShop/SampleAvatar/Textures/Face/eye_highlight.png"),
                PickSame("Assets/SampleShop/SampleAvatar/Textures/Hair/hair.png"),
                PickSame("Assets/SampleShop/SampleAvatar/Thumbnail.png"),
            ],
            [new UnityPackageEntry(@"C:\dl\SampleAvatar_v1.2.zip", "SampleAvatar_v1.2.unitypackage", 1)]),

        // 数十件：色違いの包みを入れたプロジェクト。6色 × 8枚と、長い名前の画像。大きいフォルダは畳んで始まる
        UnityPickScene(
            "unity-pick-tree-many",
            "Unityで選択の木：色違い6フォルダ × 8枚＋長い名前・unitypackage 3（大きいフォルダは畳む）",
            [
                .. new[] { "Black", "White", "Pink", "Blue", "Mint", "Lavender" }.SelectMany(color =>
                    Enumerable.Range(1, 8).Select(i => i == 3
                        ? PickNamed($"Assets/SampleShop/SampleOutfit/Textures/{color}/outfit_{color.ToLowerInvariant()}_{i:00}.png")
                        : PickSame($"Assets/SampleShop/SampleOutfit/Textures/{color}/outfit_{color.ToLowerInvariant()}_{i:00}.png"))),
                PickSame("Assets/SampleShop/SampleOutfit/Textures/Common/VeryLongTextureNameWithoutAnySpacesForTheOutfitEmissionMask_2048.png"),
                PickSame("Assets/SampleShop/SampleOutfit/Thumbnail.png"),
            ],
            [
                new UnityPackageEntry(@"C:\dl\SampleOutfit_Black.zip", "SampleOutfit_Black.unitypackage", 1),
                new UnityPackageEntry(@"C:\dl\SampleOutfit_White.zip", "SampleOutfit_White.unitypackage", 1),
                new UnityPackageEntry(@"C:\dl\SampleOutfit_AllColors_VeryLongZipNameWithoutAnySpaces.zip", "SampleOutfit_Pink.unitypackage", 1),
            ]),

        // 1つのフォルダに収まる12枚で unitypackage 無し：節は無く、頭のフォルダ1行の下に名前だけ並ぶ
        UnityPickScene(
            "unity-pick-tree-one-folder",
            "Unityで選択の木：1つのフォルダに12枚・unitypackage 無し（節なし・名前だけ並ぶ）",
            [.. Enumerable.Range(1, 12).Select(i => PickSame($"Assets/SampleShop/SampleAccessory/Textures/accessory_{i:00}.png"))],
            []),
    ];

    private static UnityImageMatch PickSame(string path) => new(path, System.IO.Path.GetFileName(path), UnityImageMatchKind.SameImage);

    private static UnityImageMatch PickNamed(string path) => new(path, System.IO.Path.GetFileName(path), UnityImageMatchKind.SameName);

    private static Scene UnityPickScene(string name, string title, IReadOnlyList<UnityImageMatch> images, IReadOnlyList<UnityPackageEntry> packages)
        => new(name, title, context =>
        {
            var window = new Chmonos.App.Views.UnityPickDialog(new UnityPickDialogViewModel("Unityで選択", images, packages));
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        };
}
