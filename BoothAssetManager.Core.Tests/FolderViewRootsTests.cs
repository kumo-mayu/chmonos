using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>フォルダビューの根の決め方（docs/history/folder-view.md §2）。置き方は作り物。</summary>
public sealed class FolderViewRootsTests
{
    /// <summary>根の場所を文字コードの順に並べて返す（並べ方を文化圏の順に左右させない）。</summary>
    private static IReadOnlyList<string> RootPaths(params string[] folders)
        => FolderViewRoots.Roots(folders)
            .Select(root => root.IsLooseBucket ? root.Path + "（直下など）" : root.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void 深い祖先の下に枝が並ぶなら1つの根()
    {
        // 友人のデータの形。枝ごとに根を割らない
        Assert.Equal([@"E:\VRChat\Assets"], RootPaths(
            @"E:\VRChat\Assets\衣装\A", @"E:\VRChat\Assets\髪\B", @"E:\VRChat\Assets\ギミック\C"));
    }

    [Fact]
    public void 祖先がドライブの直下なら1段下で分ける()
    {
        // 本人のデータの形。「D:」を根にすると意味の無い段が出る
        Assert.Equal([@"D:\storage\VRChat_model", @"D:\work\x\DLforTest"], RootPaths(
            @"D:\storage\VRChat_model", @"D:\work\x\DLforTest"));
    }

    [Fact]
    public void ユーザのフォルダの下はデスクトップとダウンロードとドキュメントで分ける()
    {
        Assert.Equal([@"C:\Users\taro\Desktop", @"C:\Users\taro\Documents", @"C:\Users\taro\Downloads"], RootPaths(
            @"C:\Users\taro\Downloads", @"C:\Users\taro\Downloads\A", @"C:\Users\taro\Desktop\B\B", @"C:\Users\taro\Desktop\C",
            @"C:\Users\taro\Documents\Unity\P\Assets", @"C:\Users\taro\Documents\X"));
    }

    [Fact]
    public void 置き場所が1か所だけならその場所が根()
    {
        // 共通の祖先は置き場所そのもの。途中の段は画面で「 › 」でつないで見せる
        Assert.Equal([@"C:\Users\taro\Documents\Unity\P\Assets"], RootPaths(@"C:\Users\taro\Documents\Unity\P\Assets"));
    }

    [Fact]
    public void よく知られた置き場は1か所でも畳まない()
    {
        // 置き場所が1か所ずつのデスクトップとダウンロード。畳むと根から消えた
        Assert.Equal([@"C:\Users\hana\Desktop", @"C:\Users\hana\Downloads"], RootPaths(
            @"C:\Users\hana\Desktop", @"C:\Users\hana\Downloads"));
    }

    [Fact]
    public void 境目の直下の小さなフォルダは直下などにまとめる()
    {
        // ドライブの直下に直接置いた物と、1か所しか無いフォルダ。整理した枝は別の根
        var roots = FolderViewRoots.Roots([@"G:", @"G:\Hair_5", @"G:\Ring_347", @"G:\VRC\整理済\A", @"G:\VRC\整理済\B"]);

        Assert.Equal(2, roots.Count);
        var loose = Assert.Single(roots, root => root.IsLooseBucket);
        Assert.Equal("G:", loose.Path);
        Assert.Equal([@"G:\Hair_5", @"G:\Ring_347"], loose.LooseFolders.Order().ToList());
        Assert.Equal(@"G:\VRC\整理済", Assert.Single(roots, root => !root.IsLooseBucket).Path);
    }

    [Fact]
    public void ボリュームが違えば別の根()
    {
        Assert.Equal([@"C:\Users\ken\Downloads", @"E:\BOOTH", @"\\nas\share\VRC"], RootPaths(
            @"E:\BOOTH\A", @"E:\BOOTH\B", @"C:\Users\ken\Downloads", @"\\nas\share\VRC\X", @"\\nas\share\VRC\Y"));
    }

    [Fact]
    public void 共有の直下とOneDriveの根は境目()
    {
        Assert.True(FolderViewRoots.IsHardBoundary(@"\\nas\share"));
        Assert.True(FolderViewRoots.IsHardBoundary(@"C:\Users\taro\OneDrive", @"C:\Users\taro\OneDrive"));
        Assert.True(FolderViewRoots.IsHardBoundary(@"C:\Users\taro\AppData\Local"));
        Assert.False(FolderViewRoots.IsHardBoundary(@"C:\Users\taro\Downloads"));
    }

    [Fact]
    public void ボリュームの見分け()
    {
        Assert.Equal("D:", FolderViewRoots.VolumeOf(@"D:\a\b"));
        Assert.Equal(@"\\nas\share", FolderViewRoots.VolumeOf(@"\\nas\share\a\b"));
    }
}
