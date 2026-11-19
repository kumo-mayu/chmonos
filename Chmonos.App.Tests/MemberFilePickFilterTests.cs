using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>使ったファイルを選ぶ窓の探す欄（ユーザ指示 2026-11-18）。絞っても選んだ物は残る。作り物の名前</summary>
public class MemberFilePickFilterTests
{
    private static ItemRecord ThreePackages()
        => Make.Item("1000002", "作り物の衣装").WithFiles(Make.File(@"D:\files\costume.zip") with
        {
            Contents = ["Body/Body.unitypackage", "Option/Option.unitypackage", "Hair/Hair.unitypackage"],
        });

    private static string[] Shown(MemberFileChoice choice)
        => choice.Slots[0].Entries.Where(entry => entry.IsShown).Select(entry => entry.Option.Label).ToArray();

    [Fact]
    public void 語に当たらない候補は一覧から隠れ_選ぶ物と選ばないは残る()
    {
        var model = new MemberFilePickViewModel([ThreePackages()]);
        var choice = model.Choices[0];
        Assert.True(model.ShowsFilter);

        model.FilterText = "option";
        Assert.Equal(["選ばない", "Option.unitypackage"], Shown(choice));

        model.FilterText = "";
        Assert.Equal(4, Shown(choice).Length);
    }

    [Fact]
    public void 選んだ物は_絞って見えなくなっても選んだまま残る()
    {
        var model = new MemberFilePickViewModel([ThreePackages()]);
        var choice = model.Choices[0];
        var body = choice.Slots[0].Entries.First(entry => entry.Option.Label == "Body.unitypackage");
        choice.Slots[0].SelectedEntry = body;

        model.FilterText = "hair";
        Assert.Equal("Body.unitypackage", choice.Selected.Label);
        Assert.Equal(["Body.unitypackage"], choice.Picked.Select(option => option.Label));

        // 足した欄にも同じ絞りが掛かり、選んだ物は元の欄に残る
        var added = choice.AddSlot()!;
        Assert.Contains(added.Entries, entry => entry.Option.Label == "Hair.unitypackage" && entry.IsShown);
        Assert.DoesNotContain(added.Entries, entry => entry.Option.Label == "Option.unitypackage" && entry.IsShown);
        Assert.Equal(["Body.unitypackage"], choice.Picked.Select(option => option.Label));
    }
}
