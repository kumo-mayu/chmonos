using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 登録したフォルダ（展開してある物）の中の unitypackage も、Unity へ送る候補に並べる（ユーザ判断 2026-10-05・メモ65-③）。
/// 前は候補を zip の中からしか集めず、フォルダだけの商品は送れなかった。展開はフォルダでは使えないまま。
/// Unity は動かさない：送る道は、渡す場所を決める所と、候補・記録・表記の計算までを確かめる。
/// </summary>
public sealed class UnityFolderPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-folder-package-" + Guid.NewGuid().ToString("N")[..8]);

    public UnityFolderPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 掴まれていることがある。後片付けで落ちる必要はない
        }
    }

    /// <summary>中に unitypackage を置いた、登録したフォルダの記録。</summary>
    private LocalFolderRecord Folder(string name, params string[] packages)
    {
        var path = Path.Combine(_root, name);
        foreach (var package in packages)
        {
            var file = Path.Combine(path, package.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, [1, 2, 3]);
        }

        return new LocalFolderRecord { Path = path, UnityPackages = packages };
    }

    private static ItemRecord WithFolders(ItemRecord item, params LocalFolderRecord[] folders)
        => item with { Local = item.Local with { LocalFolders = folders } };

    [Fact]
    public void 送る候補には_zipの中の物の後にフォルダの中の物が並ぶ()
    {
        var zip = Path.Combine(_root, "Sample.zip");
        File.WriteAllBytes(zip, [1]);
        var file = Make.File(zip) with { UnityPackages = [new UnityPackageSummary { Entry = "Sample.unitypackage", Roots = [] }], Contents = ["Sample.unitypackage"] };
        var item = WithFolders(Make.Item("9900701", "作り物の衣装A").WithFiles(file), Folder("衣装A", "Unity/Outfit.unitypackage"));

        var packages = UnityImportQueue.PackagesOf(item);

        Assert.Equal(["Sample", "Outfit"], packages.Select(package => package.Name));
        Assert.False(packages[0].InFolder);
        Assert.True(packages[1].InFolder);
        // 候補の表記は「パッケージ名 (zip名)」の決まりに合わせ、フォルダならフォルダ名（右クリックと検索の複数選択で共用）
        Assert.Equal(new ListChoiceItem("Outfit", "衣装A の中の Unity"), ItemFileActions.PackageLabels(packages)[1]);
    }

    [Fact]
    public void フォルダから消えたunitypackageは_候補に出さない()
    {
        var folder = Folder("衣装B", "B.unitypackage") with { UnityPackages = ["B.unitypackage", "消した.unitypackage"] };

        var packages = UnityImportQueue.PackagesOf(WithFolders(Make.Item("9900702", "作り物の衣装B").WithFiles(), folder));

        Assert.Equal(["B"], packages.Select(package => package.Name));
    }

    [Fact]
    public void フォルダの中の物は_取り出さずにそのままUnityへ渡す()
    {
        var folder = Folder("衣装C", "Unity/C.unitypackage");
        var package = UnityHandoff.PlacesOf(folder).Single().Entry;

        var path = UnityImportQueue.PathToSend(package, new TemporaryUnpacker(Path.Combine(_root, "temp")), null, CancellationToken.None);

        Assert.Equal(Path.Combine(folder.Path, "Unity", "C.unitypackage"), path);
        Assert.False(Directory.Exists(Path.Combine(_root, "temp", "packages")));
    }

    [Fact]
    public void フォルダの中の物が無ければ_zipから取り出すとは言わずに原因を言う()
    {
        var package = new UnityPackageEntry(Path.Combine(_root, "無いフォルダ"), "X.unitypackage", 0) { InFolder = true };

        var thrown = Assert.Throws<FileNotFoundException>(
            () => UnityImportQueue.PathToSend(package, new TemporaryUnpacker(Path.Combine(_root, "temp")), null, CancellationToken.None));

        Assert.Equal(FailureText.Cause(thrown), UnityImportQueue.ExtractFailureText(package, thrown));
        var inZip = package with { InFolder = false };
        Assert.StartsWith("zipから取り出せませんでした。", UnityImportQueue.ExtractFailureText(inZip, thrown));
    }

    [Fact]
    public void フォルダの中の物を送った記録は_改変から送るときもその1つを送る()
    {
        var item = WithFolders(Make.Item("9900703", "作り物の衣装D").WithFiles(), Folder("衣装D", "D1.unitypackage", "D2.unitypackage"));
        // ハッシュ無しで場所だけ（フォルダはハッシュを持たない）
        var member = new ModificationMember { ItemId = item.Id, Package = "D2.unitypackage" };

        var packages = ModificationViewModel.PackagesFor(item, member);

        Assert.Equal(["D2"], packages.Select(package => package.Name));
        Assert.True(member.HasFile);
        Assert.Equal("D2.unitypackage (衣装D)", ModificationRowBuilder.FileTextOf(member, item));
    }

    [Fact]
    public void 送る物を選ぶ窓に_フォルダの中の物がフォルダの名前の塊で並び_記録はハッシュ無しになる()
    {
        var item = WithFolders(Make.Item("9900704", "作り物の衣装E").WithFiles(), Folder("衣装E", "E1.unitypackage", "Sub/E2.unitypackage"));

        var section = PackageChoiceSection.Build(item)!;

        var group = Assert.Single(section.Groups);
        Assert.Equal("フォルダ：衣装E", group.Label);
        Assert.Equal("フォルダの中の Sub", group.Rows[1].FolderText);
        group.Rows[1].IsChecked = true;
        var member = Assert.Single(section.CheckedMembers);
        Assert.Null(member.FileHash);
        Assert.Equal("Sub/E2.unitypackage", member.Package);
    }
}
