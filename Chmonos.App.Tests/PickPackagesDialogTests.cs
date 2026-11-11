using System.IO;
using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 1つの商品に unitypackage が2つ以上あるときに選ぶ窓（ユーザ指示 2026-11-03）：いくつでも選べ、名前で探して、
/// 見えている物をまとめて選ぶ・外す。最初は何も選んでいない。
/// カードの右クリックの「Unityへ送る」「改変に追加して送る」、検索の複数選択の「Unityへ順に送る」、改変の画面で同じ窓を使う。
/// Unity は動かさない（送り先が要る所は、閉じた送り先で「送れなかった」側を通す）。
/// </summary>
public sealed class PickPackagesDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-pick-packages-" + Guid.NewGuid().ToString("N")[..8]);

    public PickPackagesDialogTests() => Directory.CreateDirectory(_root);

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

    /// <summary>中に unitypackage を置いた、登録したフォルダを持つ商品（zip を作らずに候補を並べられる）。</summary>
    private ItemRecord ItemWith(string id, string folderName, params string[] packages)
    {
        var path = Path.Combine(_root, folderName);
        foreach (var package in packages)
        {
            var file = Path.Combine(path, package.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, [1, 2, 3]);
        }

        var item = Make.Item(id, "作り物の衣装").WithFiles();
        return item with
        {
            Local = item.Local with { LocalFolders = [new LocalFolderRecord { Path = path, UnityPackages = packages }] },
        };
    }

    private PickPackagesDialogViewModel Dialog(params string[] packages)
    {
        var section = PackageChoiceSection.Build(ItemWith("9900801", "衣装", packages), FileOrder.Added)!;
        return new PickPackagesDialogViewModel("Unityへ送る", "Unityへ送るunitypackageを選んでください。", [section], othersCount: 0, records: false);
    }

    [Theory]
    [InlineData(FileOrder.Added, new[] { "Sample_v10", "Sample_v2", "Base" })]
    [InlineData(FileOrder.Name, new[] { "Base", "Sample_v10", "Sample_v2" })]
    [InlineData(FileOrder.NameWithVersion, new[] { "Base", "Sample_v2", "Sample_v10" })]
    public void 候補は設定のファイルの表示順に並ぶ(FileOrder order, string[] expected)
    {
        // 設定の「ファイルの表示順」（ユーザ判断 2026-11-11）。商品ページの手元のファイルと同じ順。記録の順は v10・v2・Base
        var folder = Path.Combine(_root, "単体");
        Directory.CreateDirectory(folder);
        var files = new[] { "Sample_v10.unitypackage", "Sample_v2.unitypackage", "Base.unitypackage" }
            .Select(name =>
            {
                var path = Path.Combine(folder, name);
                File.WriteAllBytes(path, [1, 2, 3]);
                return new LocalFileRecord { Hash = Make.HashOf(name), Paths = [path], SizeBytes = 3 };
            })
            .ToList();
        var item = Make.Item("9900805", "作り物の衣装");
        item = item with { Local = item.Local with { LocalFiles = files } };

        var section = PackageChoiceSection.Build(item, order)!;

        Assert.Equal(expected, section.Groups.SelectMany(group => group.Rows).Select(row => row.Package.Name));
    }

    private static IEnumerable<PackageChoiceRow> Rows(PickPackagesDialogViewModel model)
        => model.Sections.SelectMany(section => section.Groups).SelectMany(group => group.Rows);

    [Fact]
    public void 最初は何も選んでおらず_送るは押せず_理由を言う()
    {
        var model = Dialog("Body.unitypackage", "Shader.unitypackage", "Extra/Hair.unitypackage");

        Assert.All(Rows(model), row => Assert.False(row.IsChecked));
        Assert.False(model.CanCommit);
        Assert.Equal("送るものを1つ以上選んでください。", model.CommitHint);
    }

    [Fact]
    public void 探すと_名前か中の場所に当たる物だけ見え_当たらなければそう言う()
    {
        var model = Dialog("Body.unitypackage", "Shader.unitypackage", "Extra/Hair.unitypackage");

        model.FilterText = "extra";
        Assert.Equal(["Hair"], Rows(model).Where(row => row.IsVisible).Select(row => row.Name));
        Assert.False(model.HasNoMatch);

        // かなの種類・全角半角・大文字小文字は区別しない（ほかの窓の探す欄と同じ）
        model.FilterText = "ＳＨＡＤＥＲ";
        Assert.Equal(["Shader"], Rows(model).Where(row => row.IsVisible).Select(row => row.Name));

        model.FilterText = "見当たらない語";
        Assert.True(model.HasNoMatch);
        Assert.False(Assert.Single(model.Sections).IsVisible);

        model.ClearFilterCommand.Execute(null);
        Assert.All(Rows(model), row => Assert.True(row.IsVisible));
    }

    [Fact]
    public void すべて選択と選択を解除は_見えている物だけに効く()
    {
        var model = Dialog("Body.unitypackage", "Body_Quest.unitypackage", "Shader.unitypackage");

        model.FilterText = "body";
        model.SelectAllCommand.Execute(null);
        Assert.Equal(["Body", "Body_Quest"], Rows(model).Where(row => row.IsChecked).Select(row => row.Name));

        // 絞りを外して残りを選び、また絞って外すと、見えていない Shader は選んだまま
        model.FilterText = string.Empty;
        Rows(model).Single(row => row.Name == "Shader").IsChecked = true;
        model.FilterText = "quest";
        model.ClearSelectionCommand.Execute(null);
        Assert.Equal(["Body", "Shader"], Rows(model).Where(row => row.IsChecked).Select(row => row.Name));

        // 見えていない物も選んだまま送る数に入る
        Assert.True(model.CanCommit);
        Assert.Equal("2 件を選んでいます。", model.SummaryText);
    }

    [Fact]
    public void 選んだ物は_押した順ではなく見えている順に並ぶ()
    {
        var model = Dialog("A.unitypackage", "B.unitypackage", "C.unitypackage");
        var rows = Rows(model).ToList();

        rows[2].IsChecked = true;
        rows[0].IsChecked = true;

        Assert.Equal(["A", "C"], Assert.Single(model.Sections).CheckedPackages.Select(package => package.Name));
    }

    [Fact]
    public Task カードから送るときは_選ぶ窓で選んだ物を全部返す() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var item = ItemWith("9900802", "衣装2", "Body.unitypackage", "Shader.unitypackage", "Hair.unitypackage");
        app.PickPackages = model =>
        {
            Assert.All(Rows(model), row => Assert.False(row.IsChecked));
            Assert.Equal("送る", model.CommitText);
            foreach (var row in Rows(model).Where(row => row.Name != "Shader"))
            {
                row.IsChecked = true;
            }

            return true;
        };

        var picked = await ItemFileActions.PickPackagesAsync(item, "Unityへ送る", "Unityへ送るunitypackageを選んでください。", "送る", FileOrder.Added);

        Assert.Single(app.PackagePicks);
        Assert.Equal(["Body", "Hair"], picked!.Select(package => package.Name));
    });

    [Fact]
    public Task 候補が1つなら_聞かずにそれを返し_やめたらnull() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        var single = await ItemFileActions.PickPackagesAsync(
            ItemWith("9900803", "衣装3", "Only.unitypackage"), "Unityへ送る", "選んでください。", "送る", FileOrder.Added);
        Assert.Equal(["Only"], single!.Select(package => package.Name));
        Assert.Empty(app.PackagePicks);

        // 既定の答えは「キャンセル」
        var cancelled = await ItemFileActions.PickPackagesAsync(
            ItemWith("9900804", "衣装4", "A.unitypackage", "B.unitypackage"), "Unityへ送る", "選んでください。", "送る", FileOrder.Added);
        Assert.Null(cancelled);
    });

    [Fact]
    public Task 選んだ2つ以上を送れなかったら_1回の警告の窓で物と理由を並べる() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var item = ItemWith("9900805", "衣装5", "Body.unitypackage", "Shader.unitypackage");
        var packages = Services.UnityImportQueue.PackagesOf(item);

        // 送り先は、決して動いていない PID（UnitySendStopTests と同じ）
        var closed = new OpenUnityEditor(int.MaxValue, "作り物のプロジェクト");
        await ItemUnityActions.SendPickedAsync(app.Services, item, closed, packages, "Unityへ送る", null, null);
        await app.SettleAsync();

        var notice = Assert.Single(app.Notices);
        Assert.Equal(MessageBoxImage.Warning, notice.Icon);
        Assert.Contains("2 件は送れませんでした", notice.Text);
        Assert.Contains("・Body：", notice.Text);
        Assert.Contains("・Shader：", notice.Text);
        Assert.Empty(app.Toasts);
    });
}
